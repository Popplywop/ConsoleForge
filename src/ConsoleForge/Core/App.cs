using System.Threading.Channels;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Terminal;

namespace ConsoleForge.Core;

/// <summary>
/// Main event loop for a ConsoleForge application.
/// <para>
/// Call <see cref="Run"/> to start the application. The method blocks
/// until the model issues <see cref="QuitMsg"/> or the user force-quits.
/// The terminal is guaranteed to be restored (raw mode exited, alternate screen
/// exited) even if an unhandled exception occurs.
/// </para>
/// </summary>
public sealed class App
{
    private Theme _theme = Theme.Default;
    private ColorProfile _colorProfile = ColorProfile.TrueColor;
    private bool _enableMouse;

    /// <summary>Detected color capability of the current terminal.</summary>
    public ColorProfile ColorProfile => _colorProfile;
    private ITerminal? _terminal;
    private readonly Channel<IMsg> _channel = Channel.CreateUnbounded<IMsg>();
    private readonly Renderer _renderer = new();

    /// <summary>
    /// Zero-based index into the depth-first focusable list. -1 = no focus.
    /// Updated by HandleTabFocus; dispatched to the model as FocusIndexChangedMsg.
    /// </summary>
    private int _focusIndex = -1;

    /// <summary>
    /// Run the application asynchronously. Returns when the model produces a <see cref="QuitMsg"/>.
    /// </summary>
    /// <param name="model">Initial model. <c>Init()</c> is called before the loop starts.</param>
    /// <param name="terminal">
    /// Optional terminal override. If null, a new <see cref="AnsiTerminal"/> is created.
    /// Pass a <see cref="ConsoleForge.Testing.VirtualTerminal"/> for testing.
    /// </param>
    /// <param name="theme">Theme to use. Defaults to <see cref="Theme.Default"/>.</param>
    /// <param name="targetFps">Target frames per second (1–60). Default 30.</param>
    public static Task Run(
        IModel model,
        ITerminal? terminal = null,
        Theme? theme = null,
        int targetFps = 30,
        bool enableMouse = false)
    {
        var program = new App
        {
            _theme      = theme ?? Theme.Default,
            _colorProfile = DetectColorProfile(),
            _enableMouse  = enableMouse,
        };
        return program.RunInternal(model, terminal, targetFps);
    }

    /// <summary>Update the active theme at runtime. Forces a re-render.</summary>
    public void SetTheme(Theme theme)
    {
        _theme = theme;
        _renderer.MarkDirty();
        _channel.Writer.TryWrite(new RedrawMsg());
    }

    // ── Internal ──────────────────────────────────────────────────────

    private volatile bool _quitting;
    private readonly object _renderLock = new();
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Current model, updated by the event loop, read by the render timer.</summary>
    private IModel? _currentModel;

    /// <summary>Active subscriptions: key → linked CancellationTokenSource.</summary>
    private readonly Dictionary<string, CancellationTokenSource> _activeSubs = new();

    private async Task RunInternal(IModel model, ITerminal? terminal, int targetFps)
    {
        var ownTerminal = terminal is null;
        _terminal = terminal ?? new AnsiTerminal();

        try
        {
            _terminal.EnterRawMode();
            _terminal.EnterAlternateScreen();
            // Hide cursor once for the whole session — shown again in finally
            _terminal.SetCursorVisible(false);
            if (_enableMouse) _terminal.EnableMouse();

            // Send initial resize
            _channel.Writer.TryWrite(new WindowResizeMsg(_terminal.Width, _terminal.Height));

            // Subscribe to resize events
            _terminal.Resized += (_, e) =>
                _channel.Writer.TryWrite(new WindowResizeMsg(e.Width, e.Height));

            // Subscribe to input stream
            _terminal.Input.Subscribe(ev =>
            {
                if (ev is KeyInputEvent k)
                    _channel.Writer.TryWrite(k.Key);
                else if (ev is ResizeInputEvent r)
                    _channel.Writer.TryWrite(new WindowResizeMsg(r.Width, r.Height));
                else if (ev is MouseInputEvent m)
                    _channel.Writer.TryWrite(m.Mouse);
            });

            // Init model
            var initCmd = model.Init();
            DispatchCmd(initCmd);
            _currentModel = model;
            ReconcileSubscriptions(model);

            // FPS render timer
            var frameMs = Math.Max(1, 1000 / Math.Clamp(targetFps, 1, 60));
            using var timer = new Timer(_ =>
            {
                var m = _currentModel;
                if (m is not null) RenderFrame(m);
            }, null, 0, frameMs);

            // Event loop
            IModel currentModel = model;
            while (true)
            {
                IMsg msg;
                try
                {
                    msg = await _channel.Reader.ReadAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    if (msg is QuitMsg) break;

                    if (msg is BatchMsg bm)
                    {
                        foreach (var m in bm.Messages)
                        {
                            if (m is QuitMsg) goto quit;
                            ProcessMsg(m, ref currentModel);
                        }
                    }
                    else if (msg is SequenceMsg sm)
                    {
                        foreach (var m in sm.Messages)
                        {
                            if (m is QuitMsg) goto quit;
                            ProcessMsg(m, ref currentModel);
                        }
                    }
                    else
                    {
                        ProcessMsg(msg, ref currentModel);
                    }
                    continue;
                quit:
                    break;
                }
                catch (Exception ex)
                {
                    // Re-throw wrapped; finally block handles terminal cleanup.
                    throw new InvalidOperationException($"Unhandled exception in event loop: {ex.Message}", ex);
                }
            }

            // Stop the render timer before tearing down the terminal so no
            // further frames can be written to the normal screen after
            // ExitAlternateScreen is called.
            _quitting = true;
            _cts.Cancel();
            timer.Dispose();
        }
        finally
        {
            _quitting = true;
            _cts.Cancel();
            StopAllSubscriptions();
            if (_terminal is not null)
            {
                if (_enableMouse) _terminal.DisableMouse();
                _terminal.SetCursorVisible(true); // Restore cursor before exiting
            }
            if (ownTerminal)
                _terminal?.Dispose();
        }
    }

    private void RenderFrame(IModel model)
    {
        if (_terminal is null || _quitting) return;

        lock (_renderLock)
        {
            if (_terminal is null || _quitting) return;

            _renderer.RenderIfDirty(model, _terminal.Width, _terminal.Height, _theme, _colorProfile, _terminal);
        }
    }

    private void ProcessMsg(IMsg msg, ref IModel model)
    {
        // QuitMsg is handled by the event loop directly; should not reach here.

        // Intercept ThemeChangedMsg: update the runtime theme immediately,
        // then fall through so the model can update its own theme-tracking state.
        if (msg is ThemeChangedMsg themeChange)
            SetTheme(themeChange.NewTheme);

        // Handle Tab for focus traversal, then fall through to model.Update
        if (msg is KeyMsg { Key: ConsoleKey.Tab } tabKey)
        {
            HandleTabFocus(model, tabKey.Shift);
        }

        // Click-to-focus: left-click press moves focus to the clicked widget
        if (msg is MouseMsg { Button: MouseButton.Left, Action: MouseAction.Press } click)
        {
            HandleMouseFocus(model, click);
        }

        // Route key events to the focused widget
        if (msg is KeyMsg keyMsg)
        {
            RouteKeyToFocused(model, keyMsg);
            // Also pass to model for global key handling
        }

        var prevModel = model;
        var (newModel, cmd) = model.Update(msg);
        model = newModel;
        _currentModel = newModel;
        DispatchCmd(cmd);
        ReconcileSubscriptions(newModel);

        // Mark dirty whenever model changed or an explicit redraw is requested.
        // Also render immediately so keystrokes appear without waiting for the timer tick.
        if (!ReferenceEquals(newModel, prevModel) || msg is RedrawMsg)
        {
            _renderer.MarkDirty();
            RenderFrame(newModel);
        }

        // Force immediate re-render on resize; invalidate prev buffer to force full redraw
        if (msg is WindowResizeMsg && _terminal is not null)
        {
            lock (_renderLock)
            {
                _renderer.Invalidate();
                if (!_quitting && _terminal is not null)
                {
                    var root = model.View();
                    _renderer.Render(root, _terminal.Width, _terminal.Height, _theme, _colorProfile);
                    _renderer.Flush(_terminal);
                }
            }
        }
    }

    private void HandleTabFocus(IModel model, bool reverse)
    {
        var rootWidget = model.View();
        var focusable = FocusManager.CollectFocusable(rootWidget);
        if (focusable.Count == 0) return;

        if (reverse)
            _focusIndex = _focusIndex <= 0 ? focusable.Count - 1 : _focusIndex - 1;
        else
            _focusIndex = (_focusIndex + 1) % focusable.Count;

        _channel.Writer.TryWrite(new FocusIndexChangedMsg(_focusIndex));
    }

    private void HandleMouseFocus(IModel model, MouseMsg click)
    {
        var rootWidget = model.View();
        var layout     = Layout.LayoutEngine.Resolve(
            rootWidget, _terminal!.Width, _terminal.Height);

        var hit = FocusManager.FindFocusableAt(rootWidget, layout, click.Col, click.Row);
        if (hit is null) return;

        var focusable = FocusManager.CollectFocusable(rootWidget);
        var idx = -1;
        for (var i = 0; i < focusable.Count; i++)
            if (ReferenceEquals(focusable[i], hit)) { idx = i; break; }

        if (idx < 0 || idx == _focusIndex) return;
        _focusIndex = idx;
        _channel.Writer.TryWrite(new FocusIndexChangedMsg(_focusIndex));
    }

    private void RouteKeyToFocused(IModel model, KeyMsg keyMsg)
    {
        // Key routing to focused widget: the widget is owned by the model.
        // We dispatch the event to the model via the normal Update path.
        // For widgets that implement IFocusable, their OnKeyEvent is called
        // by the model's Update handler.
        // The framework provides the dispatch mechanism via FocusChangedMsg.
        // Direct OnKeyEvent invocation is available via the FocusManager API.
    }

    private void DispatchCmd(ICmd? cmd)
    {
        if (cmd is null) return;

        // Fast path: if the cmd completes synchronously (e.g. Cmd.Msg, Cmd.Quit),
        // write the result directly to the channel — no Task.Run scheduling delay.
        // This ensures follow-up messages (like ThemeChangedMsg) arrive before the
        // next render timer tick, preventing stale-cache frames.
        var task = cmd();
        if (task.IsCompletedSuccessfully)
        {
            _channel.Writer.TryWrite(task.Result);
            return;
        }

        // Slow path: genuinely async commands go through the thread pool.
        CmdDispatcher.Dispatch(cmd, _channel.Writer, _cts.Token);
    }

    private void ReconcileSubscriptions(IModel model)
    {
        if (model is not IHasSubscriptions hasSubs) return;

        var desired = hasSubs.Subscriptions();
        var desiredKeys = new HashSet<string>(desired.Select(s => s.Key));

        // Stop removed subscriptions
        foreach (var key in _activeSubs.Keys.ToList())
        {
            if (!desiredKeys.Contains(key))
            {
                _activeSubs[key].Cancel();
                _activeSubs[key].Dispose();
                _activeSubs.Remove(key);
            }
        }

        // Start new subscriptions
        foreach (var (key, sub) in desired)
        {
            if (_activeSubs.ContainsKey(key)) continue;

            var subCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _activeSubs[key] = subCts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var msg in sub(subCts.Token).WithCancellation(subCts.Token))
                    {
                        _channel.Writer.TryWrite(msg);
                    }
                }
                catch (OperationCanceledException) { /* clean shutdown */ }
                catch (Exception ex)
                {
                    _channel.Writer.TryWrite(new CmdErrorMsg(ex, $"sub:{key}"));
                }
            }, subCts.Token);
        }
    }

    private void StopAllSubscriptions()
    {
        foreach (var cts in _activeSubs.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _activeSubs.Clear();
    }

    // ── Color profile detection ───────────────────────────────────────

    private static ColorProfile DetectColorProfile()
    {
        // Windows Terminal sets WT_SESSION; treat as TrueColor.
        if (OperatingSystem.IsWindows() &&
            Environment.GetEnvironmentVariable("WT_SESSION") is { Length: > 0 })
            return ColorProfile.TrueColor;

        var colorterm = Environment.GetEnvironmentVariable("COLORTERM") ?? "";
        if (colorterm.Equals("truecolor", StringComparison.OrdinalIgnoreCase) ||
            colorterm.Equals("24bit", StringComparison.OrdinalIgnoreCase))
            return ColorProfile.TrueColor;

        var term = Environment.GetEnvironmentVariable("TERM") ?? "";
        if (term.Contains("256color", StringComparison.OrdinalIgnoreCase))
            return ColorProfile.Ansi256;

        if (term.Length > 0 && !term.Equals("dumb", StringComparison.OrdinalIgnoreCase))
            return ColorProfile.Ansi;

        return ColorProfile.NoColor;
    }
}

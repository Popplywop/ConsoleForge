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
public sealed class Program
{
    private Theme _theme = Theme.Default;
    private ColorProfile _colorProfile = ColorProfile.TrueColor;

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
    /// Run the application. Blocks until the model produces a <see cref="QuitMsg"/>.
    /// </summary>
    /// <param name="model">Initial model. <c>Init()</c> is called before the loop starts.</param>
    /// <param name="terminal">
    /// Optional terminal override. If null, a new <see cref="AnsiTerminal"/> is created.
    /// Pass a <see cref="ConsoleForge.Testing.VirtualTerminal"/> for testing.
    /// </param>
    /// <param name="theme">Theme to use. Defaults to <see cref="Theme.Default"/>.</param>
    /// <param name="targetFps">Target frames per second (1–60). Default 30.</param>
    public static void Run(
        IModel model,
        ITerminal? terminal = null,
        Theme? theme = null,
        int targetFps = 30)
    {
        var program = new Program
        {
            _theme = theme ?? Theme.Default,
            _colorProfile = DetectColorProfile()
        };
        program.RunInternal(model, terminal, targetFps);
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

    /// <summary>Current model, updated by the event loop, read by the render timer.</summary>
    private IModel? _currentModel;

    private void RunInternal(IModel model, ITerminal? terminal, int targetFps)
    {
        var ownTerminal = terminal is null;
        _terminal = terminal ?? new AnsiTerminal();

        try
        {
            _terminal.EnterRawMode();
            _terminal.EnterAlternateScreen();
            // Hide cursor once for the whole session — shown again in finally
            _terminal.SetCursorVisible(false);

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
            });

            // Init model
            var initCmd = model.Init();
            DispatchCmd(initCmd);
            _currentModel = model;

            // FPS render timer
            var frameMs = Math.Max(1, 1000 / Math.Clamp(targetFps, 1, 60));
            using var timer = new System.Threading.Timer(_ =>
            {
                var m = _currentModel;
                if (m is not null) RenderFrame(m);
            }, null, 0, frameMs);

            // Event loop
            IModel currentModel = model;
            while (true)
            {
                var msg = _channel.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();

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
            timer.Dispose();
        }
        finally
        {
            _quitting = true;
            if (_terminal is not null)
                _terminal.SetCursorVisible(true); // Restore cursor before exiting
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

        // Handle Tab for focus traversal, then fall through to model.Update
        // so models can also react to Tab (e.g. page navigation).
        if (msg is KeyMsg { Key: ConsoleKey.Tab } tabKey)
        {
            HandleTabFocus(model, tabKey.Shift);
            // Do NOT return — model.Update still receives the Tab key below.
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

        // Mark dirty whenever model changed or an explicit redraw is requested.
        if (!ReferenceEquals(newModel, prevModel) || msg is RedrawMsg)
            _renderer.MarkDirty();

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

        Task.Run(() =>
        {
            try
            {
                var result = cmd();
                _channel.Writer.TryWrite(result);
            }
            catch (Exception)
            {
                // Cmd exceptions are swallowed; could log here in future
            }
        });
    }

    // ── Color profile detection ───────────────────────────────────────

    private static ColorProfile DetectColorProfile()
    {
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


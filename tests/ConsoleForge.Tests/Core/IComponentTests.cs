using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Core;

/// <summary>Unit tests for <see cref="IComponent"/>, <see cref="IComponent{TResult}"/>,
/// and the <see cref="Component"/> helper class.</summary>
public class IComponentTests
{
    // ── Local test messages ───────────────────────────────────────────────────
    private sealed record IncrMsg : IMsg;
    private sealed record DecrMsg : IMsg;
    private sealed record PickMsg : IMsg;
    private sealed record ConfirmMsg : IMsg;
    private sealed record CancelMsg  : IMsg;
    // ── Minimal IComponent implementations ───────────────────────────────────

    /// <summary>Simple counter component with no result.</summary>
    private sealed record CounterComponent(int Count = 0) : IComponent
    {
        public ICmd? Init() => null;

        public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
        {
            IncrMsg   => (this with { Count = Count + 1 }, null),
            DecrMsg => (this with { Count = Math.Max(0, Count - 1) }, null),
            _          => (this, null),
        };

        public IWidget View() => new TextBlock($"Count: {Count}");
    }

    /// <summary>Dialog component that signals a bool result on confirm/cancel.</summary>
    private sealed record ConfirmDialog : IComponent<bool?>
    {
        public bool? Answer { get; init; } // null = unanswered

        // IComponent<bool?>.Result — non-null signals completion
        bool? IComponent<bool?>.Result => Answer;

        /// <summary>True when the user has made a choice.</summary>
        public bool IsAnswered => Answer is not null;

        public ICmd? Init() => null;

        public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
        {
            ConfirmMsg => (this with { Answer = true  }, null),
            CancelMsg  => (this with { Answer = false }, null),
            _          => (this, null),
        };

        public IWidget View() => new TextBlock("Confirm? Y/N");
    }

    /// <summary>String-result picker component.</summary>
    private sealed record ItemPicker(
        string[] Items,
        int SelectedIndex = 0,
        string? Result = null) : IComponent<string>
    {
        string IComponent<string>.Result => Result!; // null = not completed

        public ICmd? Init() => null;

        public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
        {
            IncrMsg   => (this with { SelectedIndex = Math.Max(0, SelectedIndex - 1) }, null),
            DecrMsg => (this with { SelectedIndex = Math.Min(Items.Length - 1, SelectedIndex + 1) }, null),
            PickMsg => (this with { Result = Items[SelectedIndex] }, null),
            _          => (this, null),
        };

        public IWidget View() => new ConsoleForge.Widgets.List(Items, SelectedIndex);
    }

    // ── IComponent: basic contract ────────────────────────────────────────────

    [Fact]
    public void IComponent_ImplementsIModel()
    {
        IModel model = new CounterComponent();
        Assert.NotNull(model);
    }

    [Fact]
    public void IComponent_Update_ReturnsSameType()
    {
        var comp = new CounterComponent(5);
        var (next, cmd) = ((IModel)comp).Update(new IncrMsg());
        Assert.IsType<CounterComponent>(next);
        Assert.Equal(6, ((CounterComponent)next).Count);
        Assert.Null(cmd);
    }

    [Fact]
    public void IComponent_Init_ReturnsNull_WhenNoStartup()
    {
        Assert.Null(new CounterComponent().Init());
    }

    [Fact]
    public void IComponent_View_ProducesWidget()
    {
        var comp = new CounterComponent(3);
        var widget = comp.View();
        Assert.NotNull(widget);
    }

    // ── IComponent<TResult>: completion ──────────────────────────────────────

    [Fact]
    public void IComponentT_Result_IsNullWhileRunning()
    {
        var dialog = new ConfirmDialog();
        Assert.False(new ConfirmDialog().IsAnswered);
    }

    [Fact]
    public void IComponentT_Result_SetOnConfirm()
    {
        var dialog = new ConfirmDialog();
        var (next, _) = ((IModel)dialog).Update(new ConfirmMsg());
        var typed = (ConfirmDialog)next;
        Assert.True(typed.IsAnswered);
        Assert.True(typed.Answer);
    }

    [Fact]
    public void IComponentT_Result_SetOnCancel()
    {
        var dialog = new ConfirmDialog();
        var (next, _) = ((IModel)dialog).Update(new CancelMsg());
        var typed = (ConfirmDialog)next;
        Assert.True(typed.IsAnswered);
        Assert.False(typed.Answer);
    }

    [Fact]
    public void IComponentT_StringResult_SetOnSelect()
    {
        var picker = new ItemPicker(["Apple", "Banana", "Cherry"], SelectedIndex: 1);
        var (next, _) = ((IModel)picker).Update(new PickMsg());
        var typed = (ItemPicker)next;
        Assert.Equal("Banana", ((IComponent<string>)typed).Result);
    }

    // ── Component.Delegate ────────────────────────────────────────────────────

    [Fact]
    public void Delegate_NonNull_UpdatesComponent()
    {
        var comp = new CounterComponent(0);
        var (next, cmd) = Component.Delegate(comp, new IncrMsg());
        Assert.NotNull(next);
        Assert.Equal(1, next!.Count);
        Assert.Null(cmd);
    }

    [Fact]
    public void Delegate_Null_ReturnsNullPair()
    {
        CounterComponent? comp = null;
        var (next, cmd) = Component.Delegate(comp, new IncrMsg());
        Assert.Null(next);
        Assert.Null(cmd);
    }

    [Fact]
    public void Delegate_PreservesType_NoManualCast()
    {
        var picker = new ItemPicker(["A", "B"], SelectedIndex: 0);
        var (next, _) = Component.Delegate(picker, new DecrMsg());
        // next is typed as ItemPicker — no cast needed
        Assert.NotNull(next);
        Assert.Equal(1, next!.SelectedIndex);
    }

    [Fact]
    public void Delegate_UnhandledMsg_ReturnsSameReference()
    {
        var comp = new CounterComponent(5);
        var (next, cmd) = Component.Delegate(comp, new WindowResizeMsg(80, 24));
        // Unhandled msg → same logical state
        Assert.Equal(5, next!.Count);
        Assert.Null(cmd);
    }

    // ── Component.IsCompleted ─────────────────────────────────────────────────

    [Fact]
    public void IsCompleted_FalseWhileRunning()
    {
        var dialog = new ConfirmDialog();
        Assert.False(((IComponent<bool?>)dialog).IsCompleted());
    }

    [Fact]
    public void IsCompleted_TrueAfterConfirm()
    {
        var dialog = new ConfirmDialog();
        var (next, _) = Component.Delegate(dialog, new ConfirmMsg());
        Assert.True(((IComponent<bool?>?)next).IsCompleted());
    }

    [Fact]
    public void IsCompleted_NullComponent_ReturnsFalse()
    {
        IComponent<bool?>? dialog = null;
        Assert.False(dialog.IsCompleted());
    }

    // ── Component.Init ────────────────────────────────────────────────────────

    [Fact]
    public void Init_NoStartupCmd_ReturnsSameAndNull()
    {
        var comp = new CounterComponent();
        var (returned, cmd) = Component.Init(comp);
        Assert.Same(comp, returned);
        Assert.Null(cmd);
    }

    [Fact]
    public void Init_WithExtraCmd_BatchesThem()
    {
        var comp = new CounterComponent();
        var extra = Cmd.Msg(new IncrMsg());
        var (_, cmd) = Component.Init(comp, extra);
        // extra cmd should be returned since init is null
        Assert.NotNull(cmd);
    }

    // ── Parent model delegation pattern ──────────────────────────────────────

    [Fact]
    public void ParentModel_DelegatesUpdateToComponent()
    {
        // Simulate a parent model that embeds a CounterComponent
        var parent = new ParentModel(new CounterComponent(0));

        var (next, _) = ((IModel)parent).Update(new IncrMsg());
        var nextParent = (ParentModel)next;

        Assert.Equal(1, nextParent.Counter!.Count);
    }

    [Fact]
    public void ParentModel_HandlesComponentCompletion()
    {
        var parent = new ParentModel(null, new ConfirmDialog());

        // Confirm → dialog completes
        var (next, _) = ((IModel)parent).Update(new ConfirmMsg());
        var nextParent = (ParentModel)next;

        Assert.Null(nextParent.Dialog);           // dialog dismissed
        Assert.True(nextParent.LastConfirmed);    // result captured
    }

    private sealed record ParentModel(
        CounterComponent? Counter    = null,
        ConfirmDialog?    Dialog     = null,
        bool              LastConfirmed = false) : IModel
    {
        public ICmd? Init() => null;

        public (IModel Model, ICmd? Cmd) Update(IMsg msg)
        {
            // Delegate to dialog when open
            if (Dialog is not null)
            {
                var (nextDialog, cmd) = Component.Delegate(Dialog, msg);
                if (nextDialog.IsCompleted())
                    return (this with {
                        Dialog = null,
                        LastConfirmed = nextDialog!.Answer ?? false
                    }, cmd);
                return (this with { Dialog = nextDialog }, cmd);
            }

            // Delegate to counter
            if (Counter is not null)
            {
                var (nextCounter, cmd) = Component.Delegate(Counter, msg);
                return (this with { Counter = nextCounter }, cmd);
            }

            return (this, null);
        }

        public IWidget View() =>
            (IWidget?)Counter?.View() ?? Dialog?.View() ?? new TextBlock("empty");
    }
}

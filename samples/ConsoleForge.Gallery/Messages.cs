using ConsoleForge.Core;

namespace ConsoleForge.Gallery;

/// <summary>Gallery action messages — emitted by KeyMaps, processed by Update.</summary>
record TickMsg(DateTimeOffset At) : IMsg;
record NavUpMsg       : IMsg;
record NavDownMsg     : IMsg;
record NavSelectMsg   : IMsg;
record ToggleFocusMsg : IMsg;
record DismissModalMsg : IMsg;
record ModalConfirmMsg : IMsg;
record ModalCancelMsg  : IMsg;
record OpenModalMsg    : IMsg;
record AdjustLeftMsg   : IMsg;
record AdjustRightMsg  : IMsg;
record ToggleCheckboxMsg : IMsg;
record CycleThemeMsg   : IMsg;

using Microsoft.CodeAnalysis;

namespace ConsoleForge.SourceGen;

/// <summary>
/// Diagnostic descriptors for ConsoleForge source generator warnings and errors.
/// </summary>
internal static class Diagnostics
{
    /// <summary>CFG001: type must be partial to receive generated code.</summary>
    public static readonly DiagnosticDescriptor MissingPartial = new(
        id: "CFG001",
        title: "[DispatchUpdate] requires partial type",
        messageFormat: "Type '{0}' must be declared partial to use [DispatchUpdate]",
        category: "ConsoleForge.SourceGen",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>CFG002: handler skipped — message type not found or duplicate.</summary>
    public static readonly DiagnosticDescriptor MsgTypeNotFound = new(
        id: "CFG002",
        title: "Handler skipped",
        messageFormat: "Handler '{0}' skipped: {1}",
        category: "ConsoleForge.SourceGen",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>CFG003: handler return type must be (IModel, ICmd?).</summary>
    public static readonly DiagnosticDescriptor HandlerReturnTypeMismatch = new(
        id: "CFG003",
        title: "Handler return type mismatch",
        messageFormat: "Handler '{0}' return type must be (IModel, ICmd?) to be included in dispatch",
        category: "ConsoleForge.SourceGen",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>CFG004: type already has a hand-written Update method.</summary>
    public static readonly DiagnosticDescriptor HasExistingUpdate = new(
        id: "CFG004",
        title: "Type already has Update method",
        messageFormat: "Type '{0}' already declares Update(IMsg). Source generator will skip dispatch generation.",
        category: "ConsoleForge.SourceGen",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);
}
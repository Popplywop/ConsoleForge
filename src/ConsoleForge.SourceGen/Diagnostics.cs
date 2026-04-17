using Microsoft.CodeAnalysis;

namespace ConsoleForge.SourceGen;

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor MissingPartial = new(
        id: "CFG001",
        title: "[DispatchUpdate] requires partial type",
        messageFormat: "Type '{0}' must be declared partial to use [DispatchUpdate]",
        category: "ConsoleForge.SourceGen",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MsgTypeNotFound = new(
        id: "CFG002",
        title: "Handler skipped",
        messageFormat: "Handler '{0}' skipped: {1}",
        category: "ConsoleForge.SourceGen",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HandlerReturnTypeMismatch = new(
        id: "CFG003",
        title: "Handler return type mismatch",
        messageFormat: "Handler '{0}' return type must be (IModel, ICmd?) to be included in dispatch",
        category: "ConsoleForge.SourceGen",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HasExistingUpdate = new(
        id: "CFG004",
        title: "Type already has Update method",
        messageFormat: "Type '{0}' already declares Update(IMsg). Source generator will skip dispatch generation.",
        category: "ConsoleForge.SourceGen",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);
}
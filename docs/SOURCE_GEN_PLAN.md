# ConsoleForge.SourceGen — Implementation Plan

## What this is

A Roslyn incremental source generator (`ConsoleForge.SourceGen`) that eliminates
the `Update(IMsg)` dispatch boilerplate from `IModel` and `IComponent`
implementations. It ships as a separate NuGet package so users opt in without
affecting apps that don't use it.

---

## Motivation

Every model and component today must hand-write the same structural pattern:

```csharp
public (IModel Model, ICmd? Cmd) Update(IMsg msg)
{
    if (Keys.Handle(msg) is { } action) msg = action;  // optional
    return msg switch
    {
        NavUpMsg   => (this with { Index = Index - 1 }, null),
        NavDownMsg => (this with { Index = Index + 1 }, null),
        SelectMsg  => (this with { Result = Items[Index] }, null),
        _          => (this, null),
    };
}
```

The `switch` frame, default arm, and `Keys.Handle` plumbing are identical
everywhere. Only the case bodies are user-defined logic.

With the generator the user writes:

```csharp
[DispatchUpdate]
sealed partial record ListPage(int Index = 0, string? Result = null)
    : IComponent<string>
{
    string IComponent<string>.Result => Result!;
    public ICmd? Init() => null;
    public IWidget View() => ...;

    // Generator finds On{X} methods → emits Update() switch
    (IModel, ICmd?) OnNavUp()   => (this with { Index = Index - 1 }, null);
    (IModel, ICmd?) OnNavDown() => (this with { Index = Index + 1 }, null);
    (IModel, ICmd?) OnSelect()  => (this with { Result = Items[Index] }, null);
}
```

---

## Packages

| Package | Role | Target |
|---------|------|--------|
| `ConsoleForge` | Library + trigger attributes | `net8.0` |
| `ConsoleForge.SourceGen` | Roslyn generator | `netstandard2.0` |

The trigger attributes (`[DispatchUpdate]`, `[Component]`) live in
**`ConsoleForge`** so users get them without needing the generator package.
The generator package is purely additive — existing hand-written `Update`
methods continue to work unchanged.

---

## Existing stub

```
src/ConsoleForge.SourceGen/
  ConsoleForge.SourceGen.csproj   ← already created, correct dependencies
  DispatchUpdateGenerator.cs      ← stub IIncrementalGenerator, throws NotImplementedException
```

The solution file (`ConsoleForge.slnx`) already references both projects.

---

## Step 1 — Add trigger attributes to ConsoleForge

Add to `src/ConsoleForge/Core/Attributes.cs` (new file):

```csharp
namespace ConsoleForge.Core;

/// <summary>
/// Instructs the ConsoleForge source generator to produce the
/// <c>Update(IMsg)</c> dispatch method for this partial record or class.
/// <para>
/// The generator scans for methods matching the pattern
/// <c>(IModel Model, ICmd? Cmd) On{MsgType}(...)</c> and emits a
/// <c>switch</c> that routes each message type to its handler.
/// The type must be declared <c>partial</c>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DispatchUpdateAttribute : Attribute { }

/// <summary>
/// Instructs the generator to also emit the <c>Init()</c> method
/// (returns null) and the explicit <c>IComponent&lt;TResult&gt;.Result</c>
/// property implementation when the type implements
/// <c>IComponent&lt;TResult&gt;</c>.
/// <para>
/// Requires the type to have a nullable property named <c>Result</c>
/// whose non-null value signals completion.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ComponentAttribute : Attribute { }
```

---

## Step 2 — Implement DispatchUpdateGenerator

Replace the stub in `src/ConsoleForge.SourceGen/DispatchUpdateGenerator.cs`.

### 2a. Find targets

```csharp
[Generator]
public sealed class DispatchUpdateGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var targets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "ConsoleForge.Core.DispatchUpdateAttribute",
                predicate: static (node, _) =>
                    node is RecordDeclarationSyntax { Modifiers: var m }
                        && m.Any(SyntaxKind.PartialKeyword),
                transform: static (ctx, ct) => GetTarget(ctx, ct))
            .Where(static t => t is not null);

        context.RegisterSourceOutput(targets,
            static (spc, target) => Emit(spc, target!));
    }
}
```

### 2b. Extract target information

The `GetTarget` method walks the semantic model and collects:

```csharp
sealed record GenerationTarget(
    string Namespace,
    string TypeName,
    string TypeKind,               // "record" or "class"
    bool   HasKeyMap,              // true if a static field named 'Keys' exists
    IReadOnlyList<HandlerInfo> Handlers,
    bool   EmitInit,               // from [Component]
    bool   EmitResultProperty,     // from [Component], when IComponent<T> is implemented
    string? ResultTypeName);       // e.g. "string", "bool?"

sealed record HandlerInfo(
    string MethodName,             // e.g. "OnNavUp"
    string MsgTypeName,            // e.g. "NavUpMsg" (fully qualified)
    bool   TakesMsg);              // true if method declares (XMsg msg) parameter
```

**Handler discovery rules:**
- Method return type must be `(IModel Model, ICmd? Cmd)` or `(IModel, ICmd?)`
- Method name must start with `On` followed by an uppercase letter
- Infer message type by stripping the `On` prefix and appending `Msg`:
  `OnNavUp` → look for `NavUpMsg` in scope
- If the method declares a parameter of type `{X}Msg`, pass it through:
  `OnPick(PickMsg msg)` → `PickMsg m => OnPick(m)`
- If no parameter, call with no args: `NavUpMsg => OnNavUp()`
- Methods can be `private` or internal — generator sees all declarations

**KeyMap detection:**
- Look for a `static` field or property named `Keys` of type `KeyMap`
- If present, emit `if (Keys.Handle(msg) is { } action) msg = action;`

### 2c. Emit the generated file

Template for `{TypeName}.g.cs`:

```csharp
// <auto-generated/>
// Source: ConsoleForge.SourceGen — DispatchUpdateGenerator
#nullable enable

namespace {Namespace};

partial {TypeKind} {TypeName}
{
    // ── [Component] scaffolding ───────────────────────────────────────────
    // (emitted only when [Component] attribute is present)
    {ResultTypeName} IComponent<{ResultTypeName}>.Result => Result!;
    public ICmd? Init() => null;

    // ── [DispatchUpdate] generated Update ────────────────────────────────
    public (global::ConsoleForge.Core.IModel Model, global::ConsoleForge.Core.ICmd? Cmd)
        Update(global::ConsoleForge.Core.IMsg msg)
    {
        {KeyMapLine}  // if HasKeyMap: "if (Keys.Handle(msg) is { } action) msg = action;"
        return msg switch
        {
            {Cases}   // one line per handler
            _ => (this, null),
        };
    }
}
```

Case line formats:
```csharp
// Handler with no parameter:
global::My.NavUpMsg => OnNavUp(),

// Handler with message parameter:
global::My.PickMsg __m => OnPick((global::My.PickMsg)__m),
```

Use fully-qualified type names everywhere to avoid namespace collisions.

---

## Step 3 — ComponentAttribute generator pass

Add a second `[Generator]` class (or a second pipeline in the same class)
that handles `[Component]`:

- When `[Component]` is on a type that also has `[DispatchUpdate]`, the
  `Init()` and `Result` are emitted in the same generated file.
- When `[Component]` is alone (no `[DispatchUpdate]`), emit a separate
  `{TypeName}.component.g.cs` with just those two members.

**IComponent\<T\> detection:**
Walk the base type list via the semantic model. When
`IComponent<SomeType>` is found:
- `ResultTypeName = "SomeType"` (preserve nullability annotation)
- Emit: `SomeType IComponent<SomeType>.Result => Result!;`
- Emit: `public ICmd? Init() => null;`

If the user has already written either member manually, the generator must
**skip** that member (check for existing declarations to avoid CS0111).

---

## Step 4 — Default KeyMap injection (optional, post-MVP)

If a `[DispatchUpdate]` type has **no** `static KeyMap Keys` field, the
generator could optionally emit a default one wired to the type's handlers.
**Skip this for MVP** — it adds complexity and users may want custom bindings.

---

## Step 5 — Diagnostics

Define named diagnostics so errors appear as proper IDE warnings/errors:

```csharp
static readonly DiagnosticDescriptor MissingPartial = new(
    id:             "CFG001",
    title:          "[DispatchUpdate] requires partial type",
    messageFormat:  "Type '{0}' must be declared partial to use [DispatchUpdate]",
    category:       "ConsoleForge.SourceGen",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);

static readonly DiagnosticDescriptor MsgTypeNotFound = new(
    id:             "CFG002",
    title:          "Message type not found",
    messageFormat:  "Handler '{0}' expects '{1}' but no such IMsg type was found in scope",
    category:       "ConsoleForge.SourceGen",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true);

static readonly DiagnosticDescriptor HandlerReturnTypeMismatch = new(
    id:             "CFG003",
    title:          "Handler return type mismatch",
    messageFormat:  "Handler '{0}' return type must be (IModel, ICmd?) to be included in dispatch",
    category:       "ConsoleForge.SourceGen",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true);
```

---

## Step 6 — Tests

Use `Microsoft.CodeAnalysis.CSharp.Testing` (the standard generator test harness).

Add project: `tests/ConsoleForge.SourceGen.Tests/`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Testing" Version="1.1.2" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <ProjectReference Include="..\..\src\ConsoleForge.SourceGen\ConsoleForge.SourceGen.csproj" />
    <ProjectReference Include="..\..\src\ConsoleForge\ConsoleForge.csproj" />
  </ItemGroup>
</Project>
```

### Test cases to cover

**Happy path:**
- `[DispatchUpdate]` on partial record → `Update` emitted with correct cases
- Handler with no parameter → `XMsg => OnX()`
- Handler with message parameter → `XMsg __m => OnX((XMsg)__m)`
- `static KeyMap Keys` present → `Keys.Handle` line emitted
- No handlers → Update emits only default arm
- `[Component]` on `IComponent<string>` impl → `Result` + `Init` emitted
- Both attributes together → single generated file with all members

**Diagnostics:**
- Non-partial type with `[DispatchUpdate]` → CFG001 error
- `OnFoo()` with no `FooMsg` in scope → CFG002 warning, handler skipped
- `OnBar()` returning wrong type → CFG003 warning, handler skipped

**Edge cases:**
- Two handlers for the same message type → first wins, CFG002 on second
- Handler in base type → not picked up (only declared members)
- Existing hand-written `Update` method → generator skips, emits CFG004 info

---

## Step 7 — Wire generator into Gallery sample

Update `samples/ConsoleForge.Gallery/ConsoleForge.Gallery.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\ConsoleForge.SourceGen\ConsoleForge.SourceGen.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

Refactor one or two Gallery page components to use `[DispatchUpdate]` as the
canonical demonstration. `ListPageComponent` and `CheckboxComponent` are
good candidates — they have clean handler sets.

---

## Step 8 — NuGet packaging

Update `ConsoleForge.SourceGen.csproj`:

```xml
<PropertyGroup>
  <PackageId>ConsoleForge.SourceGen</PackageId>
  <Version>0.2.0</Version>
  <Authors>Popplywop</Authors>
  <Description>
    Roslyn source generator for ConsoleForge. Eliminates Update(IMsg) dispatch
    boilerplate via [DispatchUpdate] and [Component] attributes.
  </Description>
  <PackageTags>tui;terminal;consoleforge;source-generator;roslyn</PackageTags>
  <PackageProjectUrl>https://github.com/Popplywop/ConsoleForge</PackageProjectUrl>
  <RepositoryUrl>https://github.com/Popplywop/ConsoleForge</RepositoryUrl>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <DevelopmentDependency>true</DevelopmentDependency>
  <IncludeBuildOutput>false</IncludeBuildOutput>
</PropertyGroup>

<!-- Pack the generator assembly into the analyzers folder -->
<ItemGroup>
  <None Include="$(OutputPath)\$(AssemblyName).dll"
        Pack="true"
        PackagePath="analyzers/dotnet/cs"
        Visible="false" />
</ItemGroup>
```

---

## Deliverable checklist

- [ ] `src/ConsoleForge/Core/Attributes.cs` — `DispatchUpdateAttribute`, `ComponentAttribute`
- [ ] `src/ConsoleForge.SourceGen/DispatchUpdateGenerator.cs` — full implementation
- [ ] `src/ConsoleForge.SourceGen/ComponentGenerator.cs` — `[Component]` pipeline (or second pass in same file)
- [ ] `src/ConsoleForge.SourceGen/Diagnostics.cs` — CFG001–CFG004 descriptors
- [ ] `tests/ConsoleForge.SourceGen.Tests/` — test project + happy-path + diagnostic tests
- [ ] `samples/ConsoleForge.Gallery/` — at least one component refactored to `[DispatchUpdate]`
- [ ] NuGet metadata on `ConsoleForge.SourceGen.csproj`
- [ ] Update `docs/ROADMAP.md` to mark source generators complete

---

## Key Roslyn APIs to know

```csharp
// Find types by attribute (incremental, efficient)
context.SyntaxProvider.ForAttributeWithMetadataName(fullyQualifiedName, predicate, transform)

// Get semantic model for a syntax node
ctx.SemanticModel.GetDeclaredSymbol(node)

// Walk a type's members
symbol.GetMembers()                          // ImmutableArray<ISymbol>
symbol.GetMembers().OfType<IMethodSymbol>()

// Check if a type implements an interface
symbol.AllInterfaces.Any(i => i.Name == "IComponent")

// Emit a source file
ctx.AddSource("filename.g.cs", SourceText.From(source, Encoding.UTF8))

// Report a diagnostic
ctx.ReportDiagnostic(Diagnostic.Create(descriptor, location, args))

// Get fully-qualified name (safe in generated code)
symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
```

---

## Reference

- [Roslyn incremental generator cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md)
- [Source generator testing](https://github.com/dotnet/roslyn-sdk/tree/main/src/Microsoft.CodeAnalysis.Testing)
- [IIncrementalGenerator API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.iincrementalgenerator)

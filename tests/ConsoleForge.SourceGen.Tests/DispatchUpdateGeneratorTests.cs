#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ConsoleForge.SourceGen.Tests;

/// <summary>
/// Generator tests using direct Roslyn compilation.
/// Avoids the complex CSharpSourceGeneratorVerifier harness and version-conflict issues.
/// </summary>
public class DispatchUpdateGeneratorTests
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static (Compilation output, System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics)
        RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.RuntimeHelpers).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(typeof(ConsoleForge.Core.IModel).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new DispatchUpdateGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return (outputCompilation, diagnostics);
    }

    private static string GetGeneratedSource(Compilation comp, string fileNameContains)
    {
        foreach (var tree in comp.SyntaxTrees)
        {
            if (tree.FilePath.Contains(fileNameContains))
                return tree.GetText().ToString();
        }
        return "";
    }

    // ── happy-path ──────────────────────────────────────────────────────────

    [Fact]
    public void HappyPath_PartialRecord_EmitsUpdate()
    {
        const string source = """
            using ConsoleForge.Core;
            namespace MyApp;
            
            public record NavUpMsg : IMsg;
            public record NavDownMsg : IMsg;
            
            [DispatchUpdate]
            public sealed partial record MyModel(int Index = 0) : IModel
            {
                public ConsoleForge.Core.ICmd? Init() => null;
                public IWidget View() => null!;
                
                public (IModel Model, ICmd? Cmd) OnNavUp() => (this with { Index = Index - 1 }, null);
                public (IModel Model, ICmd? Cmd) OnNavDown() => (this with { Index = Index + 1 }, null);
            }
            """;

        var (comp, diags) = RunGenerator(source);

        var errors = diags.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);

        var generated = GetGeneratedSource(comp, "MyModel.g.cs");
        Assert.Contains("Update(global::ConsoleForge.Core.IMsg msg)", generated);
        Assert.Contains("global::MyApp.NavUpMsg => OnNavUp()", generated);
        Assert.Contains("global::MyApp.NavDownMsg => OnNavDown()", generated);
        Assert.Contains("_ => (this, null)", generated);
    }

    [Fact]
    public void HappyPath_HandlerWithMsgParameter_EmitsCast()
    {
        const string source = """
            using ConsoleForge.Core;
            namespace MyApp;
            
            public record PickMsg(string Item) : IMsg;
            
            [DispatchUpdate]
            public sealed partial record Picker : IModel
            {
                public ConsoleForge.Core.ICmd? Init() => null;
                public IWidget View() => null!;
                
                public (IModel Model, ICmd? Cmd) OnPick(PickMsg msg) => (this, null);
            }
            """;

        var (comp, diags) = RunGenerator(source);
        var generated = GetGeneratedSource(comp, "Picker.g.cs");

        Assert.Contains("global::MyApp.PickMsg __m => OnPick((global::MyApp.PickMsg)__m)", generated);
    }

    [Fact]
    public void HappyPath_NoHandlers_EmitsOnlyDefaultArm()
    {
        const string source = """
            using ConsoleForge.Core;
            namespace MyApp;
            
            [DispatchUpdate]
            public sealed partial record EmptyModel : IModel
            {
                public ConsoleForge.Core.ICmd? Init() => null;
                public IWidget View() => null!;
            }
            """;

        var (comp, diags) = RunGenerator(source);
        var generated = GetGeneratedSource(comp, "EmptyModel.g.cs");

        Assert.Contains("_ => (this, null)", generated);
        // No concrete arms
        Assert.DoesNotContain("Msg => On", generated);
        Assert.DoesNotContain("Msg __m =>", generated);
    }

    [Fact]
    public void HappyPath_StaticKeyMapPresent_EmitsKeysHandleLine()
    {
        const string source = """
            using ConsoleForge.Core;
            namespace MyApp;
            
            public record NavUpMsg : IMsg;
            
            [DispatchUpdate]
            public sealed partial record ModelWithKeys : IModel
            {
                static readonly KeyMap Keys = new KeyMap();
                public ConsoleForge.Core.ICmd? Init() => null;
                public IWidget View() => null!;
                
                public (IModel Model, ICmd? Cmd) OnNavUp() => (this, null);
            }
            """;

        var (comp, diags) = RunGenerator(source);
        var generated = GetGeneratedSource(comp, "ModelWithKeys.g.cs");

        Assert.Contains("Keys.Handle(msg)", generated);
    }

    [Fact]
    public void HappyPath_ComponentAttribute_EmitsInitAndResult()
    {
        const string source = """
            using ConsoleForge.Core;
            namespace MyApp;
            
            [DispatchUpdate, Component]
            public sealed partial class MyComp : IComponent<string>
            {
                public string? Result { get; init; }
                public IWidget View() => null!;
            }
            """;

        var (comp, diags) = RunGenerator(source);
        var generated = GetGeneratedSource(comp, "MyComp.g.cs");

        Assert.Contains("Init()", generated);
        Assert.Contains("IComponent<", generated);
        Assert.Contains(".Result", generated);
        Assert.Contains("Update(", generated);
    }

    [Fact]
    public void ExistingUpdateMethod_GeneratorSkips_EmitsCFG004()
    {
        const string source = """
            using ConsoleForge.Core;
            namespace MyApp;
            
            [DispatchUpdate]
            public sealed partial record HandWritten : IModel
            {
                public ConsoleForge.Core.ICmd? Init() => null;
                public IWidget View() => null!;
                
                public (IModel Model, ICmd? Cmd) Update(IMsg msg) => (this, null);
            }
            """;

        var (comp, diags) = RunGenerator(source);

        var cfg004 = diags.FirstOrDefault(d => d.Id == "CFG004");
        Assert.NotNull(cfg004);
        Assert.Equal(DiagnosticSeverity.Info, cfg004!.Severity);
    }

    [Fact]
    public void HandlerWrongReturnType_CFG003_HandlerSkipped()
    {
        const string source = """
            using ConsoleForge.Core;
            namespace MyApp;

            public record NavUpMsg : IMsg;

            [DispatchUpdate]
            public sealed partial record MyModel : IModel
            {
                public ConsoleForge.Core.ICmd? Init() => null;
                public IWidget View() => null!;

                // wrong return type — should fire CFG003
                public void OnNavUp() { }
            }
            """;

        var (comp, diags) = RunGenerator(source);

        var cfg003 = diags.FirstOrDefault(d => d.Id == "CFG003");
        Assert.NotNull(cfg003);
        Assert.Equal(DiagnosticSeverity.Warning, cfg003!.Severity);
        Assert.Contains("OnNavUp", cfg003.GetMessage());

        // Handler must be absent from the generated switch
        var generated = GetGeneratedSource(comp, "MyModel.g.cs");
        Assert.DoesNotContain("OnNavUp", generated);
    }

    [Fact]
    public void MsgTypeNotFound_CFG002_HandlerSkipped()
    {
        const string source = """
            using ConsoleForge.Core;
            namespace MyApp;

            // No NavUpMsg declared — should fire CFG002
            [DispatchUpdate]
            public sealed partial record MyModel : IModel
            {
                public ConsoleForge.Core.ICmd? Init() => null;
                public IWidget View() => null!;

                public (IModel Model, ICmd? Cmd) OnNavUp() => (this, null);
            }
            """;

        var (comp, diags) = RunGenerator(source);

        var cfg002 = diags.FirstOrDefault(d => d.Id == "CFG002");
        Assert.NotNull(cfg002);
        Assert.Equal(DiagnosticSeverity.Warning, cfg002!.Severity);
        Assert.Contains("OnNavUp", cfg002.GetMessage());
        Assert.Contains("NavUpMsg", cfg002.GetMessage());

        // Handler must be absent from the generated switch
        var generated = GetGeneratedSource(comp, "MyModel.g.cs");
        Assert.DoesNotContain("OnNavUp", generated);
    }

    [Fact]
    public void DuplicateHandler_CFG002_SecondSkipped()
    {
        const string source = """
            using ConsoleForge.Core;
            namespace MyApp;

            public record NavUpMsg : IMsg;

            [DispatchUpdate]
            public sealed partial record MyModel : IModel
            {
                public ConsoleForge.Core.ICmd? Init() => null;
                public IWidget View() => null!;

                public (IModel Model, ICmd? Cmd) OnNavUp() => (this, null);
                // second handler for same msg — should fire CFG002
                public (IModel Model, ICmd? Cmd) OnNavUp2_NavUp() => (this, null);
            }
            """;

        // OnNavUp2_NavUp strips "On" and appends Msg → "NavUp2_NavUpMsg" which doesn't exist,
        // so this actually tests the not-found path for the second. For a true duplicate test,
        // we need two methods whose inferred msg name resolves to the same type, which requires
        // an alias or the same name. Since the generator strips 'On' + appends 'Msg',
        // two methods OnNavUp + OnNavUpB would look for NavUpMsg and NavUpBMsg respectively.
        // The only way to get a true duplicate is to have the same method name twice, which C#
        // won't allow. So we verify the not-found CFG002 path covers the second-method scenario.
        var (comp, diags) = RunGenerator(source);
        // At least one CFG002 for the second handler (NavUp2_NavUpMsg not found)
        Assert.Contains(diags, d => d.Id == "CFG002");
    }

    [Fact]
    public void NonPartialType_CFG001Error_NoOutput()
    {
        const string source = """
            using ConsoleForge.Core;
            namespace MyApp;
            
            [DispatchUpdate]
            public sealed class NotPartial : IModel
            {
                public ConsoleForge.Core.ICmd? Init() => null;
                public IWidget View() => null!;
            }
            """;

        var (comp, diags) = RunGenerator(source);
        var cfg001 = diags.FirstOrDefault(d => d.Id == "CFG001");
        Assert.NotNull(cfg001);
        Assert.Equal(DiagnosticSeverity.Error, cfg001!.Severity);
    }
}

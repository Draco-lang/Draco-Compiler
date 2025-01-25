using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Draco.Compiler.Api;
using Draco.Compiler.Api.Diagnostics;
using Draco.Compiler.Api.Semantics;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Api.Syntax.Extensions;
using Draco.Compiler.Internal;
using Draco.Compiler.Internal.Symbols;
using Microsoft.CodeAnalysis.CSharp;
using Binder = Draco.Compiler.Internal.Binding.Binder;
using CSharpSourceText = Microsoft.CodeAnalysis.Text.SourceText;
using RoslynMetadataReference = Microsoft.CodeAnalysis.MetadataReference;
using RoslynSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Draco.Compiler.Tests;

internal static class TestUtilities
{
    public const string DefaultAssemblyName = "Test.dll";

    private static IEnumerable<Basic.Reference.Assemblies.Net80.ReferenceInfo> Net8Bcl =>
        Basic.Reference.Assemblies.Net80.ReferenceInfos.All;

    public static ImmutableArray<MetadataReference> BclReferences { get; } = Net8Bcl
        .Select(r => MetadataReference.FromPeStream(new MemoryStream(r.ImageBytes)))
        .ToImmutableArray();

    public static string ToPath(params string[] parts) => Path.GetFullPath(Path.Combine(parts));

    public static void AssertDiagnostics(
        IEnumerable<Diagnostic> gotDiagnostics,
        params DiagnosticTemplate[] expectedDiagnostics) =>
        AssertDiagnostics(gotDiagnostics, expectedDiagnostics.AsEnumerable());

    public static void AssertDiagnostics(
        IEnumerable<Diagnostic> gotDiagnostics,
        IEnumerable<DiagnosticTemplate> expectedDiagnostics)
    {
        var gotDiagnosticTemplates = gotDiagnostics
            .Select(d => d.Template)
            .ToList();
        foreach (var expectedDiag in expectedDiagnostics)
        {
            Assert.Contains(expectedDiag, gotDiagnosticTemplates);
        }
    }

    public static void AssertNotDiagnostics(
        IEnumerable<Diagnostic> diagnostics,
        params DiagnosticTemplate[] expectedMissingDiagnostics) =>
        AssertNotDiagnostics(diagnostics, expectedMissingDiagnostics.AsEnumerable());

    public static void AssertNotDiagnostics(
        IEnumerable<Diagnostic> diagnostics,
        IEnumerable<DiagnosticTemplate> expectedMissingDiagnostics)
    {
        var gotDiagnosticTemplates = diagnostics
            .Select(d => d.Template)
            .ToList();
        foreach (var expectedDiag in expectedMissingDiagnostics)
        {
            Assert.DoesNotContain(expectedDiag, gotDiagnosticTemplates);
        }
    }

    #region Syntax utilities

    public static TNode GetNode<TNode>(this SyntaxTree tree, int index = 0) =>
        tree.Root.PreOrderTraverse().OfType<TNode>().ElementAt(index);

    public static TNode GetNode<TNode>(this SyntaxTree tree, Func<TNode, bool> predicate) =>
        tree.Root.PreOrderTraverse().OfType<TNode>().First(predicate);

    #endregion

    #region Semantic utilities

    public static Symbol GetMetadataSymbol(Compilation compilation, string? assemblyName, params string[] path)
    {
        assemblyName ??= DefaultAssemblyName;
        var asm = compilation.MetadataAssemblies.Single(a => a.AssemblyName.Name == assemblyName);
        return asm.RootNamespace.Lookup([.. path]).First();
    }

    public static TSymbol GetInternalSymbol<TSymbol>(ISymbol? symbol)
        where TSymbol : Symbol
    {
        Assert.NotNull(symbol);
        Assert.IsAssignableFrom<SymbolBase>(symbol);
        var symbolBase = (SymbolBase)symbol!;
        Assert.IsAssignableFrom<TSymbol>(symbolBase.Symbol);
        return (TSymbol)symbolBase.Symbol;
    }

    public static TMember AssertMember<TMember>(Symbol parent, string memberName)
        where TMember : Symbol
    {
        var member = parent.AllMembers.OfType<TMember>().Where(x => x.Name == memberName).FirstOrDefault();
        if (member is null)
        {
            Assert.Fail($"member '{memberName}' not found in '{parent.Name}'");
        }
        return member;
    }

    public static Binder GetDefiningScope(Symbol? symbol)
    {
        Assert.NotNull(symbol);
        Assert.NotNull(symbol.DeclaringCompilation);

        var syntax = symbol.DeclaringSyntax;
        Assert.NotNull(syntax);

        var parent = syntax.Parent;
        Assert.NotNull(parent);

        return symbol.DeclaringCompilation.GetBinder(parent!);
    }

    #endregion

    #region Draco compilation

    public static TResult Invoke<TResult>(
        Assembly assembly,
        string methodName = CompilerConstants.EntryPointName,
        IEnumerable<object?>? args = null,
        TextReader? stdin = null,
        TextWriter? stdout = null,
        string moduleName = CompilerConstants.DefaultModuleName)
    {
        Console.SetIn(stdin ?? Console.In);
        Console.SetOut(stdout ?? Console.Out);

        // NOTE: nested typed are not separated by . but by + in IL, thats the reason for the replace
        var method = assembly
            .GetType(moduleName.Replace('.', '+'))?
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (TResult?)method?.Invoke(null, args?.ToArray());
        return result!;
    }

    public static Assembly CompileToAssembly(
        string sourceCode,
        IEnumerable<Stream>? additionalReferences = null,
        string? rootModulePath = null) => CompileToAssembly(
            syntaxTrees: [SyntaxTree.Parse(sourceCode)],
            additionalReferences: additionalReferences,
            rootModulePath: rootModulePath);

    public static Assembly CompileToAssembly(
        IEnumerable<SyntaxTree> syntaxTrees,
        IEnumerable<Stream>? additionalReferences = null,
        string? rootModulePath = null) => CompileToAssemblyImpl(CreateCompilation(
            syntaxTrees: syntaxTrees,
            additionalReferences: additionalReferences?.Select(s =>
            {
                s.Position = 0;
                return MetadataReference.FromPeStream(s);
            }),
            rootModulePath: rootModulePath), additionalReferences);

    private static Assembly CompileToAssemblyImpl(
        Compilation compilation,
        IEnumerable<Stream>? additionalReferences)
    {
        additionalReferences ??= [];

        var loadedAssemblies = new Dictionary<string, Assembly>();
        var assemblyLoadContext = new AssemblyLoadContext("Test", isCollectible: true);

        assemblyLoadContext.Resolving += (context, name) =>
        {
            if (name.Name is null) return null;
            if (loadedAssemblies.TryGetValue(name.Name, out var asm)) return asm;
            return null;
        };

        var peStream = CompileToMemory(compilation);
        var assembly = assemblyLoadContext.LoadFromStream(peStream);

        // Load additional references
        foreach (var reference in additionalReferences)
        {
            reference.Position = 0;
            var asm = assemblyLoadContext.LoadFromStream(reference);
            var name = asm.GetName().Name;
            if (name is not null) loadedAssemblies.Add(name, asm);
        }

        return assembly;
    }

    public static MemoryStream CompileToMemory(Compilation compilation)
    {
        var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream: peStream);
        Assert.True(emitResult.Success);
        peStream.Position = 0;
        return peStream;
    }

    public static Compilation CreateCompilation(
        string sourceCode,
        IEnumerable<MetadataReference>? additionalReferences = null,
        string? rootModulePath = null) => CreateCompilation(
            syntaxTree: SyntaxTree.Parse(sourceCode),
            additionalReferences: additionalReferences,
            rootModulePath: rootModulePath);

    public static Compilation CreateCompilation(
        SyntaxTree syntaxTree,
        IEnumerable<MetadataReference>? additionalReferences = null,
        string? rootModulePath = null) => CreateCompilation(
            syntaxTrees: [syntaxTree],
            additionalReferences: additionalReferences,
            rootModulePath: rootModulePath);

    public static Compilation CreateCompilation(
        IEnumerable<SyntaxTree> syntaxTrees,
        IEnumerable<MetadataReference>? additionalReferences = null,
        string? rootModulePath = null) => Compilation.Create(
            syntaxTrees: syntaxTrees.ToImmutableArray(),
            metadataReferences: [.. BclReferences, .. (additionalReferences ?? [])],
            rootModulePath: rootModulePath);

    #endregion

    #region C# compilation

    public static MetadataReference CompileCSharpToMetadataReference(
        string code,
        string assemblyName = DefaultAssemblyName,
        IEnumerable<Stream>? aditionalReferences = null,
        Stream? xmlDocStream = null)
    {
        var stream = CompileCSharpToStream(code, assemblyName, aditionalReferences, xmlDocStream);
        return MetadataReference.FromPeStream(stream, xmlDocStream);
    }

    public static Stream CompileCSharpToStream(
        string code,
        string assemblyName = DefaultAssemblyName,
        IEnumerable<Stream>? aditionalReferences = null,
        Stream? xmlDocStream = null)
    {
        aditionalReferences ??= [];
        var sourceText = CSharpSourceText.From(code, Encoding.UTF8);
        var tree = RoslynSyntaxFactory.ParseSyntaxTree(sourceText);

        var defaultReferences = Net8Bcl
            .Select(r => RoslynMetadataReference.CreateFromStream(new MemoryStream(r.ImageBytes)))
            .Concat(aditionalReferences.Select(r => RoslynMetadataReference.CreateFromStream(r)));

        var compilation = CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: [tree],
            references: defaultReferences,
            options: new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream, xmlDocumentationStream: xmlDocStream);
        Assert.True(emitResult.Success);

        stream.Position = 0;

        if (xmlDocStream is not null) xmlDocStream.Position = 0;
        return stream;
    }

    #endregion
}

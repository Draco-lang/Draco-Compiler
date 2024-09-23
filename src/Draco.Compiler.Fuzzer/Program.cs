using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Draco.Compiler.Api;
using Draco.Compiler.Api.Syntax;
using Draco.Coverage;
using Draco.Fuzzing;
using Terminal.Gui;
using static Basic.Reference.Assemblies.Net80;

namespace Draco.Compiler.Fuzzer;

internal static class Program
{
    private static ImmutableArray<MetadataReference> BclReferences { get; } = ReferenceInfos.All
        .Select(r => MetadataReference.FromPeStream(new MemoryStream(r.ImageBytes)))
        .ToImmutableArray();

    private static readonly MemoryStream peStream = new();

    private static Compilation? previousCompilation;

    private static async Task Main(string[] args)
    {
        Application.Init();
        var debuggerWindow = new TuiTracer();

        var instrumentedAssembly = InstrumentedAssembly.FromWeavedAssembly(typeof(Compilation).Assembly);

        var fuzzer = new Fuzzer<SyntaxTree, int>
        {
            CoverageCompressor = CoverageCompressor.SimdHash,
            CoverageReader = CoverageReader.FromInstrumentedAssembly(instrumentedAssembly),
            FaultDetector = FaultDetector.FilterIdenticalTraces(FaultDetector.DefaultInProcess(TimeSpan.FromSeconds(5))),
            TargetExecutor = TargetExecutor.Assembly(instrumentedAssembly, (SyntaxTree tree) => RunCompilation(tree)),
            InputMinimizer = new SyntaxTreeInputMinimizer(),
            InputMutator = new SyntaxTreeInputMutator(),
            Tracer = debuggerWindow,
        };

        fuzzer.Enqueue(SyntaxTree.Parse("""
            func main() {}
            func foo() {}
            func bar() {}
            func baz() {}
            func qux() {}
            """));
        fuzzer.Enqueue(SyntaxTree.Parse("""
            import System.Console;

            func main() {
                WriteLine("Hello, world!");
            }
            """));
        fuzzer.Enqueue(SyntaxTree.Parse("""
            import System.Console;
            import System.Linq.Enumerable;

            func fib(n: int32): int32 =
                if (n < 2) 1
                else fib(n - 1) + fib(n - 2);

            func main() {
                for (i in Range(0, 10)) {
                    WriteLine("fib(\{i}) = \{fib(i)}");
                }
            }
            """));

        var fuzzerTask = Task.Run(() => fuzzer.Fuzz(CancellationToken.None));

        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(500), loop =>
        {
            Application.Refresh();
            return true;
        });

        Application.Run(Application.Top);
        await fuzzerTask;
        Application.Shutdown();
    }

    private static void RunCompilation(SyntaxTree syntaxTree)
    {
        // Cache compilation to optimize discovered metadata references
        if (previousCompilation is null)
        {
            previousCompilation = Compilation.Create(
                syntaxTrees: [syntaxTree],
                metadataReferences: BclReferences);
        }
        else
        {
            previousCompilation = previousCompilation
                .UpdateSyntaxTree(previousCompilation.SyntaxTrees[0], syntaxTree);
        }
        // NOTE: We reuse the same memory stream to de-stress memory usage a little
        peStream.Position = 0;
        previousCompilation.Emit(peStream: peStream);
    }
}

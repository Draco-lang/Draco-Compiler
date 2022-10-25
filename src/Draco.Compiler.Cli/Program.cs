using System;
using Draco.Compiler.Api.Scripting;
using Draco.Compiler.Api.Syntax;

namespace Draco.Compiler.Cli;

internal class Program
{
    internal static void Main(string[] args)
    {
        ScriptingEngine.Execute($$"""
            func abs(n: int32): int32 =
                if (n < 0) -n
                else n;

            func fib(n: int32): int32 =
                if (n < 2) 1
                else fib(n - 1) + fib(n - 2);

            func main() {
                println("Hello, \{1} + \{2} is \{1 + 2}");
                println("|-12| = \{abs(-12)}");
                println("fib(5) = \{fib(5)}");
            }
            """);
    }
}

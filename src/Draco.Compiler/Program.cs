using System;
using Draco.Compiler.Syntax;

namespace Draco.Compiler;

internal class Program
{
    internal static void Main(string[] args)
    {
        var src = @"
// Simple hello world
from System.Console import { WriteLine };

func main(): int32 {
    'a
}
";
        var srcReader = SourceReader.From(src);
        var lexer = new Lexer(srcReader);
        while (true)
        {
            var token = lexer.Lex();
            //Console.WriteLine(token);
            foreach (var d in token.Diagnostics) Console.WriteLine(d);
            if (token.Type == TokenType.EndOfInput) break;
        }
    }
}

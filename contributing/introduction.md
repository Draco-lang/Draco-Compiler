# Introduction

If you are considering contributing, first off, thank you! This file tries to aid in getting started with the different components. If you have any further questions, feel free to reach out in an issue or on the Discord server. Questions will help drive how to develop the contribution documentation further.

## Project Structure
### Important top-level files and folders
* `.github`: GitHub related configuration, currently only our workflow files lie in here.
* `external`: Any resource that we don't own but needed to copy into the repo. Currently the LSP and DAP specification files can be found here that we use for code-generation.
* `resources`: Any non-code resource needed for packaging or deployment, currently only the Draco NuGet icon can be found here.
* `scripts`: PowerShell scripts that help in development.
* `src`: All Draco compiler related source code.
* `Getting-Started.md`: A short description about how to get a Draco project up and running through NuGet.

### Projects
The projects within the `src` folder have a flat hierarchy. The short description of each project:
* `Draco.Compiler.Benchmarks`: Benchmarks for different aspects of the compiler using BDN.
* `Draco.Compiler.Cli`: The command-line interface for the compiler.
* `Draco.Compiler.Fuzzer`: A fuzzer for the compiler. It's lacking in features, it was mostly used to catch lexer and parser bugs so far.
* `Draco.Compiler.Tasks`: Task definitions for the SDK integration.
* `Draco.Compiler.Toolset`: The toolset integration files.
* `Draco.Compiler`: The core compiler. This is likely the most interesting portion for those who are only interested about the compiler, not the tooling or toolset integration.
* `Draco.Dap`: Our own [Debug Adapter Protocol](https://microsoft.github.io/debug-adapter-protocol//) implementation.
* `Draco.DebugAdapter`: Debug Adapter implementation for Draco specifically.
* `Draco.Debugger.Tui`: A general-purpose .NET debugger Terminal UI frontend.
* `Draco.Debugger`: A fully-managed, generic .NET debugger API.
* `Draco.Editor.Web`: The [Draco web editor](https://playground.draco-lang.org/), allowing one to host the compiler on a website.
* `Draco.Extension.VsCode`: The VS Code extension for Draco.
* `Draco.JsonRpc`: Common JSON-RPC communication for the LSP and DAP projects.
* `Draco.LanguageServer`: Language Server implementation for Draco specifically.
* `Draco.Lsp`: Our own [Language Server Protocol](https://microsoft.github.io/language-server-protocol/) implementation.
* `Draco.ProjectTemplates`: Project templates for Draco.
* `Draco.Sdk`: The .NET SDK integration.
* `Draco.SourceGeneration`: Various source-generation utilities we use for many projects.
* `Draco.SyntaxHighlighting`: Currently only a [TextMate grammar](https://macromates.com/manual/en/language_grammars) file in YAML and JSON format.

As you can see, there are some components, which one might not expect to be found directly with the compiler. We initially decided to go with this monorepo style, because it was much easier to keep everything up to date. Once components start to stabilize or completely decouple (like the VS Code extension), we will start migrating them into separate repositories.

## Specific guides

 * [Compiler](./compiler.md)
 * [Language Server and Debug Adapter](./lsp_dap.md)

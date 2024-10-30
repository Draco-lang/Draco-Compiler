# Compiler

This guide tries to outline the basic architecture and inner workings of the core compiler (`Draco.Compiler`) project.

## Local testing

TODO

## Important Representations
The compiler has a few very important representations of the source code, which is good to elaborate on a bit. One important thing to note, is that the compiler follows the philosophy of: **immutability where feasible**, which is - unsurprisingly - inspired by Roslyn.

### Syntax Tree
Syntax trees are the first major representation of source code. They are immutable, which makes them interesting, as they support parent-child navigation both ways. In other words, you can both navigate up to a parent node, and down to the child nodes from the current node. This is done by something called [red-green trees](https://blog.yaakov.online/red-green-trees/), which is a wonderful data-structure/idea coined by _drumroll please..._ Roslyn!

Since it would be quite cumbersome to write them by hand, our red-green tree nodes are generated from an [XML file](https://github.com/Draco-lang/Compiler/blob/main/src/Draco.Compiler/Internal/Syntax/Syntax.xml).

All details are saved in the syntax tree, making this a Parse Tree or Concrete Syntax Tree, rather than an Abstract Syntax Tree. Keywords and punctuation tokens are stored in the nodes themselves. Whitespaces and comments around tokens are stored as syntax trivia. Only the parser works with the green tree (internal tree) directly, then it is immediately wrapped up in the red tree, where navigation both ways is supported.

### Declaration Table
The [Declaration Table](https://github.com/Draco-lang/Compiler/tree/main/src/Draco.Compiler/Internal/Declarations) is a relatively small representation. It's a small abstraction over the syntax trees for the sole purpose of merging. For C#, the declaration tree is used to merge partial classes and namespaces. In our case, this is where we merge files that belong in the same module and [construct the module hierarchy based on the folder structure](https://github.com/Draco-lang/Compiler/blob/main/src/Draco.Compiler/Internal/Declarations/MergedModuleDeclaration.cs#L34).

### Untyped and Bound Tree
Bound trees are the next major step, and are constructed from syntax trees right after all necessary semantic checks have been done. This mainly involves all symbols being resolved, and all types being inferred. Here is where we diverge a little from Roslyn, as we have 2 stages for the Bound Tree: the untyped tree and the bound tree. Both are immutable structures, and can only be navigated from parent to child.

#### Untyped Tree
While traversing the syntax tree, collecting all type constraints - that we cannot solve yet -, we start transforming the syntax tree into something simpler. Here we throw away the unnecessary details - like punctuations -, but keep some things blank that we cannot resolve yet. This is the untyped tree, and is constructed during the first stage of the binding process. It is generated from an [XML file](https://github.com/Draco-lang/Compiler/blob/main/src/Draco.Compiler/Internal/UntypedTree/UntypedNodes.xml).

#### Bound Tree
Once the Untyped Tree is constructed and all the type-constraints could be resolved, we have all information available to construct the final, Bound Tree. In this, all type and symbolic information is available. It is also generated from an [XML file](https://github.com/Draco-lang/Compiler/blob/main/src/Draco.Compiler/Internal/BoundTree/BoundNodes.xml). This is the equivalent of the Roslyn Bound Trees.

### Symbols
During and after binding, the compiler works with symbols. They are one of the key types once we are done with parsing and start semantic checking. The basic symbol types and their declarations can be found in [this folder](https://github.com/Draco-lang/Compiler/tree/main/src/Draco.Compiler/Internal/Symbols). We define symbols like functions, fields, local- and global-variables, properties, labels, modules, ... Since each of these can come from a different origin, many of these symbols are actually abstract, and need to be implemented depending on where it comes from.
* Sentinel symbols that are used for error reporting/propagation can be [found here](https://github.com/Draco-lang/Compiler/tree/main/src/Draco.Compiler/Internal/Symbols/Error).
* Symbols instantiated in a generic context can be  [found here](https://github.com/Draco-lang/Compiler/tree/main/src/Draco.Compiler/Internal/Symbols/Generic).
* Symbols imported/read up from metadata can be [found here](https://github.com/Draco-lang/Compiler/tree/main/src/Draco.Compiler/Internal/Symbols/Metadata).
* Symbols that are derived from Draco source code can be [found here](https://github.com/Draco-lang/Compiler/tree/main/src/Draco.Compiler/Internal/Symbols/Source).
* Symbols that the compiler synthetizes can be [found here](https://github.com/Draco-lang/Compiler/tree/main/src/Draco.Compiler/Internal/Symbols/Synthetized).

Symbols form a hierarchy, where in-source and metadata elements can intermix.

### Optimizing IR
The next major representation is our register-based, SSA-form IR code. This representation serves the purpose of optimization, as the research on optimizations with register IRs in SSA is much more elaborate than on stack-machines like the .NET CIL. The entire model can be found in [this folder](https://github.com/Draco-lang/Compiler/tree/main/src/Draco.Compiler/Internal/OptimizingIr/Model). The interfaces are generally immutable, and the implementations are mutable.

### CIL/MSIL
The final representation we compile to is CIL/MSIL. We do that using the [System.Reflection.Metadata](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata.ecma335?view=net-7.0) API. There is no in-code representation, the resulting PE files are written directly to `Stream`s.

## Compiler API
The public API of the compiler consists of a relatively small number of significant elements as of right now. All the API has to do is provide programmatic and CLI compilation, and serve language tools like formatters, linters and analyzers - just like Roslyn. The goal for the compiler is to take the "tooling first" approach, where serving tools is its primary objective, producing the output is only "secondary".

### SyntaxTree
The first significant element in the public API is the `SyntaxTree` itself. As its name suggests, it simply represents the parsed source-code, without belonging to any compilation yet. The public API tree is essentially the Red tree in the Red-Green tree pair.

Parsing a `SyntaxTree` is simple:
```cs
var tree = SyntaxTree.Parse("my source code...");

// Or, if you don't want to drop things like path info
var source = SourceText.FromFile("path to source");
var tree2 = SyntaxTree.Parse(source);
```

Within the `SyntaxTree` type itself, you can find a bunch of utilities to traverse the tree. It is also possible to rewrite the tree - this is what the formatter does for example - using the `SyntaxRewriter` utility, but the API is in a very unstable state currently.

### Compilation
TODO

### SemanticModel
TODO

### Code Completion
TODO

### Code Fixes
TODO

## Compilation Flow
TODO

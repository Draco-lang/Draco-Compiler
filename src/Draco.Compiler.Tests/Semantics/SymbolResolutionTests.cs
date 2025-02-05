using System.Collections.Immutable;
using Draco.Compiler.Api;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Binding;
using Draco.Compiler.Internal.FlowAnalysis;
using Draco.Compiler.Internal.Symbols;
using Draco.Compiler.Internal.Symbols.Error;
using static Draco.Compiler.Api.Syntax.SyntaxFactory;
using static Draco.Compiler.Tests.TestUtilities;
using Binder = Draco.Compiler.Internal.Binding.Binder;

namespace Draco.Compiler.Tests.Semantics;

public sealed class SymbolResolutionTests
{
    private static void AssertParentOf(Binder? parent, Binder? child)
    {
        Assert.NotNull(child);
        Assert.False(ReferenceEquals(parent, child));
        Assert.True(ReferenceEquals(child.Parent, parent));
    }

    [Fact]
    public void BasicScopeTree()
    {
        // func foo(n: int32) { // b1
        //     var x1;
        //     {                // b2
        //         var x2;
        //         { var x3; }  // b3
        //     }
        //     {                // b4
        //         var x4;
        //         { var x5; }  // b5
        //         { var x6; }  // b6
        //     }
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(FunctionDeclaration(
            "foo",
            ParameterList(
                Parameter("n", NameType("int32"))),
            null,
            BlockFunctionBody(
                DeclarationStatement(VarDeclaration("x1")),
                ExpressionStatement(BlockExpression(
                    DeclarationStatement(VarDeclaration("x2")),
                    ExpressionStatement(BlockExpression(DeclarationStatement(VarDeclaration("x3")))))),
                ExpressionStatement(BlockExpression(
                    DeclarationStatement(VarDeclaration("x4")),
                    ExpressionStatement(BlockExpression(DeclarationStatement(VarDeclaration("x5")))),
                    ExpressionStatement(BlockExpression(DeclarationStatement(VarDeclaration("x6"))))))))));

        var foo = tree.GetNode<FunctionDeclarationSyntax>();
        var n = tree.GetNode<ParameterSyntax>();
        var x1 = tree.GetNode<VariableDeclarationSyntax>(0);
        var x2 = tree.GetNode<VariableDeclarationSyntax>(1);
        var x3 = tree.GetNode<VariableDeclarationSyntax>(2);
        var x4 = tree.GetNode<VariableDeclarationSyntax>(3);
        var x5 = tree.GetNode<VariableDeclarationSyntax>(4);
        var x6 = tree.GetNode<VariableDeclarationSyntax>(5);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var diagnostics = semanticModel.Diagnostics;

        var symFoo = GetInternalSymbol<FunctionSymbol>(semanticModel.GetDeclaredSymbol(foo));
        var symn = GetInternalSymbol<ParameterSymbol>(semanticModel.GetDeclaredSymbol(n));
        var sym1 = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(x1));
        var sym2 = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(x2));
        var sym3 = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(x3));
        var sym4 = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(x4));
        var sym5 = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(x5));
        var sym6 = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(x6));

        // Assert
        AssertParentOf(GetDefiningScope(sym2), GetDefiningScope(sym3));
        AssertParentOf(GetDefiningScope(sym1), GetDefiningScope(sym2));
        AssertParentOf(GetDefiningScope(sym4), GetDefiningScope(sym5));
        AssertParentOf(GetDefiningScope(sym4), GetDefiningScope(sym6));
        AssertParentOf(GetDefiningScope(sym1), GetDefiningScope(sym4));

        AssertParentOf(GetDefiningScope(symn), GetDefiningScope(sym1));

        AssertParentOf(GetDefiningScope(symFoo), GetDefiningScope(symn));
        Assert.True(ReferenceEquals(compilation.GetBinder(symFoo), GetDefiningScope(symn)));

        Assert.Equal(6, diagnostics.Length);
        Assert.All(diagnostics, diag => Assert.Equal(TypeCheckingErrors.CouldNotInferType, diag.Template));
    }

    [Fact]
    public void LocalShadowing()
    {
        // func foo() {
        //     var x = 0;
        //     var x = x + 1;
        //     var x = x + 1;
        //     var x = x + 1;
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(FunctionDeclaration(
            "foo",
            ParameterList(),
            null,
            BlockFunctionBody(
                DeclarationStatement(VarDeclaration("x", null, LiteralExpression(0))),
                DeclarationStatement(VarDeclaration("x", null, BinaryExpression(NameExpression("x"), Plus, LiteralExpression(1)))),
                DeclarationStatement(VarDeclaration("x", null, BinaryExpression(NameExpression("x"), Plus, LiteralExpression(1)))),
                DeclarationStatement(VarDeclaration("x", null, BinaryExpression(NameExpression("x"), Plus, LiteralExpression(1))))))));

        var x0 = tree.GetNode<VariableDeclarationSyntax>(0);
        var x1 = tree.GetNode<VariableDeclarationSyntax>(1);
        var x2 = tree.GetNode<VariableDeclarationSyntax>(2);
        var x3 = tree.GetNode<VariableDeclarationSyntax>(3);

        var x0ref = tree.GetNode<NameExpressionSyntax>(0);
        var x1ref = tree.GetNode<NameExpressionSyntax>(1);
        var x2ref = tree.GetNode<NameExpressionSyntax>(2);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var symx0 = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(x0));
        var symx1 = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(x1));
        var symx2 = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(x2));
        var symx3 = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(x3));

        var symRefx0 = GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(x0ref));
        var symRefx1 = GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(x1ref));
        var symRefx2 = GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(x2ref));

        // Assert
        Assert.False(ReferenceEquals(symx0, symx1));
        Assert.False(ReferenceEquals(symx1, symx2));
        Assert.False(ReferenceEquals(symx2, symx3));

        Assert.True(ReferenceEquals(symx0, symRefx0));
        Assert.True(ReferenceEquals(symx1, symRefx1));
        Assert.True(ReferenceEquals(symx2, symRefx2));
    }

    [Fact]
    public void OrderIndependentReferencing()
    {
        // func bar() = foo();
        // func foo() = foo();
        // func baz() = foo();

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "bar",
                ParameterList(),
                null,
                InlineFunctionBody(CallExpression(NameExpression("foo")))),
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                InlineFunctionBody(CallExpression(NameExpression("foo")))),
            FunctionDeclaration(
                "baz",
                ParameterList(),
                null,
                InlineFunctionBody(CallExpression(NameExpression("foo"))))));

        var barDecl = tree.GetNode<FunctionDeclarationSyntax>(0);
        var fooDecl = tree.GetNode<FunctionDeclarationSyntax>(1);
        var bazDecl = tree.GetNode<FunctionDeclarationSyntax>(2);

        var call1 = tree.GetNode<CallExpressionSyntax>(0);
        var call2 = tree.GetNode<CallExpressionSyntax>(1);
        var call3 = tree.GetNode<CallExpressionSyntax>(2);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var symBar = GetInternalSymbol<FunctionSymbol>(semanticModel.GetDeclaredSymbol(barDecl));
        var symFoo = GetInternalSymbol<FunctionSymbol>(semanticModel.GetDeclaredSymbol(fooDecl));
        var symBaz = GetInternalSymbol<FunctionSymbol>(semanticModel.GetDeclaredSymbol(bazDecl));

        var refFoo1 = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(call1.Function));
        var refFoo2 = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(call2.Function));
        var refFoo3 = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(call3.Function));

        // Assert
        Assert.False(ReferenceEquals(symBar, symFoo));
        Assert.False(ReferenceEquals(symFoo, symBaz));

        Assert.True(ReferenceEquals(symFoo, refFoo1));
        Assert.True(ReferenceEquals(symFoo, refFoo2));
        Assert.True(ReferenceEquals(symFoo, refFoo3));
    }

    [Fact]
    public void OrderDependentReferencing()
    {
        // func foo() {
        //     var x;
        //     var y = x + z;
        //     var z;
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(FunctionDeclaration(
            "foo",
            ParameterList(),
            null,
            BlockFunctionBody(
                DeclarationStatement(VarDeclaration("x")),
                DeclarationStatement(VarDeclaration("y", value: BinaryExpression(NameExpression("x"), Plus, NameExpression("z")))),
                DeclarationStatement(VarDeclaration("z"))))));

        var xDecl = tree.GetNode<VariableDeclarationSyntax>(0);
        var yDecl = tree.GetNode<VariableDeclarationSyntax>(1);
        var zDecl = tree.GetNode<VariableDeclarationSyntax>(2);

        var xRef = tree.GetNode<NameExpressionSyntax>(0);
        var zRef = tree.GetNode<NameExpressionSyntax>(1);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var symx = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var symy = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(yDecl));
        var symz = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(zDecl));

        var symRefx = GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(xRef));
        var symRefz = GetInternalSymbol<Symbol>(semanticModel.GetReferencedSymbol(zRef));

        // Assert
        Assert.True(ReferenceEquals(symx, symRefx));
        Assert.False(ReferenceEquals(symz, symRefz));
        Assert.True(symRefz.IsError);
    }

    [Fact]
    public void OrderDependentReferencingWithNesting()
    {
        // func foo() {
        //     var x;                 // x1
        //     {
        //         var y;             // y1
        //         var z = x + y;     // z1, x1, y1
        //         var x;             // x2
        //         {
        //             var k = x + w; // k1, x2, error
        //         }
        //         var w;             // w1
        //     }
        //     var k = w;             // k2, error
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(FunctionDeclaration(
            "foo",
            ParameterList(),
            null,
            BlockFunctionBody(
                DeclarationStatement(VarDeclaration("x")),
                ExpressionStatement(BlockExpression(
                    DeclarationStatement(VarDeclaration("y")),
                    DeclarationStatement(VarDeclaration("z", value: BinaryExpression(NameExpression("x"), Plus, NameExpression("y")))),
                    DeclarationStatement(VarDeclaration("x")),
                    ExpressionStatement(BlockExpression(
                        DeclarationStatement(VarDeclaration("k", value: BinaryExpression(NameExpression("x"), Plus, NameExpression("w")))))),
                    DeclarationStatement(VarDeclaration("w")))),
                DeclarationStatement(VarDeclaration("k", value: NameExpression("w")))))));

        var x1Decl = tree.GetNode<VariableDeclarationSyntax>(0);
        var y1Decl = tree.GetNode<VariableDeclarationSyntax>(1);
        var z1Decl = tree.GetNode<VariableDeclarationSyntax>(2);
        var x2Decl = tree.GetNode<VariableDeclarationSyntax>(3);
        var k1Decl = tree.GetNode<VariableDeclarationSyntax>(4);
        var w1Decl = tree.GetNode<VariableDeclarationSyntax>(5);
        var k2Decl = tree.GetNode<VariableDeclarationSyntax>(6);

        var x1Ref1 = tree.GetNode<NameExpressionSyntax>(0);
        var y1Ref1 = tree.GetNode<NameExpressionSyntax>(1);
        var x2Ref1 = tree.GetNode<NameExpressionSyntax>(2);
        var wRefErr1 = tree.GetNode<NameExpressionSyntax>(3);
        var wRefErr2 = tree.GetNode<NameExpressionSyntax>(4);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var x1SymDecl = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(x1Decl));
        var y1SymDecl = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(y1Decl));
        var z1SymDecl = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(z1Decl));
        var x2SymDecl = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(x2Decl));
        var k1SymDecl = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(k1Decl));
        var w1SymDecl = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(w1Decl));
        var k2SymDecl = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(k2Decl));

        var x1SymRef1 = GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(x1Ref1));
        var y1SymRef1 = GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(y1Ref1));
        var x2SymRef1 = GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(x2Ref1));
        var wSymRef1 = semanticModel.GetReferencedSymbol(wRefErr1);
        var wSymRef2 = semanticModel.GetReferencedSymbol(wRefErr2);

        // Assert
        Assert.True(ReferenceEquals(x1SymDecl, x1SymRef1));
        Assert.True(ReferenceEquals(y1SymDecl, y1SymRef1));
        Assert.True(ReferenceEquals(x2SymDecl, x2SymRef1));
        // TODO: Maybe we should still resolve the reference, but mark it that it's something that comes later?
        // (so it is still an error)
        // It would definitely help reduce error cascading
        Assert.True(wSymRef1!.IsError);
        Assert.True(wSymRef2!.IsError);
    }

    [Fact]
    public void ParameterRedefinitionError()
    {
        // func foo(x: int32, x: int32) {
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(FunctionDeclaration(
            "foo",
            ParameterList(
                Parameter("x", NameType("int32")),
                Parameter("x", NameType("int32"))),
            null,
            BlockFunctionBody())));

        var x1Decl = tree.GetNode<ParameterSyntax>(0);
        var x2Decl = tree.GetNode<ParameterSyntax>(1);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var diagnostics = semanticModel.Diagnostics;

        var x1SymDecl = GetInternalSymbol<ParameterSymbol>(semanticModel.GetDeclaredSymbol(x1Decl));
        var x2SymDecl = GetInternalSymbol<ParameterSymbol>(semanticModel.GetDeclaredSymbol(x2Decl));

        // Assert
        Assert.False(x1SymDecl.IsError);
        Assert.False(x2SymDecl.IsError);
        Assert.Single(diagnostics);
        AssertDiagnostics(diagnostics, SymbolResolutionErrors.IllegalShadowing);
    }

    [Fact]
    public void RedefinedParameterReference()
    {
        // func foo(x: int32, x: int32) {
        //     var y = x;
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(FunctionDeclaration(
            "foo",
            ParameterList(
                Parameter("x", NameType("int32")),
                Parameter("x", NameType("int32"))),
            null,
            BlockFunctionBody(
                DeclarationStatement(VarDeclaration("y", null, NameExpression("x")))))));

        var x1Decl = tree.GetNode<ParameterSyntax>(0);
        var x2Decl = tree.GetNode<ParameterSyntax>(1);
        var xRef = tree.GetNode<NameExpressionSyntax>(0);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var diagnostics = semanticModel.Diagnostics;

        var x1SymDecl = GetInternalSymbol<ParameterSymbol>(semanticModel.GetDeclaredSymbol(x1Decl));
        var x2SymDecl = GetInternalSymbol<ParameterSymbol>(semanticModel.GetDeclaredSymbol(x2Decl));
        var x2SymRef = GetInternalSymbol<ParameterSymbol>(semanticModel.GetReferencedSymbol(xRef));

        // Assert
        Assert.Equal(x2SymDecl, x2SymRef);
    }

    [Fact]
    public void GenericParameterRedefinitionError()
    {
        // func foo<T, T>() {}

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(FunctionDeclaration(
            "foo",
            GenericParameterList(GenericParameter("T"), GenericParameter("T")),
            ParameterList(),
            null,
            BlockFunctionBody())));

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var diagnostics = semanticModel.Diagnostics;

        // Assert
        Assert.Single(diagnostics);
        AssertDiagnostics(diagnostics, SymbolResolutionErrors.IllegalShadowing);
    }

    [Fact]
    public void FuncOverloadsGlobalVar()
    {
        // var b: int32;
        // func b(b: int32): int32 = b;

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            VarDeclaration("b", NameType("int32")),
            FunctionDeclaration(
                "b",
                ParameterList(Parameter("b", NameType("int32"))),
                NameType("int32"),
                InlineFunctionBody(NameExpression("b")))));

        var varDecl = tree.GetNode<VariableDeclarationSyntax>(0);
        var funcDecl = tree.GetNode<FunctionDeclarationSyntax>(0);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var diagnostics = semanticModel.Diagnostics;

        var varSym = GetInternalSymbol<PropertySymbol>(semanticModel.GetDeclaredSymbol(varDecl));
        var funcSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetDeclaredSymbol(funcDecl));

        // Assert
        Assert.False(varSym.IsError);
        Assert.False(funcSym.IsError);
        Assert.Single(diagnostics);
        AssertDiagnostics(diagnostics, SymbolResolutionErrors.IllegalShadowing);
    }

    [Fact]
    public void GlobalVariableDefinedLater()
    {
        // func foo() {
        //     var y = x;
        // }
        // var x;

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("y", value: NameExpression("x"))))),
            VarDeclaration("x")));

        var localVarDecl = tree.GetNode<VariableDeclarationSyntax>(0);
        var globalVarDecl = tree.GetNode<VariableDeclarationSyntax>(1);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var varRefSym = GetInternalSymbol<PropertySymbol>(semanticModel.GetReferencedSymbol(localVarDecl.Value!.Value));
        var varDeclSym = GetInternalSymbol<PropertySymbol>(semanticModel.GetDeclaredSymbol(globalVarDecl));

        // Assert
        Assert.True(ReferenceEquals(varDeclSym, varRefSym));
    }

    [Fact]
    public void NestedLabelCanNotBeAccessed()
    {
        // func foo() {
        //     if (false) {
        //     lbl:
        //     }
        //     goto lbl;
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(IfExpression(
                        condition: LiteralExpression(false),
                        then: BlockExpression(DeclarationStatement(LabelDeclaration("lbl"))))),
                    ExpressionStatement(GotoExpression("lbl"))))));

        var labelDecl = tree.GetNode<LabelDeclarationSyntax>(0);
        var labelRef = tree.GetNode<GotoExpressionSyntax>(0).Target;

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var labelDeclSym = GetInternalSymbol<LabelSymbol>(semanticModel.GetDeclaredSymbol(labelDecl));
        var labelRefSym = semanticModel.GetReferencedSymbol(labelRef);

        // Assert
        Assert.False(ReferenceEquals(labelDeclSym, labelRefSym));
        Assert.False(labelDeclSym.IsError);
        Assert.True(labelRefSym!.IsError);
    }

    [Fact]
    public void LabelInOtherFunctionCanNotBeAccessed()
    {
        // func foo() {
        // lbl:
        // }
        //
        // func bar() {
        //     goto lbl;
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(LabelDeclaration("lbl")))),
            FunctionDeclaration(
                "bar",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(GotoExpression("lbl"))))));

        var labelDecl = tree.GetNode<LabelDeclarationSyntax>(0);
        var labelRef = tree.GetNode<GotoExpressionSyntax>(0).Target;

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var labelDeclSym = GetInternalSymbol<LabelSymbol>(semanticModel.GetDeclaredSymbol(labelDecl));
        var labelRefSym = semanticModel.GetReferencedSymbol(labelRef);

        // Assert
        Assert.False(ReferenceEquals(labelDeclSym, labelRefSym));
        Assert.False(labelDeclSym.IsError);
        Assert.True(labelRefSym!.IsError);
    }

    [Fact]
    public void GlobalCanNotReferenceGlobal()
    {
        // var x = 0;
        // var y = x;

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            VarDeclaration("x", null, LiteralExpression(0)),
            VarDeclaration("y", null, NameExpression("x"))));

        var xDecl = tree.GetNode<VariableDeclarationSyntax>(0);
        var xRef = tree.GetNode<NameExpressionSyntax>(0);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var xDeclSym = GetInternalSymbol<PropertySymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var xRefSym = semanticModel.GetReferencedSymbol(xRef);

        // TODO: Should see it, but should report illegal reference error
        // Assert
        Assert.False(ReferenceEquals(xDeclSym, xRefSym));
        Assert.False(xDeclSym.IsError);
        Assert.True(xRefSym!.IsError);
    }

    [Fact]
    public void GlobalCanReferenceFunction()
    {
        // var x = foo();
        // func foo(): int32 = 0;

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            VarDeclaration("x", null, CallExpression(NameExpression("foo"))),
            FunctionDeclaration(
                "foo",
                ParameterList(),
                NameType("int32"),
                InlineFunctionBody(LiteralExpression(0)))));

        var fooDecl = tree.GetNode<FunctionDeclarationSyntax>(0);
        var fooRef = tree.GetNode<NameExpressionSyntax>(0);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var fooDeclSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetDeclaredSymbol(fooDecl));
        var fooRefSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(fooRef));

        // Assert
        Assert.True(ReferenceEquals(fooDeclSym, fooRefSym));
    }

    [Fact]
    public void GotoToNonExistingLabel()
    {
        // func foo() {
        //     goto not_existing;
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(GotoExpression("non_existing"))))));

        var labelRef = tree.GetNode<GotoExpressionSyntax>(0).Target;

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var labelRefSym = semanticModel.GetReferencedSymbol(labelRef);

        // Assert
        Assert.True(labelRefSym!.IsError);
    }

    [Fact]
    public void GotoBreakLabelInCondition()
    {
        // func foo() {
        //     while ({ goto break; false }) {}
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(WhileExpression(
                        condition: BlockExpression(ImmutableArray.Create(ExpressionStatement(GotoExpression("break"))), LiteralExpression(false)),
                        then: BlockExpression()))))));

        var labelRef = tree.GetNode<GotoExpressionSyntax>(0).Target;

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var labelRefSym = GetInternalSymbol<LabelSymbol>(semanticModel.GetReferencedSymbol(labelRef));

        // Assert
        Assert.False(labelRefSym.IsError);
    }

    [Fact]
    public void GotoBreakLabelInInlineBody()
    {
        // func foo() {
        //     while (true) goto break;
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(WhileExpression(
                        condition: LiteralExpression(false),
                        then: GotoExpression("break")))))));

        var labelRef = tree.GetNode<GotoExpressionSyntax>(0).Target;

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var labelRefSym = GetInternalSymbol<LabelSymbol>(semanticModel.GetReferencedSymbol(labelRef));

        // Assert
        Assert.False(labelRefSym.IsError);
    }

    [Fact]
    public void GotoBreakLabelInBlockBody()
    {
        // func foo() {
        //     while (true) { goto break; }
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(WhileExpression(
                        condition: LiteralExpression(false),
                        then: BlockExpression(ExpressionStatement(GotoExpression("break")))))))));

        var labelRef = tree.GetNode<GotoExpressionSyntax>(0).Target;

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var labelRefSym = GetInternalSymbol<LabelSymbol>(semanticModel.GetReferencedSymbol(labelRef));

        // Assert
        Assert.False(labelRefSym.IsError);
    }

    [Fact]
    public void GotoBreakLabelOutsideOfBody()
    {
        // func foo() {
        //     while (true) {}
        //     goto break;
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(WhileExpression(
                        condition: LiteralExpression(false),
                        then: BlockExpression())),
                    ExpressionStatement(GotoExpression("break"))))));

        var labelRef = tree.GetNode<GotoExpressionSyntax>(0).Target;

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var labelRefSym = semanticModel.GetReferencedSymbol(labelRef);

        // Assert
        Assert.True(labelRefSym!.IsError);
    }

    [Fact]
    public void NestedLoopLabels()
    {
        // func foo() {
        //     while (true) {
        //         goto continue;
        //         while (true) {
        //             goto break;
        //             goto continue;
        //         }
        //         goto break;
        //     }
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(WhileExpression(
                        condition: LiteralExpression(true),
                        then: BlockExpression(
                            ExpressionStatement(GotoExpression("continue")),
                            ExpressionStatement(WhileExpression(
                                condition: LiteralExpression(true),
                                then: BlockExpression(
                                    ExpressionStatement(GotoExpression("break")),
                                    ExpressionStatement(GotoExpression("continue"))))),
                            ExpressionStatement(GotoExpression("break")))))))));

        var outerContinueRef = tree.GetNode<GotoExpressionSyntax>(0).Target;
        var innerBreakRef = tree.GetNode<GotoExpressionSyntax>(1).Target;
        var innerContinueRef = tree.GetNode<GotoExpressionSyntax>(2).Target;
        var outerBreakRef = tree.GetNode<GotoExpressionSyntax>(3).Target;

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var outerContinueRefSym = GetInternalSymbol<LabelSymbol>(semanticModel.GetReferencedSymbol(outerContinueRef));
        var innerBreakRefSym = GetInternalSymbol<LabelSymbol>(semanticModel.GetReferencedSymbol(innerBreakRef));
        var innerContinueRefSym = GetInternalSymbol<LabelSymbol>(semanticModel.GetReferencedSymbol(innerContinueRef));
        var outerBreakRefSym = GetInternalSymbol<LabelSymbol>(semanticModel.GetReferencedSymbol(outerBreakRef));

        // Assert
        Assert.False(outerContinueRefSym.IsError);
        Assert.False(innerBreakRefSym.IsError);
        Assert.False(innerContinueRefSym.IsError);
        Assert.False(outerBreakRefSym.IsError);
        Assert.False(ReferenceEquals(innerBreakRefSym, outerBreakRefSym));
        Assert.False(ReferenceEquals(innerContinueRefSym, outerContinueRefSym));
    }

    [Fact]
    public void ModuleIsIllegalInExpressionContext()
    {
        // func foo() {
        //     var a = System;
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("a", null, NameExpression("System")))))));

        var varDecl = tree.GetNode<VariableDeclarationSyntax>(0);
        var moduleRef = tree.GetNode<NameExpressionSyntax>(0);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var diags = semanticModel.Diagnostics;

        var localSymbol = GetInternalSymbol<LocalSymbol>(semanticModel.GetDeclaredSymbol(varDecl));
        var systemSymbol = GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(moduleRef));

        // Assert
        Assert.True(localSymbol.Type.IsError);
        Assert.False(systemSymbol.IsError);
        Assert.Single(diags);
        AssertDiagnostics(diags, TypeCheckingErrors.IllegalExpression);
    }

    [Fact]
    public void ImportPointsToNonExistingModuleInCompilationUnit()
    {
        // import Nonexisting;

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            ImportDeclaration("Nonexisting")));

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var diags = semanticModel.Diagnostics;

        // Assert
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.UndefinedReference);
    }

    [Fact]
    public void ImportPointsToNonExistingModuleInFunctionBody()
    {
        // func foo() {
        //     import Nonexisting;
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(FunctionDeclaration(
            "foo",
            ParameterList(),
            null,
            BlockFunctionBody(
                DeclarationStatement(ImportDeclaration("Nonexisting"))))));

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var diags = semanticModel.Diagnostics;

        // Assert
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.UndefinedReference);
    }

    [Fact]
    public void ImportIsNotAtTheTopOfCompilationUnit()
    {
        // func foo() {}
        // import System;

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody()),
            ImportDeclaration("System")));

        var importPath = tree.GetNode<ImportPathSyntax>(0);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var diags = semanticModel.Diagnostics;

        var systemSymbol = GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(importPath));

        // Assert
        Assert.False(systemSymbol.IsError);
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.ImportNotAtTop);
    }

    [Fact]
    public void ImportIsNotAtTheTopOfFunctionBody()
    {
        // func foo() {
        //     var x = 0;
        //     import System;
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(FunctionDeclaration(
            "foo",
            ParameterList(),
            null,
            BlockFunctionBody(
                DeclarationStatement(VarDeclaration("x", null, LiteralExpression(0))),
                DeclarationStatement(ImportDeclaration("System"))))));

        var importPath = tree.GetNode<ImportPathSyntax>(0);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var diags = semanticModel.Diagnostics;

        var systemSymbol = GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(importPath));

        // Assert
        Assert.False(systemSymbol.IsError);
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.ImportNotAtTop);
    }

    [Fact]
    public void ModuleAsReturnType()
    {
        // func foo(): System = 0;

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(FunctionDeclaration(
            "foo",
            ParameterList(),
            NameType("System"),
            InlineFunctionBody(
                LiteralExpression(0)))));

        var returnTypeSyntax = tree.GetNode<NameTypeSyntax>(0);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var diags = semanticModel.Diagnostics;

        var returnTypeSymbol = GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(returnTypeSyntax));

        // Assert
        Assert.NotNull(returnTypeSymbol);
        Assert.False(returnTypeSymbol.IsError);
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.IllegalModuleType);
    }

    [Fact]
    public void ModuleAsVariableType()
    {
        // func foo() {
        //     var x: System = 0;
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(FunctionDeclaration(
            "foo",
            ParameterList(),
            null,
            BlockFunctionBody(
                DeclarationStatement(VarDeclaration("x", NameType("System"), LiteralExpression(0)))))));

        var varTypeSyntax = tree.GetNode<NameTypeSyntax>(0);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var diags = semanticModel.Diagnostics;

        var varTypeSymbol = GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(varTypeSyntax));

        // Assert
        Assert.NotNull(varTypeSymbol);
        Assert.False(varTypeSymbol.IsError);
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.IllegalModuleType);
    }

    [Fact]
    public void VisibleElementFullyQualified()
    {
        // func main(){
        //   FooModule.foo();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(CallExpression(MemberExpression(NameExpression("FooModule"), "foo")))))),
            ToPath("Tests", "main.draco"));

        // internal func foo(): int32 = 0;

        var foo = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                Api.Semantics.Visibility.Internal,
                "foo",
                ParameterList(),
                NameType("int32"),
                InlineFunctionBody(LiteralExpression(0)))),
           ToPath("Tests", "FooModule", "foo.draco"));

        var fooDecl = foo.GetNode<FunctionDeclarationSyntax>(0);
        var fooCall = main.GetNode<CallExpressionSyntax>(0);

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main, foo],
            rootModulePath: ToPath("Tests"));

        var mainModel = compilation.GetSemanticModel(main);
        var fooModel = compilation.GetSemanticModel(foo);

        var diags = mainModel.Diagnostics;

        var fooCallSymbol = GetInternalSymbol<FunctionSymbol>(mainModel.GetReferencedSymbol(fooCall));
        var fooDeclSymbol = GetInternalSymbol<FunctionSymbol>(fooModel.GetDeclaredSymbol(fooDecl));

        // Assert
        Assert.Empty(diags);
        Assert.Equal(fooDeclSymbol, fooCallSymbol);
    }

    [Fact]
    public void NotVisibleElementFullyQualified()
    {
        // func main(){
        //   FooModule.foo();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(CallExpression(MemberExpression(NameExpression("FooModule"), "foo")))))),
            ToPath("Tests", "main.draco"));

        // func foo(): int32 = 0;

        var foo = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                NameType("int32"),
                InlineFunctionBody(LiteralExpression(0)))),
           ToPath("Tests", "FooModule", "foo.draco"));

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main, foo],
            rootModulePath: ToPath("Tests"));

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;

        // Assert
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.InaccessibleSymbol);
    }

    [Fact]
    public void VisibleElementFullyQualifiedInCodeDefinedModule()
    {
        // func main() {
        //   FooModule.foo();
        // }
        //
        // module FooModule{
        //   internal func foo(): int32 = 0;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(CallExpression(MemberExpression(NameExpression("FooModule"), "foo"))))),
            ModuleDeclaration(
                "FooModule",
                FunctionDeclaration(
                    Api.Semantics.Visibility.Internal,
                    "foo",
                    ParameterList(),
                    NameType("int32"),
                    InlineFunctionBody(LiteralExpression(0))))));

        var fooDecl = main.GetNode<FunctionDeclarationSyntax>(1);
        var fooCall = main.GetNode<CallExpressionSyntax>(0);

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            rootModulePath: ToPath("Tests"));

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;

        var fooCallSymbol = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(fooCall));
        var fooDeclSymbol = GetInternalSymbol<FunctionSymbol>(semanticModel.GetDeclaredSymbol(fooDecl));

        // Assert
        Assert.Empty(diags);
        Assert.Equal(fooDeclSymbol, fooCallSymbol);
    }

    [Fact]
    public void NotVisibleElementFullyQualifiedInCodeDefinedModule()
    {
        // func main() {
        //   FooModule.foo();
        // }
        //
        // module FooModule {
        //   func foo(): int32 = 0;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(CallExpression(MemberExpression(NameExpression("FooModule"), "foo"))))),
            ModuleDeclaration(
                "FooModule",
                FunctionDeclaration(
                    "foo",
                    ParameterList(),
                    NameType("int32"),
                    InlineFunctionBody(LiteralExpression(0))))));

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            rootModulePath: ToPath("Tests"));

        var fooCall = main.GetNode<MemberExpressionSyntax>(0);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooCallSymbol = GetInternalSymbol<Symbol>(semanticModel.GetReferencedSymbol(fooCall));

        // Assert
        Assert.Single(diags);
        Assert.False(fooCallSymbol.IsError);
        AssertDiagnostics(diags, SymbolResolutionErrors.InaccessibleSymbol);
    }

    [Fact]
    public void NotVisibleGlobalVariableFullyQualified()
    {
        // func main() {
        //   var x = BarModule.bar;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("x", type: null, value: MemberExpression(NameExpression("BarModule"), "bar")))))),
            ToPath("Tests", "main.draco"));

        // var bar = 0;

        var foo = SyntaxTree.Create(CompilationUnit(
            VarDeclaration("bar", type: null, value: LiteralExpression(0))),
           ToPath("Tests", "BarModule", "bar.draco"));

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main, foo],
            rootModulePath: ToPath("Tests"));

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;

        // Assert
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.InaccessibleSymbol);
    }

    [Fact]
    public void InternalElementImportedFromDifferentAssembly()
    {
        // import FooModule;
        // func main() {
        //   Foo();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            ImportDeclaration("FooModule"),
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(CallExpression(NameExpression("Foo")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class FooModule{
                internal static void Foo() { }
            }
            """);

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;

        // Assert
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.InaccessibleSymbol);
    }

    [Fact]
    public void InternalElementFullyQualifiedFromDifferentAssembly()
    {
        // func main() {
        //   FooModule.Foo();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(CallExpression(MemberExpression(NameExpression("FooModule"), "Foo")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class FooModule{
                internal static void Foo() { }
            }
            """);

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;

        // Assert
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.InaccessibleSymbol);
    }

    [Fact]
    public void VisibleElementImported()
    {
        // import FooModule;
        // func main(){
        //   foo();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            ImportDeclaration("FooModule"),
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(CallExpression(NameExpression("foo")))))),
            ToPath("Tests", "main.draco"));

        // internal func foo(): int32 = 0;

        var foo = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                Api.Semantics.Visibility.Internal,
                "foo",
                ParameterList(),
                NameType("int32"),
                InlineFunctionBody(LiteralExpression(0)))),
           ToPath("Tests", "FooModule", "foo.draco"));

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main, foo],
            rootModulePath: ToPath("Tests"));

        var fooDecl = foo.GetNode<FunctionDeclarationSyntax>(0);
        var fooCall = main.GetNode<CallExpressionSyntax>(0);

        var mainModel = compilation.GetSemanticModel(main);
        var fooModel = compilation.GetSemanticModel(foo);

        var diags = mainModel.Diagnostics;

        var fooCallSymbol = GetInternalSymbol<FunctionSymbol>(mainModel.GetReferencedSymbol(fooCall));
        var fooDeclSymbol = GetInternalSymbol<FunctionSymbol>(fooModel.GetDeclaredSymbol(fooDecl));

        // Assert
        Assert.Empty(diags);
        Assert.Equal(fooDeclSymbol, fooCallSymbol);
    }

    [Fact]
    public void NotVisibleElementImported()
    {
        // import FooModule;
        // func main(){
        //   foo();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            ImportDeclaration("FooModule"),
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(CallExpression(NameExpression("foo")))))),
            ToPath("Tests", "main.draco"));

        // func foo(): int32 = 0;

        var foo = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                NameType("int32"),
                InlineFunctionBody(LiteralExpression(0)))),
           ToPath("Tests", "FooModule", "foo.draco"));

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main, foo],
            rootModulePath: ToPath("Tests"));

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;

        // Assert
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.UndefinedReference);
    }

    [Fact]
    public void VisibleElementImportedInCodeDefinedModule()
    {
        // import FooModule;
        // func main(){
        //   foo();
        // }
        //
        // module FooModule{
        //   internal func foo(): int32 = 0;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            ImportDeclaration("FooModule"),
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(CallExpression(NameExpression("foo"))))),
            ModuleDeclaration(
                "FooModule",
                FunctionDeclaration(
                    Api.Semantics.Visibility.Internal,
                    "foo",
                    ParameterList(),
                    NameType("int32"),
                    InlineFunctionBody(LiteralExpression(0))))));

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            rootModulePath: ToPath("Tests"));

        var fooDecl = main.GetNode<FunctionDeclarationSyntax>(1);
        var fooCall = main.GetNode<CallExpressionSyntax>(0);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;

        var fooCallSymbol = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(fooCall));
        var fooDeclSymbol = GetInternalSymbol<FunctionSymbol>(semanticModel.GetDeclaredSymbol(fooDecl));

        // Assert
        Assert.Empty(diags);
        Assert.Equal(fooDeclSymbol, fooCallSymbol);
    }

    [Fact]
    public void NotVisibleElementImportedInCodeDefinedModule()
    {
        // import FooModule;
        // func main(){
        //   foo();
        // }
        //
        // module FooModule{
        //   func foo(): int32 = 0;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            ImportDeclaration("FooModule"),
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(CallExpression(NameExpression("foo"))))),
            ModuleDeclaration(
                "FooModule",
                FunctionDeclaration(
                    "foo",
                    ParameterList(),
                    NameType("int32"),
                    InlineFunctionBody(LiteralExpression(0))))));

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            rootModulePath: ToPath("Tests"));

        var fooCall = main.GetNode<NameExpressionSyntax>(0);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooCallSymbol = GetInternalSymbol<Symbol>(semanticModel.GetReferencedSymbol(fooCall));

        // Assert
        Assert.Single(diags);
        Assert.True(fooCallSymbol.IsError);
        AssertDiagnostics(diags, SymbolResolutionErrors.UndefinedReference);
    }

    [Fact]
    public void ElementFromTheSameModuleButDifferentFile()
    {
        // func main(){
        //   foo();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(CallExpression(NameExpression("foo")))))),
            ToPath("Tests", "main.draco"));

        // func foo(): int32 = 0;

        var foo = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                NameType("int32"),
                InlineFunctionBody(LiteralExpression(0)))),
           ToPath("Tests", "foo.draco"));

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main, foo],
            rootModulePath: ToPath("Tests"));

        var fooDecl = foo.GetNode<FunctionDeclarationSyntax>(0);
        var fooCall = main.GetNode<CallExpressionSyntax>(0);

        var mainModel = compilation.GetSemanticModel(main);
        var fooModel = compilation.GetSemanticModel(foo);

        var diags = mainModel.Diagnostics;

        var fooCallSymbol = GetInternalSymbol<FunctionSymbol>(mainModel.GetReferencedSymbol(fooCall));
        var fooDeclSymbol = GetInternalSymbol<FunctionSymbol>(fooModel.GetDeclaredSymbol(fooDecl));

        // Assert
        Assert.Empty(diags);
        Assert.Equal(fooDeclSymbol, fooCallSymbol);
    }

    [Fact]
    public void SyntaxTreeOutsideOfRoot()
    {
        // func main() { }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody())),
            ToPath("NotRoot", "main.draco"));

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            rootModulePath: ToPath("Tests"));

        var diags = compilation.Diagnostics;

        // Assert
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.FilePathOutsideOfRootPath);
    }

    [Fact]
    public void InCodeModuleImports()
    {
        // import System.Text;
        //
        // module FooModule {
        //     import System.Console;
        //
        //     func bar()
        //     {
        //         var sb = StringBuilder(); // OK
        //         WriteLine(sb.ToString()); // OK
        //     }
        // }
        //
        // func baz()
        // {
        //     var sb = StringBuilder(); // OK
        //     WriteLine(); // ERROR
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            ImportDeclaration("System", "Text"),
            ModuleDeclaration(
                "FooModule",
                ImportDeclaration("System", "Console"),
                FunctionDeclaration(
                    "bar",
                    ParameterList(),
                    null,
                    BlockFunctionBody(
                        DeclarationStatement(VarDeclaration("sb", null, CallExpression(NameExpression("StringBuilder")))),
                        ExpressionStatement(CallExpression(NameExpression("WriteLine"), CallExpression(MemberExpression(NameExpression("sb"), "ToString"))))))),
            FunctionDeclaration(
                "baz",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("sb", null, CallExpression(NameExpression("StringBuilder")))),
                    ExpressionStatement(CallExpression(NameExpression("WriteLine")))))));

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            rootModulePath: ToPath("Tests"));

        var writeLineCall = main.GetNode<CallExpressionSyntax>(4).Function;

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var writeLineCallSymbol = GetInternalSymbol<Symbol>(semanticModel.GetReferencedSymbol(writeLineCall));

        // Assert
        Assert.Single(diags);
        Assert.True(writeLineCallSymbol.IsError);
        AssertDiagnostics(diags, SymbolResolutionErrors.UndefinedReference);
    }

    [Fact]
    public void UndefinedTypeInReturnType()
    {
        // func foo(): unknown { }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(FunctionDeclaration(
            "foo",
            ParameterList(),
            NameType("unknown"),
            BlockFunctionBody())));

        var returnTypeSyntax = tree.GetNode<NameTypeSyntax>(0);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var diags = semanticModel.Diagnostics;

        var returnTypeSymbol = semanticModel.GetReferencedSymbol(returnTypeSyntax);

        // Assert
        Assert.NotNull(returnTypeSymbol);
        Assert.True(returnTypeSymbol.IsError);
        Assert.Equal(2, diags.Length);
        AssertDiagnostics(diags, SymbolResolutionErrors.UndefinedReference);
        AssertDiagnostics(diags, FlowAnalysisErrors.DoesNotReturn);
    }

    [Fact]
    public void UndefinedTypeInParameterType()
    {
        // func foo(x: unknown) { }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(FunctionDeclaration(
            "foo",
            ParameterList(Parameter("x", NameType("unknown"))),
            null,
            BlockFunctionBody())));

        var paramTypeSyntax = tree.GetNode<NameTypeSyntax>(0);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var diags = semanticModel.Diagnostics;

        var paramTypeSymbol = semanticModel.GetReferencedSymbol(paramTypeSyntax);

        // Assert
        Assert.NotNull(paramTypeSymbol);
        Assert.True(paramTypeSymbol.IsError);
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.UndefinedReference);
    }

    [Fact]
    public void ReadingAndSettingStaticFieldFullyQualified()
    {
        // func main(){
        //   FooModule.foo = 5;
        //   var x = FooModule.foo;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(BinaryExpression(MemberExpression(NameExpression("FooModule"), "foo"), Assign, LiteralExpression(5))),
                    DeclarationStatement(VarDeclaration("x", null, MemberExpression(NameExpression("FooModule"), "foo")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class FooModule{
                public static int foo = 0;
            }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(0);
        var fooModuleRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooSym = AssertMember<FieldSymbol>(GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(fooModuleRef)), "foo");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooModule", "foo");

        // Assert
        Assert.Empty(diags);
        Assert.False(xSym.IsError);
        Assert.False(fooSym.IsError);
        Assert.Same(fooSym, fooDecl);
    }

    [Fact]
    public void ReadingAndSettingStaticFieldImported()
    {
        // import FooModule;
        // func main(){
        //   foo = 5;
        //   var x = foo;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            ImportDeclaration("FooModule"),
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(BinaryExpression(NameExpression("foo"), Assign, LiteralExpression(5))),
                    DeclarationStatement(VarDeclaration("x", null, NameExpression("foo")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class FooModule{
                public static int foo = 0;
            }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(0);
        var fooNameRef = main.GetNode<NameExpressionSyntax>(0);

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooSym = GetInternalSymbol<FieldSymbol>(semanticModel.GetReferencedSymbol(fooNameRef));
        var fooDecl = GetMetadataSymbol(compilation, null, "FooModule", "foo");

        // Assert
        Assert.Empty(diags);
        Assert.False(xSym.IsError);
        Assert.False(fooSym.IsError);
        Assert.Same(fooSym, fooDecl);
    }

    [Fact]
    public void ReadingAndSettingNonStaticField()
    {
        // func main(){
        //   var fooType = FooType();
        //   fooType.foo = 5;
        //   var x = fooType.foo;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("fooType", null, CallExpression(NameExpression("FooType")))),
                    ExpressionStatement(BinaryExpression(MemberExpression(NameExpression("fooType"), "foo"), Assign, LiteralExpression(5))),
                    DeclarationStatement(VarDeclaration("x", null, MemberExpression(NameExpression("fooType"), "foo")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType{
                public int foo = 0;
            }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(1);
        var fooTypeRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooSym = AssertMember<FieldSymbol>(GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type, "foo");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooType", "foo");

        // Assert
        Assert.Empty(diags);
        Assert.False(xSym.IsError);
        Assert.False(fooSym.IsError);
        Assert.Same(fooSym, fooDecl);
    }

    [Fact]
    public void SettingReadonlyStaticField()
    {
        // func main(){
        //   FooModule.foo = 5;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(BinaryExpression(MemberExpression(NameExpression("FooModule"), "foo"), Assign, LiteralExpression(5)))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class FooModule{
                public static readonly int foo = 0;
            }
            """);

        var fooModuleRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooSym = AssertMember<FieldSymbol>(GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(fooModuleRef)), "foo");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooModule", "foo");

        // Assert
        Assert.Single(diags);
        Assert.False(fooSym.IsError);
        Assert.Same(fooSym, fooDecl);
        AssertDiagnostics(diags, FlowAnalysisErrors.ImmutableVariableAssignedMultipleTimes);
    }

    [Fact]
    public void SettingConstantField()
    {
        // func main(){
        //   FooModule.foo = 5;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(BinaryExpression(MemberExpression(NameExpression("FooModule"), "foo"), Assign, LiteralExpression(5)))))));


        var fooRef = CompileCSharpToMetadataReference("""
            public static class FooModule{
                public const int foo = 0;
            }
            """);

        var fooModuleRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooSym = AssertMember<FieldSymbol>(GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(fooModuleRef)), "foo");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooModule", "foo");

        // Assert
        Assert.Single(diags);
        Assert.False(fooSym.IsError);
        Assert.Same(fooSym, fooDecl);
        AssertDiagnostics(diags, FlowAnalysisErrors.ImmutableVariableAssignedMultipleTimes);
    }

    [Fact]
    public void SettingReadonlyNonStaticField()
    {
        // func main(){
        //   var fooType = FooType();
        //   fooType.foo = 5;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("fooType", null, CallExpression(NameExpression("FooType")))),
                    ExpressionStatement(BinaryExpression(MemberExpression(NameExpression("fooType"), "foo"), Assign, LiteralExpression(5)))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType{
                public readonly int foo = 0;
            }
            """);

        var fooModuleRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooSym = AssertMember<FieldSymbol>(GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooModuleRef)).Type, "foo");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooType", "foo");

        // Assert
        Assert.Single(diags);
        Assert.False(fooSym.IsError);
        Assert.Same(fooSym, fooDecl);
        AssertDiagnostics(diags, FlowAnalysisErrors.ImmutableVariableAssignedMultipleTimes);
    }

    [Fact]
    public void ReadingNonExistingNonStaticField()
    {
        // func main(){
        //   var fooType = FooType();
        //   var x = fooType.foo;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("fooType", null, CallExpression(NameExpression("FooType")))),
                    DeclarationStatement(VarDeclaration("x", null, MemberExpression(NameExpression("fooType"), "foo")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType { }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(1);
        var fooTypeRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooTypeSym = GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type;
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "FooType");

        // Assert
        Assert.Single(diags);
        Assert.False(fooTypeSym.IsError);
        Assert.False(xSym.IsError);
        Assert.Same(fooTypeSym, fooTypeDecl);
        AssertDiagnostics(diags, SymbolResolutionErrors.MemberNotFound);
    }

    [Fact]
    public void SettingNonExistingNonStaticField()
    {
        // func main(){
        //   var fooType = FooType();
        //   fooType.foo = 5;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("fooType", null, CallExpression(NameExpression("FooType")))),
                    ExpressionStatement(BinaryExpression(MemberExpression(NameExpression("fooType"), "foo"), Assign, LiteralExpression(5)))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType { }
            """);

        var fooTypeRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooTypeSym = GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type;
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "FooType");

        // Assert
        Assert.Single(diags);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeSym, fooTypeDecl);
        AssertDiagnostics(diags, SymbolResolutionErrors.MemberNotFound);
    }

    [Fact]
    public void SettingNonExistingStaticField()
    {
        // func main(){
        //   FooModule.foo = 5;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(BinaryExpression(MemberExpression(NameExpression("FooModule"), "foo"), Assign, LiteralExpression(5)))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class FooModule { }
            """);

        var fooModuleRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooModuleSym = GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(fooModuleRef));
        var fooModuleDecl = GetMetadataSymbol(compilation, null, "FooModule");

        // Assert
        Assert.Single(diags);
        Assert.False(fooModuleSym.IsError);
        Assert.Same(fooModuleSym, fooModuleDecl);
        AssertDiagnostics(diags, SymbolResolutionErrors.UndefinedReference);
    }

    [Fact]
    public void ReadingNonExistingStaticField()
    {
        // func main(){
        //   var x = FooModule.foo;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("x", null, MemberExpression(NameExpression("FooModule"), "foo")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class FooModule { }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(0);
        var fooModuleRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooModuleSym = GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(fooModuleRef));
        var fooModuleDecl = GetMetadataSymbol(compilation, null, "FooModule");

        // Assert
        Assert.Single(diags);
        Assert.False(fooModuleSym.IsError);
        Assert.False(xSym.IsError);
        Assert.Same(fooModuleSym, fooModuleDecl);
        AssertDiagnostics(diags, SymbolResolutionErrors.UndefinedReference);
    }

    [Fact]
    public void ReadingAndSettingStaticPropertyFullyQualified()
    {
        // func main(){
        //   FooModule.foo = 5;
        //   var x = FooModule.foo;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(BinaryExpression(MemberExpression(NameExpression("FooModule"), "foo"), Assign, LiteralExpression(5))),
                    DeclarationStatement(VarDeclaration("x", null, MemberExpression(NameExpression("FooModule"), "foo")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class FooModule{
                public static int foo { get; set; }
            }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(0);
        var fooModuleRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooSym = AssertMember<PropertySymbol>(GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(fooModuleRef)), "foo");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooModule", "foo");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooSym.IsError);
        Assert.False(xSym.IsError);
        Assert.Same(fooSym, fooDecl);
    }

    [Fact]
    public void ReadingAndSettingStaticPropertyImported()
    {
        // import FooModule;
        // func main(){
        //   foo = 5;
        //   var x = foo;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            ImportDeclaration("FooModule"),
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(BinaryExpression(NameExpression("foo"), Assign, LiteralExpression(5))),
                    DeclarationStatement(VarDeclaration("x", null, NameExpression("foo")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class FooModule{
                public static int foo { get; set; }
            }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(0);
        var fooAssignRef = main.GetNode<BinaryExpressionSyntax>(0);

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooSym = GetInternalSymbol<PropertySymbol>(semanticModel.GetReferencedSymbol(fooAssignRef));
        var fooDecl = GetMetadataSymbol(compilation, null, "FooModule", "foo");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooSym.IsError);
        Assert.False(xSym.IsError);
        Assert.Same(fooSym, fooDecl);
    }

    [Fact]
    public void ReadingAndSettingNonStaticProperty()
    {
        // func main(){
        //   var fooType = FooType();
        //   fooType.foo = 5;
        //   var x = fooType.foo;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("fooType", null, CallExpression(NameExpression("FooType")))),
                    ExpressionStatement(BinaryExpression(MemberExpression(NameExpression("fooType"), "foo"), Assign, LiteralExpression(5))),
                    DeclarationStatement(VarDeclaration("x", null, MemberExpression(NameExpression("fooType"), "foo")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType{
                public int foo { get; set; }
            }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(1);
        var fooTypeRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooSym = AssertMember<PropertySymbol>(GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type, "foo");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooType", "foo");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooSym.IsError);
        Assert.False(xSym.IsError);
        Assert.Same(fooSym, fooDecl);

    }

    [Fact]
    public void SettingGetOnlyStaticProperty()
    {
        // func main(){
        //   FooModule.foo = 5;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(BinaryExpression(MemberExpression(NameExpression("FooModule"), "foo"), Assign, LiteralExpression(5)))))));


        var fooRef = CompileCSharpToMetadataReference("""
            public static class FooModule{
                public static int foo { get; }
            }
            """);

        var fooModuleRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooSym = AssertMember<PropertySymbol>(GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(fooModuleRef)), "foo");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooModule", "foo");

        // Assert
        Assert.Single(diags);
        Assert.False(fooSym.IsError);
        Assert.Same(fooSym, fooDecl);
        AssertDiagnostics(diags, SymbolResolutionErrors.CannotSetGetOnlyProperty);
    }

    [Fact]
    public void GettingSetOnlyStaticProperty()
    {
        // func main(){
        //   var x = FooModule.foo;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("x", null, MemberExpression(NameExpression("FooModule"), "foo")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class FooModule{
                public static int foo { set { } }
            }
            """);

        var fooModuleRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooSym = AssertMember<PropertySymbol>(GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(fooModuleRef)), "foo");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooModule", "foo");

        // Assert
        Assert.Single(diags);
        Assert.False(fooSym.IsError);
        Assert.Same(fooSym, fooDecl);
        AssertDiagnostics(diags, SymbolResolutionErrors.CannotGetSetOnlyProperty);
    }

    [Fact]
    public void SettingGetOnlyNonStaticProperty()
    {
        // func main(){
        //   var fooType = FooType();
        //   fooType.foo = 5;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("fooType", null, CallExpression(NameExpression("FooType")))),
                    ExpressionStatement(BinaryExpression(MemberExpression(NameExpression("fooType"), "foo"), Assign, LiteralExpression(5)))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType{
                public int foo { get; }
            }
            """);

        var fooTypeRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooSym = AssertMember<PropertySymbol>(GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type, "foo");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooType", "foo");

        // Assert
        Assert.Single(diags);
        Assert.False(fooSym.IsError);
        Assert.Same(fooSym, fooDecl);
        AssertDiagnostics(diags, SymbolResolutionErrors.CannotSetGetOnlyProperty);
    }

    [Fact]
    public void GettingSetOnlyNonStaticProperty()
    {
        // func main(){
        //   var fooType = FooType();
        //   var x = fooType.foo;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("fooType", null, CallExpression(NameExpression("FooType")))),
                    DeclarationStatement(VarDeclaration("x", null, MemberExpression(NameExpression("fooType"), "foo")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType{
                public int foo { set { } }
            }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(1);
        var fooTypeRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooSym = AssertMember<PropertySymbol>(GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type, "foo");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooType", "foo");

        // Assert
        Assert.Single(diags);
        Assert.False(fooSym.IsError);
        Assert.False(xSym.IsError);
        Assert.Same(fooSym, fooDecl);
        AssertDiagnostics(diags, SymbolResolutionErrors.CannotGetSetOnlyProperty);
    }

    [Fact]
    public void CompoundAssignmentNonStaticProperty()
    {
        // func main(){
        //   var fooType = FooType();
        //   fooType.foo += 2;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("fooType", null, CallExpression(NameExpression("FooType")))),
                    ExpressionStatement(BinaryExpression(MemberExpression(NameExpression("fooType"), "foo"), PlusAssign, LiteralExpression(2)))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType{
                public int foo { get; set; }
            }
            """);

        var fooTypeRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooSym = AssertMember<PropertySymbol>(GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type, "foo");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooType", "foo");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooSym.IsError);
        Assert.Same(fooSym, fooDecl);
    }

    [Fact]
    public void CompoundAssignmentStaticProperty()
    {
        // func main(){
        //   FooModule.foo += 2;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(BinaryExpression(MemberExpression(NameExpression("FooModule"), "foo"), PlusAssign, LiteralExpression(2)))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class FooModule{
                public static int foo { get; set; }
            }
            """);

        var fooModuleRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooSym = AssertMember<PropertySymbol>(GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(fooModuleRef)), "foo");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooModule", "foo");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooSym.IsError);
        Assert.Same(fooSym, fooDecl);
    }

    [Fact]
    public void ReadingAndSettingIndexer()
    {
        // func main(){
        //   var fooType = FooType();
        //   fooType[0] = 5;
        //   var x = fooType[0];
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("fooType", null, CallExpression(NameExpression("FooType")))),
                    ExpressionStatement(BinaryExpression(IndexExpression(NameExpression("fooType"), LiteralExpression(0)), Assign, LiteralExpression(5))),
                    DeclarationStatement(VarDeclaration("x", null, IndexExpression(NameExpression("fooType"), LiteralExpression(0))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType{
                public int this[int index]
                {
                    get => index * 2;
                    set { }
                }
            }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(1);
        var fooTypeRef = main.GetNode<IndexExpressionSyntax>(0).Indexed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooSym = AssertMember<PropertySymbol>(GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type, "Item");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooType", "Item");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooSym.IsError);
        Assert.False(xSym.IsError);
        Assert.Same(fooSym, fooDecl);
    }

    [Fact]
    public void SettingGetOnlyIndexer()
    {
        // func main(){
        //   var fooType = FooType();
        //   fooType[0] = 5;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("fooType", null, CallExpression(NameExpression("FooType")))),
                    ExpressionStatement(BinaryExpression(IndexExpression(NameExpression("fooType"), LiteralExpression(0)), Assign, LiteralExpression(5)))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType{
                public int this[int index] => index * 2;
            }
            """);

        var fooAssignRef = main.GetNode<BinaryExpressionSyntax>(0);

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooSym = GetInternalSymbol<ErrorPropertySymbol>(semanticModel.GetReferencedSymbol(fooAssignRef));

        // Assert
        Assert.Single(diags);
        Assert.True(fooSym.IsError);
        AssertDiagnostics(diags, SymbolResolutionErrors.NoSettableIndexerInType);
    }

    [Fact]
    public void GettingSetOnlyIndexer()
    {
        // func main(){
        //   var fooType = FooType();
        //   var x = fooType[0];
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("fooType", null, CallExpression(NameExpression("FooType")))),
                    DeclarationStatement(VarDeclaration("x", null, IndexExpression(NameExpression("fooType"), LiteralExpression(0))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType{
                public int this[int index] { set { } }
            }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(1);
        var fooTypeRef = main.GetNode<IndexExpressionSyntax>(0).Indexed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooSym = AssertMember<PropertySymbol>(GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type, "Item");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooType", "Item");

        // Assert
        Assert.Single(diags);
        Assert.False(fooSym.IsError);
        Assert.False(xSym.IsError);
        Assert.Same(fooSym, fooDecl);
        AssertDiagnostics(diags, SymbolResolutionErrors.NoGettableIndexerInType);
    }

    [Fact]
    public void ReadingAndSettingMemberAccessIndexer()
    {
        // func main(){
        //   var fooType = FooType();
        //   fooType.foo[0] = 5;
        //   var x = fooType.foo[0];
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("fooType", null, CallExpression(NameExpression("FooType")))),
                    ExpressionStatement(BinaryExpression(IndexExpression(MemberExpression(NameExpression("fooType"), "foo"), LiteralExpression(0)), Assign, LiteralExpression(5))),
                    DeclarationStatement(VarDeclaration("x", null, IndexExpression(MemberExpression(NameExpression("fooType"), "foo"), LiteralExpression(0))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType{
                public Foo foo = new Foo();
            }
            public class Foo{
                public int this[int index]
                {
                    get => index * 2;
                    set { }
                }
            }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(1);
        var fooTypeRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooSym = AssertMember<FieldSymbol>(GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type, "foo");
        var indexSym = AssertMember<PropertySymbol>(fooSym.Type, "Item");
        var indexDecl = GetMetadataSymbol(compilation, null, "Foo", "Item");

        // Assert
        Assert.Empty(diags);
        Assert.False(xSym.IsError);
        Assert.False(fooSym.IsError);
        Assert.False(indexSym.IsError);
        Assert.Same(indexSym, indexDecl);
    }

    [Fact]
    public void SettingGetOnlyMemberAccessIndexer()
    {
        // func main(){
        //   var fooType = FooType();
        //   fooType.foo[0] = 5;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("fooType", null, CallExpression(NameExpression("FooType")))),
                    ExpressionStatement(BinaryExpression(IndexExpression(MemberExpression(NameExpression("fooType"), "foo"), LiteralExpression(0)), Assign, LiteralExpression(5)))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType{
                public Foo foo = new Foo();
            }
            public class Foo{
                public int this[int index] => index * 2;
            }
            """);

        var fooAssignRef = main.GetNode<BinaryExpressionSyntax>(0);

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooSym = GetInternalSymbol<ErrorPropertySymbol>(semanticModel.GetReferencedSymbol(fooAssignRef));

        // Assert
        Assert.Single(diags);
        Assert.True(fooSym.IsError);
        AssertDiagnostics(diags, SymbolResolutionErrors.NoSettableIndexerInType);
    }

    [Fact]
    public void GettingSetOnlyMemberAccessIndexer()
    {
        // func main(){
        //   var fooType = FooType();
        //   var x = fooType.foo[0];
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("fooType", null, CallExpression(NameExpression("FooType")))),
                    DeclarationStatement(VarDeclaration("x", null, IndexExpression(MemberExpression(NameExpression("fooType"), "foo"), LiteralExpression(0))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType{
                public Foo foo = new Foo();
            }
            public class Foo{
                public int this[int index] { set { } }
            }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(1);
        var fooTypeRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooSym = AssertMember<FieldSymbol>(GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type, "foo");
        var indexSym = AssertMember<PropertySymbol>(fooSym.Type, "Item");
        var indexDecl = GetMetadataSymbol(compilation, null, "Foo", "Item");

        // Assert
        Assert.Single(diags);
        Assert.False(xSym.IsError);
        Assert.False(fooSym.IsError);
        Assert.False(indexSym.IsError);
        Assert.Same(indexSym, indexDecl);
        AssertDiagnostics(diags, SymbolResolutionErrors.NoGettableIndexerInType);
    }

    [Fact]
    public void GettingNonExistingIndexer()
    {
        // func main(){
        //   var foo = FooType();
        //   var x = foo[0];
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression(NameExpression("FooType")))),
                    DeclarationStatement(VarDeclaration("x", null, IndexExpression(NameExpression("foo"), LiteralExpression(0))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType { }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(1);
        var fooTypeRef = main.GetNode<IndexExpressionSyntax>(0).Indexed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooTypeSym = GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type;
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "FooType");

        // Assert
        Assert.Single(diags);
        Assert.False(xSym.IsError);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeSym, fooTypeDecl);
        AssertDiagnostics(diags, SymbolResolutionErrors.NoGettableIndexerInType);
    }

    [Fact]
    public void SettingNonExistingIndexer()
    {
        // func main(){
        //   var foo = FooType();
        //   foo[0] = 5;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression(NameExpression("FooType")))),
                    ExpressionStatement(BinaryExpression(IndexExpression(NameExpression("foo"), LiteralExpression(0)), Assign, LiteralExpression(5)))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType { }
            """);

        var fooAssignRef = main.GetNode<BinaryExpressionSyntax>(0);

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooSym = GetInternalSymbol<ErrorPropertySymbol>(semanticModel.GetReferencedSymbol(fooAssignRef));

        // Assert
        Assert.Single(diags);
        Assert.True(fooSym.IsError);
        AssertDiagnostics(diags, SymbolResolutionErrors.NoSettableIndexerInType);
    }

    [Fact]
    public void CompoundAssignmentIndexer()
    {
        // func main(){
        //   var foo = FooType();
        //   foo[0] += 2;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression(NameExpression("FooType")))),
                    ExpressionStatement(BinaryExpression(IndexExpression(NameExpression("foo"), LiteralExpression(0)), PlusAssign, LiteralExpression(2)))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType
            {
                public int this[int index]
                {
                    get => index * 2;
                    set { }
                }
            }
            """);

        var fooTypeRef = main.GetNode<IndexExpressionSyntax>(0).Indexed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooSym = AssertMember<PropertySymbol>(GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type, "Item");
        var fooDecl = GetMetadataSymbol(compilation, null, "FooType", "Item");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooSym.IsError);
        Assert.Same(fooSym, fooDecl);
    }

    [Fact]
    public void NestedTypeWithStaticParentInTypeContextAndConstructorFullyQualified()
    {
        // func foo(): ParentType.FooType = ParentType.FooType();

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                MemberType(NameType("ParentType"), "FooType"),
                InlineFunctionBody(
                    CallExpression(MemberExpression(NameExpression("ParentType"), "FooType"))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class ParentType
            {
                public class FooType { }
            }
            """);

        var parentTypeRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooTypeSym = AssertMember<TypeSymbol>(GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(parentTypeRef)), "FooType");
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "ParentType", "FooType");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeDecl, fooTypeSym);
    }

    [Fact]
    public void NestedTypeWithStaticParentInTypeContextAndConstructorImported()
    {
        // import ParentType;
        // func foo(): FooType = FooType();

        var main = SyntaxTree.Create(CompilationUnit(
            ImportDeclaration("ParentType"),
            FunctionDeclaration(
                "foo",
                ParameterList(),
                NameType("FooType"),
                InlineFunctionBody(
                    CallExpression(NameExpression("FooType"))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class ParentType{
                public class FooType { }
            }
            """);

        var fooTypeRef = main.GetNode<TypeSyntax>(0);

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooTypeSym = GetInternalSymbol<TypeSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef));
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "ParentType", "FooType");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeDecl, fooTypeSym);
    }

    [Fact]
    public void NestedTypeWithNonStaticParentInTypeContextAndConstructor()
    {
        // func foo(): ParentType.FooType = ParentType.FooType();

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                MemberType(NameType("ParentType"), "FooType"),
                InlineFunctionBody(
                    CallExpression(MemberExpression(NameExpression("ParentType"), "FooType"))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class ParentType
            {
                public class FooType { }
            }
            """);

        var parentTypeRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooTypeSym = AssertMember<TypeSymbol>(GetInternalSymbol<TypeSymbol>(semanticModel.GetReferencedSymbol(parentTypeRef)), "FooType");
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "ParentType", "FooType");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeDecl, fooTypeSym);
    }

    [Fact]
    public void NestedTypeWithStaticParentStaticMemberAccess()
    {
        // func foo()
        // {
        //   var x = ParentType.FooType.foo;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("x", null, MemberExpression(MemberExpression(NameExpression("ParentType"), "FooType"), "foo")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class ParentType
            {
                public class FooType
                {
                    public static int foo = 0;
                }
            }
            """);

        var parentTypeRef = main.GetNode<MemberExpressionSyntax>(1).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooTypeSym = AssertMember<TypeSymbol>(GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(parentTypeRef)), "FooType");
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "ParentType", "FooType");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeDecl, fooTypeSym);
    }

    [Fact]
    public void NestedTypeWithNonStaticParentStaticMemberAccess()
    {
        // func foo()
        // {
        //   var x = ParentType.FooType.foo;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("x", null, MemberExpression(MemberExpression(NameExpression("ParentType"), "FooType"), "foo")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class ParentType
            {
                public class FooType
                {
                    public static int foo = 0;
                }
            }
            """);

        var parentTypeRef = main.GetNode<MemberExpressionSyntax>(1).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooTypeSym = AssertMember<TypeSymbol>(GetInternalSymbol<TypeSymbol>(semanticModel.GetReferencedSymbol(parentTypeRef)), "FooType");
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "ParentType", "FooType");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeDecl, fooTypeSym);
    }

    [Fact]
    public void NestedTypeWithStaticParentNonStaticMemberAccess()
    {
        // func foo()
        // {
        //   var foo = ParentType.FooType();
        //   var x = foo.member;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression(MemberExpression(NameExpression("ParentType"), "FooType")))),
                    DeclarationStatement(VarDeclaration("x", null, MemberExpression(NameExpression("foo"), "member")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public static class ParentType
            {
                public class FooType
                {
                    public int member = 0;
                }
            }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(1);
        var fooTypeRef = main.GetNode<MemberExpressionSyntax>(1).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooTypeSym = GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type;
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "ParentType", "FooType");

        // Assert
        Assert.Empty(diags);
        Assert.False(xSym.IsError);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeSym, fooTypeDecl);
    }

    [Fact]
    public void NestedTypeWithNonStaticParentNonStaticMemberAccess()
    {
        // func foo()
        // {
        //   var foo = ParentType.FooType();
        //   var x = foo.member;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression(MemberExpression(NameExpression("ParentType"), "FooType")))),
                    DeclarationStatement(VarDeclaration("x", null, MemberExpression(NameExpression("foo"), "member")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class ParentType
            {
                public class FooType
                {
                    public int member = 0;
                }
            }
            """);

        var xDecl = main.GetNode<VariableDeclarationSyntax>(1);
        var fooTypeRef = main.GetNode<MemberExpressionSyntax>(1).Accessed;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fooTypeSym = GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type;
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "ParentType", "FooType");

        // Assert
        Assert.Empty(diags);
        Assert.False(xSym.IsError);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeSym, fooTypeDecl);
    }

    [Fact]
    public void GenericFunction()
    {
        // func identity<T>(x: T): T = x;

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(FunctionDeclaration(
            "foo",
            GenericParameterList(GenericParameter("T")),
            ParameterList(Parameter("x", NameType("T"))),
            NameType("T"),
            InlineFunctionBody(NameExpression("x")))));

        var functionSyntax = tree.GetNode<FunctionDeclarationSyntax>(0);
        var genericTypeSyntax = tree.GetNode<GenericParameterSyntax>(0);
        var paramTypeSyntax = tree.GetNode<NameTypeSyntax>(0);
        var returnTypeSyntax = tree.GetNode<NameTypeSyntax>(1);

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var diags = semanticModel.Diagnostics;

        var functionSymbol = GetInternalSymbol<FunctionSymbol>(semanticModel.GetDeclaredSymbol(functionSyntax));
        var genericTypeSymbol = GetInternalSymbol<TypeParameterSymbol>(semanticModel.GetDeclaredSymbol(genericTypeSyntax));
        var paramTypeSymbol = GetInternalSymbol<TypeParameterSymbol>(semanticModel.GetReferencedSymbol(paramTypeSyntax));
        var returnTypeSymbol = GetInternalSymbol<TypeParameterSymbol>(semanticModel.GetReferencedSymbol(returnTypeSyntax));

        // Assert
        Assert.True(functionSymbol.IsGenericDefinition);
        Assert.NotNull(genericTypeSymbol);
        Assert.Same(genericTypeSymbol, paramTypeSymbol);
        Assert.Same(genericTypeSymbol, returnTypeSymbol);
        Assert.Empty(diags);
    }

    [Fact]
    public void InheritanceFromObject()
    {
        // func foo()
        // {
        //   var foo = FooType();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression((NameExpression("FooType")))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType { }
            """);

        var fooTypeRef = main.GetNode<CallExpressionSyntax>(0).Function;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooTypeSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).ReturnType;
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "FooType");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeSym, fooTypeDecl);
        Assert.Single(fooTypeSym.ImmediateBaseTypes);
        Assert.Equal("System.Object", fooTypeSym.ImmediateBaseTypes[0].FullName);
    }

    [Fact]
    public void InheritanceFromTypeDefinition()
    {
        // func foo()
        // {
        //   var foo = FooType();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression((NameExpression("FooType")))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class ParentType { }
            public class FooType : ParentType { }
            """);

        var fooTypeRef = main.GetNode<CallExpressionSyntax>(0).Function;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooTypeSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).ReturnType;
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "FooType");
        var parentTypeDecl = GetMetadataSymbol(compilation, null, "ParentType");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeSym, fooTypeDecl);
        Assert.Single(fooTypeSym.ImmediateBaseTypes);
        Assert.Equal(parentTypeDecl, fooTypeSym.ImmediateBaseTypes[0]);
    }

    [Fact]
    public void InheritanceFromNestedTypeReference()
    {
        // func foo()
        // {
        //   var foo = FooType();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression((NameExpression("FooType")))))))));

        var baseStream = CompileCSharpToStream("""
            public class ParentType
            {
                public class BaseType { }
            }
            """, "Base.dll");

        var baseRef = MetadataReference.FromPeStream(baseStream);

        baseStream.Position = 0;

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType : ParentType.BaseType { }
            """, aditionalReferences: [baseStream]);

        var fooTypeRef = main.GetNode<CallExpressionSyntax>(0).Function;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [baseRef, fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooTypeSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).ReturnType;
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "FooType");
        var baseTypeDecl = GetMetadataSymbol(compilation, "Base.dll", "ParentType", "BaseType");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeSym, fooTypeDecl);
        Assert.Single(fooTypeSym.ImmediateBaseTypes);
        Assert.Equal(baseTypeDecl, fooTypeSym.ImmediateBaseTypes[0]);
    }

    [Fact]
    public void InheritanceFromTypeSpecification()
    {
        // func foo()
        // {
        //   var foo = FooType();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression((NameExpression("FooType")))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class ParentType<T> { }
            public class FooType : ParentType<int> { }
            """);

        var fooTypeRef = main.GetNode<CallExpressionSyntax>(0).Function;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooTypeSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).ReturnType;
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "FooType");
        var parentTypeDecl = GetMetadataSymbol(compilation, null, "ParentType`1");
        var baseTypeSym = fooTypeSym.ImmediateBaseTypes[0];

        // Assert
        Assert.Empty(diags);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeSym, fooTypeDecl);
        Assert.Single(fooTypeSym.ImmediateBaseTypes);
        Assert.True(baseTypeSym.IsGenericInstance);
        Assert.False(baseTypeSym.IsGenericDefinition);
        Assert.Same(parentTypeDecl, baseTypeSym.GenericDefinition);
        Assert.Single(baseTypeSym.GenericArguments);
        Assert.Same(compilation.WellKnownTypes.SystemInt32, baseTypeSym.GenericArguments[0]);
    }

    [Fact]
    public void InheritingInterfacesFromTypeDefinition()
    {
        // func foo()
        // {
        //   var foo = FooType();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression((NameExpression("FooType")))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public interface ParentInterface { }
            public class FooType : ParentInterface { }
            """);

        var fooTypeRef = main.GetNode<CallExpressionSyntax>(0).Function;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooTypeSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).ReturnType;
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "FooType");
        var parentInterfaceDecl = GetMetadataSymbol(compilation, null, "ParentInterface");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeSym, fooTypeDecl);
        Assert.Equal(2, fooTypeSym.ImmediateBaseTypes.Length);
        Assert.Equal("System.Object", fooTypeSym.ImmediateBaseTypes[0].FullName);
        Assert.Equal(parentInterfaceDecl, fooTypeSym.ImmediateBaseTypes[1]);
    }

    [Fact]
    public void InheritingInterfacesFromTypeReference()
    {
        // func foo()
        // {
        //   var foo = FooType();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression((NameExpression("FooType")))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class FooType : System.ICloneable
            {
                public object Clone() => new object();
            }
            """);

        var fooTypeRef = main.GetNode<CallExpressionSyntax>(0).Function;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooTypeSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).ReturnType;
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "FooType");

        // Assert
        Assert.Empty(diags);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeSym, fooTypeDecl);
        Assert.Equal(2, fooTypeSym.ImmediateBaseTypes.Length);
        Assert.Equal("System.Object", fooTypeSym.ImmediateBaseTypes[0].FullName);
        Assert.Equal("System.ICloneable", fooTypeSym.ImmediateBaseTypes[1].FullName);
    }

    [Fact]
    public void InheretingInterfaceFromTypeSpecification()
    {
        // func foo()
        // {
        //   var foo = FooType();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression((NameExpression("FooType")))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public interface ParentInterface<T> { }
            public class FooType : ParentInterface<int> { }
            """);

        var fooTypeRef = main.GetNode<CallExpressionSyntax>(0).Function;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var fooTypeSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).ReturnType;
        var fooTypeDecl = GetMetadataSymbol(compilation, null, "FooType");
        var parentInterfaceDecl = GetMetadataSymbol(compilation, null, "ParentInterface`1");
        var baseInterfaceSym = fooTypeSym.ImmediateBaseTypes[^1];

        // Assert
        Assert.Empty(diags);
        Assert.False(fooTypeSym.IsError);
        Assert.Same(fooTypeSym, fooTypeDecl);
        Assert.Equal(2, fooTypeSym.ImmediateBaseTypes.Length);
        Assert.Equal("System.Object", fooTypeSym.ImmediateBaseTypes[0].FullName);
        Assert.True(baseInterfaceSym.IsGenericInstance);
        Assert.False(baseInterfaceSym.IsGenericDefinition);
        Assert.Same(parentInterfaceDecl, baseInterfaceSym.GenericDefinition);
        Assert.Single(baseInterfaceSym.GenericArguments);
        Assert.Same(compilation.WellKnownTypes.SystemInt32, baseInterfaceSym.GenericArguments[0]);
    }

    [Fact]
    public void AccessingMemberOfBaseType()
    {
        // func foo()
        // {
        //   var foo = FooType();
        //   var x = foo.Field;
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression(NameExpression("FooType")))),
                    DeclarationStatement(VarDeclaration("x", null, MemberExpression(NameExpression("foo"), "Field")))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class ParentType
            {
                public int Field = 5;
            }
            public class FooType : ParentType { }
            """);

        var fooTypeRef = main.GetNode<MemberExpressionSyntax>(0).Accessed;
        var xDecl = main.GetNode<VariableDeclarationSyntax>(0);

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var xSym = GetInternalSymbol<VariableSymbol>(semanticModel.GetDeclaredSymbol(xDecl));
        var fieldSym = AssertMember<FieldSymbol>(GetInternalSymbol<LocalSymbol>(semanticModel.GetReferencedSymbol(fooTypeRef)).Type.ImmediateBaseTypes[0], "Field");
        var fieldDecl = GetMetadataSymbol(compilation, null, "ParentType", "Field");

        // Assert
        Assert.Empty(diags);
        Assert.False(fieldSym.IsError);
        Assert.False(xSym.IsError);
        Assert.Same(fieldSym, fieldDecl);
    }

    [Fact]
    public void ImplicitOverrideFunction()
    {
        // func foo()
        // {
        //   var foo = Derived();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression(NameExpression("Derived"))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class Base
            {
                public virtual Base Clone() => this;
            }

            public class Derived : Base
            {
                public override Base Clone() => this;
            }
            """);

        var derivedRef = main.GetNode<CallExpressionSyntax>(0).Function;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var derivedSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(derivedRef)).ReturnType;
        var derivedDecl = GetMetadataSymbol(compilation, null, "Derived");
        var nonObjectSymbols = derivedSym.NonSpecialMembers.Where(x => x.ContainingSymbol?.FullName != "System.Object");

        // Assert
        Assert.Empty(diags);
        Assert.Same(derivedDecl, derivedSym);
        Assert.Single(nonObjectSymbols);
        Assert.Equal("Derived.Clone", nonObjectSymbols.First().FullName);
    }

    [Fact]
    public void ExplicitOverrideFunctionInSameAssembly()
    {
        // func foo()
        // {
        //   var foo = Derived();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression(NameExpression("Derived"))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class Base
            {
                public virtual Base Clone(int parameter) => this;
            }

            public class Derived : Base
            {
                public override Derived Clone(int parameter) => this;
            }
            """);

        var derivedRef = main.GetNode<CallExpressionSyntax>(0).Function;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var derivedSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(derivedRef)).ReturnType;
        var derivedDecl = GetMetadataSymbol(compilation, null, "Derived");
        var nonObjectSymbols = derivedSym.NonSpecialMembers.Where(x => x.ContainingSymbol?.FullName != "System.Object");

        // Assert
        Assert.Empty(diags);
        Assert.Same(derivedDecl, derivedSym);
        Assert.Single(nonObjectSymbols);
        Assert.Equal("Derived.Clone", nonObjectSymbols.First().FullName);
    }

    [Fact]
    public void ExplicitOverrideFunctionInDifferentAssembly()
    {
        // func foo()
        // {
        //   var foo = Derived();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression(NameExpression("Derived"))))))));

        var baseStream = CompileCSharpToStream("""
            public class Base
            {
                public virtual Base Clone(int parameter) => this;
            }
            """, "Base.dll");

        var baseRef = MetadataReference.FromPeStream(baseStream);

        baseStream.Position = 0;

        var fooRef = CompileCSharpToMetadataReference("""
            public class Derived : Base
            {
                public override Derived Clone(int parameter) => this;
            }
            """, aditionalReferences: [baseStream]);

        var derivedRef = main.GetNode<CallExpressionSyntax>(0).Function;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef, baseRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var derivedSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(derivedRef)).ReturnType;
        var derivedDecl = GetMetadataSymbol(compilation, null, "Derived");
        var nonObjectSymbols = derivedSym.NonSpecialMembers.Where(x => x.ContainingSymbol?.FullName != "System.Object");

        // Assert
        Assert.Empty(diags);
        Assert.Same(derivedDecl, derivedSym);
        Assert.Single(nonObjectSymbols);
        Assert.Equal("Derived.Clone", nonObjectSymbols.First().FullName);
    }

    [Fact]
    public void ImplicitOverrideProperty()
    {
        // func foo()
        // {
        //   var foo = Derived();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression(NameExpression("Derived"))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class Base
            {
                public virtual Base Clone => this;
            }

            public class Derived : Base
            {
                public override Base Clone => this;
            }
            """);

        var derivedRef = main.GetNode<CallExpressionSyntax>(0).Function;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var derivedSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(derivedRef)).ReturnType;
        var derivedDecl = GetMetadataSymbol(compilation, null, "Derived");
        var nonObjectSymbols = derivedSym.NonSpecialMembers.Where(x => x.ContainingSymbol?.FullName != "System.Object");

        // Assert
        Assert.Empty(diags);
        Assert.Same(derivedDecl, derivedSym);
        Assert.Single(nonObjectSymbols);
        Assert.Equal("Derived.Clone", nonObjectSymbols.First().FullName);
    }

    [Fact]
    public void ExplicitOverrideProperty()
    {
        // func foo()
        // {
        //   var foo = Derived();
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    DeclarationStatement(VarDeclaration("foo", null, CallExpression(NameExpression("Derived"))))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class Base
            {
                public virtual Base Clone => this;
            }

            public class Derived : Base
            {
                public override Derived Clone => this;
            }
            """);

        var derivedRef = main.GetNode<CallExpressionSyntax>(0).Function;

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;
        var derivedSym = GetInternalSymbol<FunctionSymbol>(semanticModel.GetReferencedSymbol(derivedRef)).ReturnType;
        var derivedDecl = GetMetadataSymbol(compilation, null, "Derived");
        var nonObjectSymbols = derivedSym.NonSpecialMembers.Where(x => x.ContainingSymbol?.FullName != "System.Object");

        // Assert
        Assert.Empty(diags);
        Assert.Same(derivedDecl, derivedSym);
        Assert.Single(nonObjectSymbols);
        Assert.Equal("Derived.Clone", nonObjectSymbols.First().FullName);
    }

    [Fact]
    public void InCodeModules()
    {
        // module Foo {
        //     public func bar() {}
        // }
        //
        // func main() {
        //     Foo.bar();
        // }

        // Arrange
        var tree = SyntaxTree.Create(CompilationUnit(
            ModuleDeclaration(
                "Foo",
                FunctionDeclaration(
                    Api.Semantics.Visibility.Public,
                    "bar",
                    ParameterList(),
                    null,
                    BlockFunctionBody())),
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(CallExpression(MemberExpression(NameExpression("Foo"), "bar")))))));

        var moduleDeclSyntax = tree.GetNode<ModuleDeclarationSyntax>(0);
        var moduleRefSyntax = tree.GetNode<MemberExpressionSyntax>(0).Accessed;

        // Act
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);

        var diags = semanticModel.Diagnostics;

        var moduleDefSymbol = GetInternalSymbol<ModuleSymbol>(semanticModel.GetDeclaredSymbol(moduleDeclSyntax));
        var moduleRefSymbol = GetInternalSymbol<ModuleSymbol>(semanticModel.GetReferencedSymbol(moduleRefSyntax));

        // Assert
        Assert.True(SymbolEqualityComparer.Default.Equals(moduleDefSymbol, moduleRefSymbol));
        Assert.Empty(diags);
    }

    [Fact]
    public void ForeachSequenceHasNoGetEnumerator()
    {
        // func main() {
        //     for (i in 0) {}
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "main",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(ForExpression("i", LiteralExpression(0), BlockExpression()))))));

        // Act
        var compilation = CreateCompilation(main);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;

        // Assert
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.MemberNotFound);
    }

    [Fact]
    public void ForeachEnumeratorHasNoMoveNext()
    {
        // func foo() {
        //     for (i in Seq()) {}
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(ForExpression(
                        "i",
                        CallExpression(NameExpression("Seq")),
                        BlockExpression()))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class Seq
            {
                public TestEnumerator GetEnumerator() => default;
            }

            public struct TestEnumerator
            {
                public int Current => 0;
            }
            """);

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;

        // Assert
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.MemberNotFound);
    }

    [Fact]
    public void ForeachEnumeratorHasNoCurrentProperty()
    {
        // func foo() {
        //     for (i in Seq()) {}
        // }

        var main = SyntaxTree.Create(CompilationUnit(
            FunctionDeclaration(
                "foo",
                ParameterList(),
                null,
                BlockFunctionBody(
                    ExpressionStatement(ForExpression(
                        "i",
                        CallExpression(NameExpression("Seq")),
                        BlockExpression()))))));

        var fooRef = CompileCSharpToMetadataReference("""
            public class Seq
            {
                public TestEnumerator GetEnumerator() => default;
            }

            public struct TestEnumerator
            {
                public int Current;

                public bool MoveNext() => true;
            }
            """);

        // Act
        var compilation = CreateCompilation(
            syntaxTrees: [main],
            additionalReferences: [fooRef]);

        var semanticModel = compilation.GetSemanticModel(main);

        var diags = semanticModel.Diagnostics;

        // Assert
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.NotGettableProperty);
    }

    [Fact]
    public void ReturningInGlobalBlockIsIllegal()
    {
        // val a = { return 4; };

        var main = SyntaxTree.Create(CompilationUnit(
            ValDeclaration(
                "a",
                null,
                BlockExpression(ExpressionStatement(ReturnExpression(LiteralExpression(4)))))));

        // Act
        var compilation = CreateCompilation(main);
        var semanticModel = compilation.GetSemanticModel(main);
        var diags = semanticModel.Diagnostics;

        // Assert
        Assert.Single(diags);
        AssertDiagnostics(diags, SymbolResolutionErrors.IllegalReturn);
    }
}

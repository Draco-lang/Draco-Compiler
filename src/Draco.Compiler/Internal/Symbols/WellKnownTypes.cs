using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using Draco.Compiler.Api;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.OptimizingIr.Codegen;
using Draco.Compiler.Internal.OptimizingIr.Model;
using Draco.Compiler.Internal.Symbols.Error;
using Draco.Compiler.Internal.Symbols.Generic;
using Draco.Compiler.Internal.Symbols.Metadata;
using Draco.Compiler.Internal.Symbols.Synthetized;
using Draco.Compiler.Internal.Symbols.Synthetized.Array;
using Draco.Compiler.Internal.Utilities;
using static Draco.Compiler.Internal.OptimizingIr.InstructionFactory;

namespace Draco.Compiler.Internal.Symbols;

/// <summary>
/// A collection of well-known types that the compiler needs.
/// </summary>
internal sealed partial class WellKnownTypes(Compilation compilation)
{
    #region AllSymbols Helper
    /// <summary>
    /// A utility for all intrinsic symbols.
    /// </summary>
    public ImmutableArray<Symbol> AllSymbols => InterlockedUtils.InitializeDefault(ref this.allSymbols, this.BuildAllSymbols);
    private ImmutableArray<Symbol> allSymbols;
    private ImmutableArray<Symbol> BuildAllSymbols() =>
        this.GenerateWellKnownTypes().ToImmutableArray();

    // NOTE: We don't yield each primitive directly, we need to alias them
    private IEnumerable<Symbol> GenerateWellKnownTypes()
    {
        // Primitive aliases
        yield return Alias("char", this.SystemChar);
        yield return Alias("bool", this.SystemBoolean);

        yield return Alias("uint8", this.SystemByte);
        yield return Alias("uint16", this.SystemUInt16);
        yield return Alias("uint32", this.SystemUInt32);
        yield return Alias("uint64", this.SystemUInt64);

        yield return Alias("int8", this.SystemByte);
        yield return Alias("int16", this.SystemInt16);
        yield return Alias("int32", this.SystemInt32);
        yield return Alias("int64", this.SystemInt64);

        yield return Alias("float32", this.SystemSingle);
        yield return Alias("float64", this.SystemDouble);

        yield return Alias("string", this.SystemString);
        yield return Alias("object", this.SystemObject);

        // Default value
        yield return DefaultValueFunctionSymbol.Instance;

        // 1D array
        yield return this.ArrayType;
        yield return this.ArrayCtor;

        // Array types from 2D to 8D
        for (var i = 2; i <= 8; ++i)
        {
            // Type
            var arrayType = new ArrayTypeSymbol(compilation, i, this.SystemInt32);
            yield return arrayType;
            // Ctor
            yield return new ArrayConstructorSymbol(arrayType);
        }

        // Bool negation
        yield return this.Bool_Not;

        // Numeric operators
        foreach (var type in new[]
        {
            this.SystemSByte, this.SystemInt16, this.SystemInt32, this.SystemInt64,
            this.SystemByte, this.SystemUInt16, this.SystemUInt32, this.SystemUInt64,
             this.SystemSingle, this.SystemDouble,
        })
        {
            // Comparison
            yield return this.Comparison(TokenKind.Equal, type, type, FromAllocating(this.CodegenEqual));
            yield return this.Comparison(TokenKind.NotEqual, type, type, FromAllocating(this.CodegenNotEqual));
            yield return this.Comparison(TokenKind.GreaterThan, type, type, FromAllocating(this.CodegenGreater));
            yield return this.Comparison(TokenKind.LessThan, type, type, FromAllocating(this.CodegenLess));
            yield return this.Comparison(TokenKind.GreaterEqual, type, type, FromAllocating(this.CodegenGreaterEqual));
            yield return this.Comparison(TokenKind.LessEqual, type, type, FromAllocating(this.CodegenLessEqual));

            // Unary
            yield return this.Unary(TokenKind.Plus, type, type, this.CodegenPlus);
            yield return this.Unary(TokenKind.Minus, type, type, FromAllocating(this.CodegenMinus));

            // Binary
            yield return this.Binary(TokenKind.Plus, type, type, type, FromAllocating(this.CodegenAdd));
            yield return this.Binary(TokenKind.Minus, type, type, type, FromAllocating(this.CodegenSub));
            yield return this.Binary(TokenKind.Star, type, type, type, FromAllocating(this.CodegenMul));
            yield return this.Binary(TokenKind.Slash, type, type, type, FromAllocating(this.CodegenDiv));
            yield return this.Binary(TokenKind.KeywordMod, type, type, type, FromAllocating(this.CodegenMod));
            yield return this.Binary(TokenKind.KeywordRem, type, type, type, FromAllocating(this.CodegenRem));
        }

        // The operators that make sense for characters too
        {
            yield return this.Comparison(TokenKind.Equal, this.SystemChar, this.SystemChar, FromAllocating(this.CodegenEqual));
            yield return this.Comparison(TokenKind.NotEqual, this.SystemChar, this.SystemChar, FromAllocating(this.CodegenNotEqual));
            yield return this.Comparison(TokenKind.GreaterThan, this.SystemChar, this.SystemChar, FromAllocating(this.CodegenGreater));
            yield return this.Comparison(TokenKind.LessThan, this.SystemChar, this.SystemChar, FromAllocating(this.CodegenLess));
            yield return this.Comparison(TokenKind.GreaterEqual, this.SystemChar, this.SystemChar, FromAllocating(this.CodegenGreaterEqual));
            yield return this.Comparison(TokenKind.LessEqual, this.SystemChar, this.SystemChar, FromAllocating(this.CodegenLessEqual));
        }

        // String addition
        yield return this.Binary(TokenKind.Plus, this.SystemString, this.SystemString, this.SystemString,
            FromAllocating((codegen, target, operands) => codegen.Write(Call(target, this.SystemString_Concat, operands))));
    }

    private static SynthetizedAliasSymbol Alias(string name, TypeSymbol type) =>
        new(name, type);
    #endregion

    #region Singletons
    public static TypeSymbol Never => NeverTypeSymbol.Instance;
    public static TypeSymbol ErrorType { get; } = new ErrorTypeSymbol("<error>");
    public static TypeSymbol UninferredType { get; } = new ErrorTypeSymbol("?");
    public static TypeSymbol Unit { get; } = new PrimitiveTypeSymbol("unit", isValueType: true);
    #endregion

    #region Methods
    /// <summary>
    /// object.ToString().
    /// </summary>
    public MetadataMethodSymbol SystemObject_ToString => LazyInitializer.EnsureInitialized(
        ref this.object_ToString,
        () => this.SystemObject
            .AllMembers
            .OfType<MetadataMethodSymbol>()
            .Single(m => m.Name == "ToString"));
    private MetadataMethodSymbol? object_ToString;

    /// <summary>
    /// string.Format(string formatString, object[] args).
    /// </summary>
    public MetadataMethodSymbol SystemString_Format => LazyInitializer.EnsureInitialized(
        ref this.systemString_Format,
        () => this.SystemString
            .AllMembers
            .OfType<MetadataMethodSymbol>()
            .First(m =>
                m.Name == "Format"
             && m.Parameters is [_, { Type: TypeInstanceSymbol { GenericDefinition: ArrayTypeSymbol } }]));
    private MetadataMethodSymbol? systemString_Format;

    /// <summary>
    /// string.Concat(string str1, string str2).
    /// </summary>
    public MetadataMethodSymbol SystemString_Concat => LazyInitializer.EnsureInitialized(
        ref this.systemString_Concat,
        () => this.SystemString
            .AllMembers
            .OfType<MetadataMethodSymbol>()
            .First(m =>
                m.Name == "Concat"
             && m.Parameters.Length == 2
             && m.Parameters.All(p => SymbolEqualityComparer.Default.Equals(p.Type, this.SystemString))));
    private MetadataMethodSymbol? systemString_Concat;

    public TypeSymbol InstantiateArray(TypeSymbol elementType, int rank = 1) => rank switch
    {
        1 => this.ArrayType.GenericInstantiate(elementType),
        int n => new ArrayTypeSymbol(compilation, n, this.SystemInt32).GenericInstantiate(elementType),
    };

    #endregion

    #region Additives/Mixins for types
    /// <summary>
    /// Returns all the equality operator members for an enum type.
    /// </summary>
    /// <param name="type">The enum type.</param>
    /// <returns>The equality operator members.</returns>
    public IEnumerable<Symbol> GetEnumEqualityMembers(TypeSymbol type)
    {
        if (!type.IsEnumType) throw new ArgumentException("the type must be an enum type", nameof(type));

        // == and !=
        yield return this.Comparison(TokenKind.Equal, type, type, FromAllocating(this.CodegenEqual));
        yield return this.Comparison(TokenKind.NotEqual, type, type, FromAllocating(this.CodegenNotEqual));
    }
    #endregion

    public ArrayTypeSymbol ArrayType => LazyInitializer.EnsureInitialized(ref this.array, () => new(compilation, 1, this.SystemInt32));
    private ArrayTypeSymbol? array;

    public ArrayConstructorSymbol ArrayCtor => LazyInitializer.EnsureInitialized(ref this.arrayCtor, () => new(this.ArrayType));
    private ArrayConstructorSymbol? arrayCtor;

    /// <summary>
    /// Translates a <see cref="Type"/> to a <see cref="TypeSymbol"/>, if it's a well-known primitive type.
    /// </summary>
    /// <param name="type">The reflected type to translate.</param>
    /// <returns>The translated type symbol, or <see langword="null"/> if it's not a translatable primitive type.</returns>
    public TypeSymbol? TranslatePrimitive(Type type)
    {
        if (type == typeof(byte)) return this.SystemByte;
        if (type == typeof(ushort)) return this.SystemUInt16;
        if (type == typeof(uint)) return this.SystemUInt32;
        if (type == typeof(ulong)) return this.SystemUInt64;

        if (type == typeof(sbyte)) return this.SystemSByte;
        if (type == typeof(short)) return this.SystemInt16;
        if (type == typeof(int)) return this.SystemInt32;
        if (type == typeof(long)) return this.SystemInt64;

        if (type == typeof(float)) return this.SystemSingle;
        if (type == typeof(double)) return this.SystemDouble;

        if (type == typeof(bool)) return this.SystemBoolean;
        if (type == typeof(char)) return this.SystemChar;

        if (type == typeof(string)) return this.SystemString;
        if (type == typeof(object)) return this.SystemObject;

        if (type == typeof(Type)) return this.SystemType;

        return null;
    }

    #region Loader Methods
    public MetadataTypeSymbol GetTypeFromAssembly(AssemblyName name, ImmutableArray<string> path)
    {
        var assembly = this.GetAssemblyWithAssemblyName(name);
        return this.GetTypeFromAssembly(assembly, path);
    }

    public MetadataTypeSymbol GetTypeFromAssembly(MetadataAssemblySymbol assembly, ImmutableArray<string> path) =>
        assembly.Lookup(path).OfType<MetadataTypeSymbol>().First();

    public Symbol GetDotnetTypeFromAssembly(AssemblyName name, ImmutableArray<string> path)
    {
        var assembly = this.GetAssemblyWithAssemblyName(name);
        return this.GetDotnetTypeFromAssembly(assembly, path);
    }

    public Symbol GetDotnetTypeFromAssembly(MetadataAssemblySymbol assembly, ImmutableArray<string> path) =>
        assembly.Lookup(path).Where(s => s.IsTypeOnCilLevel).First();

    private MetadataAssemblySymbol GetAssemblyWithAssemblyName(AssemblyName name) =>
        compilation.MetadataAssemblies.First(asm => AssemblyNameComparer.Full.Equals(asm.AssemblyName, name));

    private MetadataAssemblySymbol GetAssemblyWithNameAndToken(string name, byte[] token)
    {
        var assemblyName = new AssemblyName() { Name = name };
        assemblyName.SetPublicKeyToken(token);
        return compilation.MetadataAssemblies
            .FirstOrDefault(asm => AssemblyNameComparer.NameAndToken.Equals(asm.AssemblyName, assemblyName))
            ?? throw new InvalidOperationException($"Failed to locate assembly with name '{name}' and public key token '{BitConverter.ToString(token)}'.");
    }

    #endregion

    #region Operators

    private FunctionSymbol Unary(
        TokenKind token,
        TypeSymbol operandType,
        TypeSymbol returnType,
        FunctionSymbol.CodegenDelegate codegen) =>
        DelegateIrFunctionSymbol.UnaryOperator(token, operandType, returnType, codegen);
    private FunctionSymbol Binary(
        TokenKind token,
        TypeSymbol leftType,
        TypeSymbol rightType,
        TypeSymbol returnType,
        FunctionSymbol.CodegenDelegate codegen) =>
        DelegateIrFunctionSymbol.BinaryOperator(token, leftType, rightType, returnType, codegen);
    private FunctionSymbol Comparison(
        TokenKind token,
        TypeSymbol leftType,
        TypeSymbol rightType,
        FunctionSymbol.CodegenDelegate codegen) =>
        DelegateIrFunctionSymbol.ComparisonOperator(token, leftType, rightType, this.SystemBoolean, codegen);

    public FunctionSymbol Bool_Not => LazyInitializer.EnsureInitialized(
       ref this.bool_not,
       () => this.Unary(TokenKind.KeywordNot, this.SystemBoolean, this.SystemBoolean, FromAllocating(this.CodegenNot)));
    private FunctionSymbol? bool_not;
    #endregion

    #region Codegen Methods

    private delegate void AllocatingCodegenDelegate(
        LocalCodegen codegen,
        Register target,
        ImmutableArray<IOperand> operands);

    private static FunctionSymbol.CodegenDelegate FromAllocating(AllocatingCodegenDelegate codegen) =>
        (body, targetType, operands) =>
        {
            var target = body.DefineRegister(targetType);
            codegen(body, target, operands);
            return target;
        };

    private IOperand CodegenPlus(LocalCodegen codegen, TypeSymbol targetType, ImmutableArray<IOperand> operands) =>
        // Simply return the operand holding the value
        operands[0];

    private void CodegenMinus(LocalCodegen codegen, Register target, ImmutableArray<IOperand> operands) =>
        codegen.Write(Mul(target, operands[0], new Constant(-1, this.SystemInt32)));

    private void CodegenNot(LocalCodegen codegen, Register target, ImmutableArray<IOperand> operands) =>
        codegen.Write(Equal(target, operands[0], new Constant(false, this.SystemBoolean)));

    private void CodegenAdd(LocalCodegen codegen, Register target, ImmutableArray<IOperand> operands) =>
        codegen.Write(Add(target, operands[0], operands[1]));

    private void CodegenSub(LocalCodegen codegen, Register target, ImmutableArray<IOperand> operands) =>
        codegen.Write(Sub(target, operands[0], operands[1]));

    private void CodegenMul(LocalCodegen codegen, Register target, ImmutableArray<IOperand> operands) =>
        codegen.Write(Mul(target, operands[0], operands[1]));

    private void CodegenDiv(LocalCodegen codegen, Register target, ImmutableArray<IOperand> operands) =>
        codegen.Write(Div(target, operands[0], operands[1]));

    private void CodegenRem(LocalCodegen codegen, Register target, ImmutableArray<IOperand> operands) =>
        codegen.Write(Rem(target, operands[0], operands[1]));

    private void CodegenMod(LocalCodegen codegen, Register target, ImmutableArray<IOperand> operands)
    {
        // a mod b
        //  <=>
        // (a rem b + b) rem b
        var tmp1 = codegen.DefineRegister(target.Type);
        var tmp2 = codegen.DefineRegister(target.Type);
        codegen.Write(Rem(tmp1, operands[0], operands[1]));
        codegen.Write(Add(tmp2, tmp1, operands[1]));
        codegen.Write(Rem(target, tmp1, operands[1]));
    }

    private void CodegenLess(LocalCodegen codegen, Register target, ImmutableArray<IOperand> operands) =>
        codegen.Write(Less(target, operands[0], operands[1]));

    private void CodegenGreater(LocalCodegen codegen, Register target, ImmutableArray<IOperand> operands) =>
        // a > b
        //  <=>
        // b < a
        codegen.Write(Less(target, operands[1], operands[0]));

    private void CodegenLessEqual(LocalCodegen codegen, Register target, ImmutableArray<IOperand> operands)
    {
        // a <= b
        //  <=>
        // (b < a) == false
        var tmp = codegen.DefineRegister(this.SystemBoolean);
        codegen.Write(Less(tmp, operands[1], operands[0]));
        codegen.Write(Equal(target, tmp, new Constant(false, this.SystemBoolean)));
    }

    private void CodegenGreaterEqual(LocalCodegen codegen, Register target, ImmutableArray<IOperand> operands)
    {
        // a >= b
        //  <=>
        // (a < b) == false
        var tmp = codegen.DefineRegister(this.SystemBoolean);
        codegen.Write(Less(tmp, operands[0], operands[1]));
        codegen.Write(Equal(target, tmp, new Constant(false, this.SystemBoolean)));
    }

    private void CodegenEqual(LocalCodegen codegen, Register target, ImmutableArray<IOperand> operands) =>
        codegen.Write(Equal(target, operands[0], operands[1]));

    private void CodegenNotEqual(LocalCodegen codegen, Register target, ImmutableArray<IOperand> operands)
    {
        // a != b
        //  <=>
        // (a == b) == false
        var tmp = codegen.DefineRegister(this.SystemBoolean);
        codegen.Write(Equal(tmp, operands[0], operands[1]));
        codegen.Write(Equal(target, tmp, new Constant(false, this.SystemBoolean)));
    }
    #endregion
}

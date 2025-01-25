using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Draco.Compiler.Internal.Documentation;
using Draco.Compiler.Internal.Symbols.Generic;
using Draco.Compiler.Internal.Utilities;
using static Draco.Compiler.Internal.OptimizingIr.InstructionFactory;

namespace Draco.Compiler.Internal.Symbols.Synthetized;

/// <summary>
/// A constructor function for types.
/// </summary>
internal sealed class ConstructorFunctionSymbol(FunctionSymbol ctorDefinition) : FunctionSymbol
{
    public override Symbol? ContainingSymbol => ctorDefinition.ContainingSymbol;
    public override ImmutableArray<AttributeInstance> Attributes => this.ConstructorSymbol.Attributes;
    public override string Name => this.InstantiatedType.Name;
    public override Api.Semantics.Visibility Visibility =>
        ctorDefinition.Visibility < this.ReturnType.Visibility ? ctorDefinition.Visibility : this.ReturnType.Visibility;
    public override SymbolDocumentation Documentation => ctorDefinition.Documentation;
    internal override string RawDocumentation => ctorDefinition.RawDocumentation;

    public override ImmutableArray<TypeParameterSymbol> GenericParameters =>
        InterlockedUtils.InitializeDefault(ref this.genericParameters, this.BuildGenericParameters);
    private ImmutableArray<TypeParameterSymbol> genericParameters;

    public override ImmutableArray<ParameterSymbol> Parameters =>
        InterlockedUtils.InitializeDefault(ref this.parameters, this.BuildParameters);
    private ImmutableArray<ParameterSymbol> parameters;

    public override TypeSymbol ReturnType => LazyInitializer.EnsureInitialized(ref this.returnType, this.BuildReturnType);
    private TypeSymbol? returnType;

    private FunctionSymbol ConstructorSymbol => LazyInitializer.EnsureInitialized(ref this.constructorSymbol, this.BuildConstructorSymbol);
    private FunctionSymbol? constructorSymbol;

    public override CodegenDelegate Codegen => (codegen, targetType, args) =>
    {
        var target = codegen.DefineRegister(targetType);
        var ctorSymbol = this.ConstructorSymbol;
        if (targetType.IsGenericInstance && this.IsGenericDefinition)
        {
            ctorSymbol = ctorSymbol.GenericInstantiate(targetType, ((IGenericInstanceSymbol)targetType).Context);
        }
        codegen.Write(NewObject(target, ctorSymbol, args));
        return target;
    };

    private GenericContext? Context
    {
        get
        {
            if (!this.contextNeedsBuild) return this.context;
            lock (this.contextBuildLock)
            {
                if (this.contextNeedsBuild)
                {
                    this.context = this.BuildContext();
                    // IMPORTANT: We write the flag last and here
                    this.contextNeedsBuild = false;
                }
                return this.context;
            }
        }
    }
    private GenericContext? context;
    // IMPORTANT: Flag is a bool and not computed because we can't atomically copy nullable structs
    private volatile bool contextNeedsBuild = true;
    private readonly object contextBuildLock = new();

    private TypeSymbol InstantiatedType => (TypeSymbol)ctorDefinition.ContainingSymbol!;

    private ImmutableArray<TypeParameterSymbol> BuildGenericParameters() => this.InstantiatedType.GenericParameters
        .Select(p => new SynthetizedTypeParameterSymbol(this, p.Name))
        .Cast<TypeParameterSymbol>()
        .ToImmutableArray();

    private ImmutableArray<ParameterSymbol> BuildParameters() => this.ConstructorSymbol.Parameters
        .Select(p => new SynthetizedParameterSymbol(this, p.Name, p.Type))
        .Cast<ParameterSymbol>()
        .ToImmutableArray();

    private TypeSymbol BuildReturnType() => this.Context is null
        ? this.InstantiatedType
        : this.InstantiatedType.GenericInstantiate(this.InstantiatedType.ContainingSymbol, this.Context.Value);

    private FunctionSymbol BuildConstructorSymbol() => this.Context is null
        ? ctorDefinition
        : ctorDefinition.GenericInstantiate(this.ReturnType, this.Context.Value);

    private GenericContext? BuildContext()
    {
        if (this.GenericParameters.Length == 0) return null;
        var substitutions = this.InstantiatedType.GenericParameters
            .Zip(this.GenericParameters)
            .ToImmutableDictionary(p => p.First, p => p.Second as TypeSymbol);
        return new(substitutions);
    }
}

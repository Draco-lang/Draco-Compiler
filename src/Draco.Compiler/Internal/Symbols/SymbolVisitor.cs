namespace Draco.Compiler.Internal.Symbols;

internal abstract class SymbolVisitor
{
    public virtual void VisitModule(ModuleSymbol moduleSymbol)
    {
        foreach (var member in moduleSymbol.AllMembers) member.Accept(this);
    }

    public virtual void VisitFunction(FunctionSymbol functionSymbol)
    {
        foreach (var genericParam in functionSymbol.GenericParameters) genericParam.Accept(this);
        foreach (var param in functionSymbol.Parameters) param.Accept(this);
    }

    public virtual void VisitType(TypeSymbol typeSymbol)
    {
        foreach (var genericParam in typeSymbol.GenericParameters) genericParam.Accept(this);
        foreach (var member in typeSymbol.AllMembers) member.Accept(this);
    }

    public virtual void VisitAlias(AliasSymbol aliasSymbol)
    {
        aliasSymbol.Substitution.Accept(this);
    }

    public virtual void VisitTypeParameter(TypeParameterSymbol typeParameterSymbol)
    {
    }

    public virtual void VisitParameter(ParameterSymbol parameterSymbol)
    {
    }

    public virtual void VisitField(FieldSymbol fieldSymbol)
    {
    }

    public virtual void VisitProperty(PropertySymbol propertySymbol)
    {
    }

    public virtual void VisitLocal(LocalSymbol localSymbol)
    {
    }

    public virtual void VisitLabel(LabelSymbol labelSymbol)
    {
    }
}

internal abstract class SymbolVisitor<TResult>
{
    public virtual TResult VisitModule(ModuleSymbol moduleSymbol)
    {
        foreach (var member in moduleSymbol.AllMembers) member.Accept(this);
        return default!;
    }

    public virtual TResult VisitFunction(FunctionSymbol functionSymbol)
    {
        foreach (var genericParam in functionSymbol.GenericParameters) genericParam.Accept(this);
        foreach (var param in functionSymbol.Parameters) param.Accept(this);
        return default!;
    }

    public virtual TResult VisitType(TypeSymbol typeSymbol)
    {
        foreach (var member in typeSymbol.AllMembers) member.Accept(this);
        return default!;
    }

    public virtual TResult VisitAlias(AliasSymbol aliasSymbol)
    {
        aliasSymbol.Substitution.Accept(this);
        return default!;
    }

    public virtual TResult VisitTypeParameter(TypeParameterSymbol typeParameterSymbol) => default!;

    public virtual TResult VisitParameter(ParameterSymbol parameterSymbol) => default!;

    public virtual TResult VisitField(FieldSymbol fieldSymbol) => default!;

    public virtual TResult VisitProperty(PropertySymbol propertySymbol) => default!;

    public virtual TResult VisitLocal(LocalSymbol localSymbol) => default!;

    public virtual TResult VisitLabel(LabelSymbol labelSymbol) => default!;
}

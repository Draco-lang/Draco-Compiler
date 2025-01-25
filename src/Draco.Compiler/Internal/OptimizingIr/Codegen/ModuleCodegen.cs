using System.Linq;
using Draco.Compiler.Api;
using Draco.Compiler.Internal.BoundTree;
using Draco.Compiler.Internal.Lowering;
using Draco.Compiler.Internal.OptimizingIr.Model;
using Draco.Compiler.Internal.Symbols;
using Draco.Compiler.Internal.Symbols.Source;
using Draco.Compiler.Internal.Symbols.Syntax;
using Draco.Compiler.Internal.Symbols.Synthetized.AutoProperty;
using static Draco.Compiler.Internal.OptimizingIr.InstructionFactory;

namespace Draco.Compiler.Internal.OptimizingIr.Codegen;

/// <summary>
/// Generates IR code on module-level.
/// </summary>
internal sealed class ModuleCodegen : SymbolVisitor
{
    /// <summary>
    /// The compilation the codegen generates for.
    /// </summary>
    public Compilation Compilation { get; }

    /// <summary>
    /// True, if sequence points should be emitted.
    /// </summary>
    public bool EmitSequencePoints { get; }

    private readonly LocalCodegen globalInitializer;
    private readonly Module module;

    public ModuleCodegen(Compilation compilation, Module module, bool emitSequencePoints)
    {
        this.Compilation = compilation;
        this.EmitSequencePoints = emitSequencePoints;
        this.module = module;
        this.globalInitializer = new(this.Compilation, module.GlobalInitializer);
    }

    private void Complete()
    {
        // Complete anything that needs completion
        // The global initializer for example is missing a return
        this.globalInitializer.Write(Ret(default(Void)));
    }

    public override void VisitField(FieldSymbol fieldSymbol)
    {
        if (fieldSymbol is not SyntaxFieldSymbol and not AutoPropertyBackingFieldSymbol) return;

        this.module.DefineField(fieldSymbol);

        if (fieldSymbol is not SourceFieldSymbol sourceGlobal) return;

        // If there's a value, compile it
        if (sourceGlobal.Value is not null)
        {
            var body = this.RewriteBody(sourceGlobal.Value);
            // Yank out potential local functions and closures
            var (bodyWithoutLocalFunctions, localFunctions) = ClosureRewriter.Rewrite(body);
            // Compile it
            var value = bodyWithoutLocalFunctions.Accept(this.globalInitializer);
            // Store it
            this.globalInitializer.WriteAssignment(sourceGlobal, value);

            // Compile the local functions
            foreach (var localFunc in localFunctions) this.VisitFunction(localFunc);
        }
    }

    public override void VisitProperty(PropertySymbol propertySymbol)
    {
        // TODO: Not flexible, won't work for non-auto props
        if (propertySymbol is not SyntaxAutoPropertySymbol) return;

        this.module.DefineProperty(propertySymbol);

        if (propertySymbol is not SourceAutoPropertySymbol sourceAutoProp) return;

        // If there's a value, compile it
        if (sourceAutoProp.Value is not null)
        {
            var body = this.RewriteBody(sourceAutoProp.Value);
            // Yank out potential local functions and closures
            var (bodyWithoutLocalFunctions, localFunctions) = ClosureRewriter.Rewrite(body);
            // Compile it
            var value = bodyWithoutLocalFunctions.Accept(this.globalInitializer);
            // Store it
            this.globalInitializer.WriteAssignment(sourceAutoProp.BackingField, value);

            // Compile the local functions
            foreach (var localFunc in localFunctions) this.VisitFunction(localFunc);
        }
    }

    public override void VisitFunction(FunctionSymbol functionSymbol)
    {
        if (functionSymbol.Body is null) return;

        // Add procedure
        var procedure = this.module.DefineProcedure(functionSymbol);

        // Create the body
        var body = this.RewriteBody(functionSymbol.Body);
        // Yank out potential local functions and closures
        var (bodyWithoutLocalFunctions, localFunctions) = ClosureRewriter.Rewrite(body);
        // Compile it
        var bodyCodegen = new LocalCodegen(this.Compilation, procedure);
        bodyWithoutLocalFunctions.Accept(bodyCodegen);

        // Compile the local functions
        foreach (var localFunc in localFunctions) this.VisitFunction(localFunc);
    }

    public override void VisitModule(ModuleSymbol moduleSymbol)
    {
        foreach (var subModuleSymbol in moduleSymbol.AllMembers.OfType<ModuleSymbol>())
        {
            var module = this.module.DefineModule(subModuleSymbol);
            var moduleCodegen = new ModuleCodegen(this.Compilation, module, this.EmitSequencePoints);
            subModuleSymbol.Accept(moduleCodegen);
        }

        foreach (var member in moduleSymbol.AllMembers.Where(x => x is not ModuleSymbol))
        {
            member.Accept(this);
        }

        this.Complete();
    }

    private BoundNode RewriteBody(BoundNode body)
    {
        // If needed, inject sequence points
        if (this.EmitSequencePoints) body = SequencePointInjector.Inject(body);
        // Desugar it
        return body.Accept(new LocalRewriter(this.Compilation));
    }
}

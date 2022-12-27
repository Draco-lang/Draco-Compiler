using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Internal.DracoIr;
using Type = Draco.Compiler.Internal.DracoIr.Type;

namespace Draco.Compiler.Internal.Codegen;

/// <summary>
/// Generates CIL from DracoIR.
/// </summary>
internal sealed class CilCodegen
{
    /// <summary>
    /// Generates a PE file from the given Draco assembly.
    /// </summary>
    /// <param name="assembly">The assembly to generate an executable from.</param>
    /// <param name="peStream">The stream to write the executable to.</param>
    public static void Generate(IReadOnlyAssembly assembly, Stream peStream)
    {
        var codegen = new CilCodegen(assembly);

        codegen.CreateModuleAndAssembly();
        codegen.Translate(assembly);
        codegen.CreateFreeFunctionsClass();
        codegen.GeneratePe(peStream);
    }

    private readonly IReadOnlyAssembly assembly;

    private readonly MetadataBuilder metadataBuilder = new();
    private readonly BlobBuilder ilBuilder = new();

    private readonly List<MethodDefinitionHandle> compiledFreeFunctions = new();

    // Context local to procedures
    private readonly Dictionary<IReadOnlyBasicBlock, LabelHandle> labelTranslations = new();
    private InstructionEncoder ilEncoder;

    private CilCodegen(IReadOnlyAssembly assembly)
    {
        this.assembly = assembly;
    }

    public void Translate(IReadOnlyAssembly asm)
    {
        foreach (var proc in asm.Procedures.Values) this.Translate(proc);
    }

    public void Translate(IReadOnlyProcecude proc)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature()
            .Parameters(
                parameterCount: 0,
                returnType: returnType => returnType.Void(),
                parameters: parameters => { });

        var methodBodyStream = new MethodBodyStreamEncoder(this.ilBuilder);
        var codeBuilder = new BlobBuilder();

        this.ilEncoder = new InstructionEncoder(codeBuilder, new ControlFlowBuilder());

        // "Forward-declare" labels
        foreach (var bb in proc.BasicBlocks)
        {
            this.labelTranslations.Add(bb, this.ilEncoder.DefineLabel());
        }

        // Actual codegen
        foreach (var bb in proc.BasicBlocks)
        {
            this.Translate(bb);
        }

        var offset = methodBodyStream.AddMethodBody(this.ilEncoder);
        var handle = this.metadataBuilder.AddMethodDefinition(
            attributes: MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            implAttributes: MethodImplAttributes.IL,
            name: this.metadataBuilder.GetOrAddString(proc.Name),
            signature: this.metadataBuilder.GetOrAddBlob(signature),
            bodyOffset: offset,
            parameterList: default);

        this.compiledFreeFunctions.Add(handle);
    }

    public void Translate(IReadOnlyBasicBlock block)
    {
        this.ilEncoder.MarkLabel(this.labelTranslations[block]);
        foreach (var instr in block.Instructions) this.Translate(instr);
    }

    public void Translate(IReadOnlyInstruction instr)
    {
        switch (instr.Kind)
        {
        case InstructionKind.Nop:
        {
            this.ilEncoder.OpCode(ILOpCode.Nop);
            break;
        }
        case InstructionKind.Ret:
        {
            var returnedValue = instr.GetOperandAt<Value>(0);
            this.TranslateToStackValue(returnedValue);
            this.ilEncoder.OpCode(ILOpCode.Ret);
            break;
        }
        default:
            throw new ArgumentOutOfRangeException(nameof(instr));
        }
    }

    private void TranslateToStackValue(Value value)
    {
        if (value.Type == Type.Unit) return;

        throw new NotImplementedException();
    }

    public void CreateModuleAndAssembly()
    {
        this.metadataBuilder.AddModule(
            generation: 0,
            moduleName: this.metadataBuilder.GetOrAddString(this.assembly.Name),
            // TODO: Proper module-version ID
            mvid: this.metadataBuilder.GetOrAddGuid(Guid.NewGuid()),
            // TODO: What are these? Encryption?
            encId: default,
            encBaseId: default);

        this.metadataBuilder.AddAssembly(
            name: this.metadataBuilder.GetOrAddString(this.assembly.Name),
            // TODO: Proper versioning
            version: new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: 0,
            hashAlgorithm: AssemblyHashAlgorithm.None);
    }

    public void CreateFreeFunctionsClass()
    {
        var mscorlibAssemblyRef = this.metadataBuilder.AddAssemblyReference(
            name: this.metadataBuilder.GetOrAddString("mscorlib"),
            // TODO: What version?
            version: new Version(4, 0, 0, 0),
            culture: default,
            // TODO: What the hell?
            publicKeyOrToken: this.metadataBuilder.GetOrAddBlob(new byte[]
            {
                0xB7, 0x7A, 0x5C, 0x56, 0x19, 0x34, 0xE0, 0x89,
            }),
            flags: default,
            hashValue: default);

        var systemObjectTypeRef = this.metadataBuilder.AddTypeReference(
            mscorlibAssemblyRef,
            this.metadataBuilder.GetOrAddString("System"),
            this.metadataBuilder.GetOrAddString("Object"));

        this.metadataBuilder.AddTypeDefinition(
            attributes: TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.AutoLayout | TypeAttributes.BeforeFieldInit | TypeAttributes.Abstract | TypeAttributes.Sealed,
            @namespace: default,
            name: this.metadataBuilder.GetOrAddString("FreeFunctions"),
            baseType: systemObjectTypeRef,
            // TODO: What's this?
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: this.compiledFreeFunctions.Count == 0
                ? MetadataTokens.MethodDefinitionHandle(1)
                : this.compiledFreeFunctions[0]);
    }

    public void GeneratePe(Stream peStream)
    {
        var peHeaderBuilder = new PEHeaderBuilder(
            imageCharacteristics: Characteristics.Dll);
        var peBuilder = new ManagedPEBuilder(
            header: peHeaderBuilder,
            metadataRootBuilder: new(this.metadataBuilder),
            ilStream: this.ilBuilder,
            // TODO: When entry point is exposed from assembly
            entryPoint: default,
            flags: CorFlags.ILOnly,
            // TODO: For deterministic builds
            deterministicIdProvider: null);

        var peBlob = new BlobBuilder();
        var contentId = peBuilder.Serialize(peBlob);
        peBlob.WriteContentTo(peStream);
    }
}

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Internal.Symbols;

/// <summary>
/// Represents a module with a single name, merged from multiple sources.
/// </summary>
internal sealed class MergedModuleSymbol(
    Symbol? containingSymbol,
    string name,
    ImmutableArray<ModuleSymbol> modules) : ModuleSymbol
{
    public override IEnumerable<Symbol> AllMembers =>
        InterlockedUtils.InitializeDefault(ref this.members, this.BuildMembers);
    private ImmutableArray<Symbol> members;

    public override Symbol? ContainingSymbol { get; } = containingSymbol;
    public override string Name { get; } = name;

    private ImmutableArray<Symbol> BuildMembers()
    {
        var members = ImmutableArray.CreateBuilder<Symbol>();
        var submodules = new List<ModuleSymbol>();
        foreach (var singleModule in modules)
        {
            // singleModule is a piece of this module, we need to go through each element of that
            foreach (var member in singleModule.AllMembers)
            {
                if (member is ModuleSymbol module)
                {
                    submodules.Add(module);
                }
                else
                {
                    members.Add(member);
                }
            }
        }
        // We need to merge submodules by name
        var submodulesGrouped = submodules.GroupBy(m => m.Name);
        // And add them as merged modules
        foreach (var group in submodulesGrouped)
        {
            var groupElements = group.ToImmutableArray();
            // For single-element groups we skip merging
            if (groupElements.Length == 1) members.Add(groupElements[0]);
            else members.Add(new MergedModuleSymbol(this, group.Key, groupElements));
        }
        return members.ToImmutable();
    }
}

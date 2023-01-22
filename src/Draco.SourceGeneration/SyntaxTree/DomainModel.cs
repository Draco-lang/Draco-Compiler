using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Draco.SourceGeneration.SyntaxTree;

public sealed class Tree
{
    public static Tree FromXml(XmlTree tree)
    {
        ValidateXml(tree);

        Node MakePredefinedNode(XmlPredefinedNode node) =>
            new(node.Name, GetBaseByName(node.Base), false, string.Empty);

        Node MakeAbstractNode(XmlAbstractNode node) =>
            new(node.Name, GetBaseByName(node.Base), true, node.Documentation.Trim());

        Node MakeNode(XmlNode node) =>
            new(node.Name, GetBaseByName(node.Base), false, node.Documentation.Trim());

        Node MakeNodeByName(string name)
        {
            var predefined = tree.PredefinedNodes.FirstOrDefault(n => n.Name == name);
            if (predefined is not null) return MakePredefinedNode(predefined);

            var @abstract = tree.AbstractNodes.FirstOrDefault(n => n.Name == name);
            if (@abstract is not null) return MakeAbstractNode(@abstract);

            var node = tree.Nodes.FirstOrDefault(n => n.Name == name);
            if (node is not null) return MakeNode(node);

            throw new KeyNotFoundException($"no node called {name} was found in the tree");
        }

        var nodes = new Dictionary<string, Node>();
        Node GetNodeByName(string name)
        {
            if (!nodes!.TryGetValue(name, out var node))
            {
                node = MakeNodeByName(name);
                nodes.Add(name, node);
            }
            return node;
        }
        Node? GetBaseByName(string? name) => name is null ? null : GetNodeByName(name);

        return new Tree(
            root: GetNodeByName(tree.Root),
            abstractNodes: tree.AbstractNodes.Select(n => GetNodeByName(n.Name)).ToList(),
            nodes: tree.Nodes.Select(n => GetNodeByName(n.Name)).ToList());
    }

    private static void ValidateXml(XmlTree tree)
    {
        // Unique node name validation
        var names = new HashSet<string>();
        void AddNodeName(string name)
        {
            if (!names!.Add(name)) throw new InvalidOperationException($"duplicate node named {name} in ther tree");
        }

        foreach (var predefined in tree.PredefinedNodes) AddNodeName(predefined.Name);
        foreach (var @abstract in tree.AbstractNodes) AddNodeName(@abstract.Name);
        foreach (var node in tree.Nodes) AddNodeName(node.Name);
    }

    public Node Root { get; }
    public IList<Node> AbstractNodes { get; }
    public IList<Node> Nodes { get; }

    public Tree(Node root, IList<Node> abstractNodes, IList<Node> nodes)
    {
        this.Root = root;
        this.AbstractNodes = abstractNodes;
        this.Nodes = nodes;
    }
}

public sealed class Node
{
    public string Name { get; }
    public Node? Base { get; }
    public bool IsAbstract { get; }
    public string Documentation { get; }
    public IList<Node> Derived { get; } = new List<Node>();

    public Node(string name, Node? @base, bool isAbstract, string documentation)
    {
        this.Name = name;
        this.Base = @base;
        this.IsAbstract = isAbstract;
        this.Documentation = documentation;

        @base?.Derived.Add(this);
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Draco.Compiler.Internal.Documentation;

/// <summary>
/// Represents documentation for a <see cref="Symbols.Symbol"/>.
/// </summary>
/// <param name="Sections">The <see cref="DocumentationSection"/>s this documentation contains.</param>
internal record class SymbolDocumentation(ImmutableArray<DocumentationSection> Sections)
{
    /// <summary>
    /// Empty documentation;
    /// </summary>
    public static SymbolDocumentation Empty = new SymbolDocumentation(ImmutableArray<DocumentationSection>.Empty);

    /// <summary>
    /// The summary documentation section.
    /// </summary>
    public DocumentationSection? Summary => this.Sections.FirstOrDefault(x => x.Name.ToLower() == "summary");

    /// <summary>
    /// Creates a markdown representation of this documentation.
    /// </summary>
    /// <returns>The documentation in markdown format.</returns>
    public virtual string ToMarkdown()
    {
        var builder = new StringBuilder();
        for (int i = 0; i < this.Sections.Length; i++)
        {
            var section = this.Sections[i];
            builder.Append(section switch
            {
                SummaryDocumentationSection => string.Join(string.Empty, section.Elements.Select(x => x.ToMarkdown())),

                ParametersDocumentationSection =>
                    $"""
                    # parameters
                    {string.Join(Environment.NewLine, section.Elements.Select(x => x.ToMarkdown()))}
                    """,

                TypeParametersDocumentationSection =>
                    $"""
                    # type parameters
                    {string.Join(Environment.NewLine, section.Elements.Select(x => x.ToMarkdown()))}
                    """,

                CodeDocumentationSection code => code.Code.ToMarkdown(),

                _ => $"""
                     # {section.Name.ToLowerInvariant()}
                     {string.Join(string.Empty, section.Elements.Select(x => x.ToMarkdown()))}
                     """
            });

            // Newline after each section except the last one
            if (i != this.Sections.Length - 1) builder.Append(Environment.NewLine);
        }
        return builder.ToString();
    }


    /// <summary>
    /// Creates an XML representation of this documentation.
    /// </summary>
    /// <returns>The documentation in XML format, encapsulated by a <c>documentation</c> tag.</returns>
    public virtual XElement ToXml()
    {
        var sections = new List<XNode>();
        foreach (var section in this.Sections)
        {
            switch (section)
            {
            case ParametersDocumentationSection:
            case TypeParametersDocumentationSection:
                sections.AddRange(section.Elements.Select(x => x.ToXml()));
                break;
            case CodeDocumentationSection code:
                sections.Add(code.Code.ToXml());
                break;
            default:
                sections.Add(new XElement(section.Name.ToLowerInvariant(), section.Elements.Select(x => x.ToXml())));
                break;
            }
        }
        return new XElement("documentation", sections);
    }
}

/// <summary>
/// Temporary structure for storing markdown documentation.
/// </summary>
/// <param name="Markdown">The markdown documentation.</param>
internal sealed record class MarkdownSymbolDocumentation(string Markdown) : SymbolDocumentation(ImmutableArray<DocumentationSection>.Empty)
{
    public override string ToMarkdown() => this.Markdown;

    public override XElement ToXml() => throw new NotSupportedException();
}

// TODO: Re-add this once we have proper markdown extractor
#if false
internal sealed record class FunctionDocumentation(ImmutableArray<DocumentationSection> Sections) : SymbolDocumentation(Sections)
{
    public DocumentationSection? Return => this.Sections.FirstOrDefault(x => x.Name.ToLower() == "return");
    public ParametersDocumentationSection? Parameters => this.Sections.OfType<ParametersDocumentationSection>().FirstOrDefault();
}
#endif

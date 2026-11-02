using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace Portfolio.Api;

public static class PortfolioIndex
{
    public const string DefaultName = "portfolio-knowledge";

    public static SearchIndex Create(string name) =>
        new(name) { Fields = new FieldBuilder().Build(typeof(KnowledgePassage)) };
}

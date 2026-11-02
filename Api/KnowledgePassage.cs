using System.Text.Json.Serialization;
using Azure.Search.Documents.Indexes;

namespace Portfolio.Api;

public sealed class KnowledgePassage
{
    [SimpleField(IsKey = true, IsFilterable = true)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [SearchableField(IsFilterable = true)]
    [JsonPropertyName("topic")]
    public string Topic { get; set; } = "";

    [SearchableField]
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [SearchableField]
    [JsonPropertyName("terms")]
    public string Terms { get; set; } = "";

    [SearchableField]
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

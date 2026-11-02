using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Portfolio.Api;

public sealed class AzureSearchPortfolioRetriever(
    IConfiguration configuration,
    PortfolioCatalog catalog,
    ILogger<AzureSearchPortfolioRetriever> logger) : IPortfolioRetriever
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SearchClient? _search;
    private bool _indexed;

    public bool IsConfigured =>
        HttpsEndpoint("AzureSearch:Endpoint") is not null &&
        ConfiguredSecret("AzureSearch:ApiKey") &&
        ValidIndex(configuration["AzureSearch:IndexName"] ?? PortfolioIndex.DefaultName);

    public async Task<RetrievalResult> RetrieveAsync(string question, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return new RetrievalResult(RetrievalStatus.Unavailable, []);

        var searchText = KnowledgeSearchText.Build(question);
        if (searchText is null)
            return new RetrievalResult(RetrievalStatus.Ok, []);

        try
        {
            var search = await EnsureIndexedAsync(cancellationToken);
            var response = await search.SearchAsync<KnowledgePassage>(searchText, new SearchOptions
            {
                Size = 4,
                QueryType = SearchQueryType.Simple,
                SearchMode = SearchMode.Any
            }, cancellationToken);
            var passages = new List<KnowledgePassage>();
            await foreach (var result in response.Value.GetResultsAsync())
            {
                passages.Add(result.Document);
            }

            return new RetrievalResult(RetrievalStatus.Ok, passages);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Portfolio search timed out.");
            return new RetrievalResult(RetrievalStatus.TimedOut, []);
        }
        catch (RequestFailedException exception)
        {
            logger.LogError("Portfolio search failed with status {Status}.", exception.Status);
            return new RetrievalResult(RetrievalStatus.Failed, []);
        }
    }

    private async Task<SearchClient> EnsureIndexedAsync(CancellationToken cancellationToken)
    {
        if (_indexed && _search is not null)
            return _search;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_indexed && _search is not null)
                return _search;

            var endpoint = HttpsEndpoint("AzureSearch:Endpoint")!;
            var indexName = configuration["AzureSearch:IndexName"] ?? PortfolioIndex.DefaultName;
            var credential = new AzureKeyCredential(configuration["AzureSearch:ApiKey"]!);
            var options = new SearchClientOptions();
            options.Retry.NetworkTimeout = TimeSpan.FromSeconds(15);
            options.Retry.MaxRetries = 1;

            var indexes = new SearchIndexClient(endpoint, credential, options);
            await indexes.CreateOrUpdateIndexAsync(PortfolioIndex.Create(indexName), cancellationToken: cancellationToken);
            var search = new SearchClient(endpoint, indexName, credential, options);
            await search.MergeOrUploadDocumentsAsync(catalog.Passages, cancellationToken: cancellationToken);
            logger.LogInformation("Portfolio knowledge index {IndexName} is ready with {Count} passages.", indexName, catalog.Passages.Count);
            _search = search;
            _indexed = true;
            return search;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Uri? HttpsEndpoint(string key)
    {
        var value = configuration[key];
        return Uri.TryCreate(value, UriKind.Absolute, out var endpoint) && endpoint.Scheme == Uri.UriSchemeHttps
            ? endpoint
            : null;
    }

    private bool ConfiguredSecret(string key)
    {
        var value = configuration[key];
        return !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl);
    }

    private static bool ValidIndex(string name) =>
        name.Length is >= 1 and <= 128 &&
        char.IsAsciiLetterLower(name[0]) &&
        name.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');
}

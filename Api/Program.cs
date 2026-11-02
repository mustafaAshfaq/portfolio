using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Portfolio.Api;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.Services.AddHttpClient(Options.DefaultName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<PortfolioCatalog>();
builder.Services.AddSingleton<IPortfolioRetriever, AzureSearchPortfolioRetriever>();
builder.Services.AddSingleton<IPortfolioChat, AzurePortfolioChat>();
builder.Build().Run();

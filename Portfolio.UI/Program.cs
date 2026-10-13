using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Portfolio.UI;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});

var host = builder.Build();

var gtmContainerId = host.Configuration["GoogleTagManager:ContainerId"];
if (!string.IsNullOrWhiteSpace(gtmContainerId))
{
    var js = host.Services.GetRequiredService<IJSRuntime>();
    await js.InvokeVoidAsync("portfolioGtm.load", gtmContainerId.Trim());
}

await host.RunAsync();

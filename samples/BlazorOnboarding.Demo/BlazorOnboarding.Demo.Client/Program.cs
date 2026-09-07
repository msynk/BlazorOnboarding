using BlazorOnboarding;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// The same registration works unchanged on WebAssembly and on the server.
builder.Services.AddBlazorOnboarding(options =>
{
    options.Labels.Skip = "Skip";
    options.StorageKeyPrefix = "blazor-onboarding-demo:";
});

await builder.Build().RunAsync();

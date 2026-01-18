using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using My.ConsoleApp;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<Ticker>();

await builder.Build().RunAsync().ConfigureAwait(false);
using DevDash;

var builder = WebApplication.CreateBuilder(args);

builder.UseDevDash(configuration =>
{
    configuration        
        .AddCompose("../compose.yaml", ComposeType.Podman)
        .AddDotNetApplication("Console-App-1", "../My.ConsoleApp")
        .AddDotNetApplication("Web-Api-1", "../My.WebApi", launchProfile: "http1")
        .AddDotNetApplication("Web-Api-2", "../My.WebApi", launchProfile: "http2");
});

builder
    .Build()
    .UseDevDash()
    .Run();
using DevDash;

var builder = WebApplication.CreateBuilder(args);

builder.UseDevDash(configuration =>
{
    configuration
        .SetConsoleOutputMaxLines(20)
        .AddCompose(0, "../compose.yaml", ComposeType.Podman)
        .AddDotNetApplication(0, "Console-App-1", "../My.ConsoleApp")
        .AddDotNetWebApplication(1, "Web-Api-1", "../My.WebApi", launchProfile: "http1")
        .AddDotNetWebApplication(2, "Web-Api-2", "../My.WebApi", launchProfile: "http2")
        .AddGenericProcess(new GenericProcessConfiguration
        {
            StartupOrder = 1,
            Id = "my-go-app",
            PathToFolder = "../my-go-app",
            FileName = "go",
            Args = ["run", "."],
            UrlDetections = [new(@"Server starting on port (\d+)", IsPortOnly: true, IsHttpsWhenPortOnly: false)]
        })
        .AddGenericProcess(new GenericProcessConfiguration {
            StartupOrder = 3,
            Id = "my-go-app-e2e",
            PathToFolder = "../my-go-app-e2e",
            FileName = "go",
            Args = ["test", "-v", "./..."],
            StartDetectionRegex = "=== RUN"
        })
        .AddGenericProcess(new GenericProcessConfiguration
        {
            StartupOrder = 1,
            Id = "my-node-app",
            PathToFolder = "../my-node-app",
            FileName = "npm",
            Args = ["i", "&&", "start"],
            UrlDetections = [new(@"Server starting on port (\d+)", IsPortOnly: true, IsHttpsWhenPortOnly: false)]
        })
        .AddGenericProcess(new GenericProcessConfiguration
        {
            StartupOrder = 3,
            Id = "my-node-app-e2e",
            PathToFolder = "../my-node-app-e2e",
            FileName = "npm",
            Args = ["test"],
            StartDetectionRegex = "> node --test"
        });
});

builder
    .Build()
    .UseDevDash()
    .Run();
using DevDash;

var builder = WebApplication.CreateBuilder(args);

builder.UseDevDash(configuration =>
{
    configuration
        .SetConsoleOutputMaxLines(50)
        .AddCompose(0, "../compose.yaml", ComposeType.Podman)
        .AddProcess(new ProcessConfiguration
        {
            StartupOrder = 0,
            Id = "console-app-1",
            PathToFolder = "../My.ConsoleApp",
            Instructions = "dotnet run --no-restore --no-build",
            StartDetectionRegex = "Application started"
        })
        .AddProcess(new ProcessConfiguration
        {
            StartupOrder = 1,
            Id = "web-api-1",
            PathToFolder = "../My.WebApi",
            Instructions = "dotnet run  --no-restore --no-build --launch-profile http1",
            UrlDetections = [new(@"Now listening on: (https?://\S+)", IsPortOnly: false, IsHttpsWhenPortOnly: false)]
        })
        .AddProcess(new ProcessConfiguration
        {
            StartupOrder = 2,
            Id = "web-api-2",
            PathToFolder = "../My.WebApi",
            Instructions = "dotnet run --no-restore --no-build --launch-profile http2",
            UrlDetections = [new(@"Now listening on: (https?://\S+)", IsPortOnly: false, IsHttpsWhenPortOnly: false)]
        })
        .AddProcess(new ProcessConfiguration
        {
            StartupOrder = 1,
            Id = "my-go-app",
            PathToFolder = "../my-go-app",
            Instructions = "go run .",
            UrlDetections = [new(@"Server starting on port (\d+)", IsPortOnly: true, IsHttpsWhenPortOnly: false)]
        })
        .AddProcess(new ProcessConfiguration
        {
            StartupOrder = 2,
            Id = "my-go-app-e2e",
            PathToFolder = "../my-go-app-e2e",
            Instructions = "go test -v ./...",
            StartDetectionRegex = "=== RUN"
        })
        .AddProcess(new ProcessConfiguration
        {
            StartupOrder = 1,
            Id = "my-node-app",
            PathToFolder = "../my-node-app",
            Instructions = "npm i && npm start",
            UrlDetections = [new(@"Server starting on port (\d+)", IsPortOnly: true, IsHttpsWhenPortOnly: false)]
        })
        .AddProcess(new ProcessConfiguration
        {
            StartupOrder = 2,
            Id = "my-node-app-e2e",
            PathToFolder = "../my-node-app-e2e",
            Instructions = "npm test",
            StartDetectionRegex = "> node --test"
        });
});

builder
    .Build()
    .UseDevDash()
    .Run();
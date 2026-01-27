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
            StartDetectionRegex = null,
            PreDefinedStartDetection = null,
            UrlDetections = [new(@"Server starting on port (\d+)", IsPortOnly: true, IsHttpsWhenPortOnly: false)],
            PreDefinedUrlDetections = null
        });
});

builder
    .Build()
    .UseDevDash()
    .Run();
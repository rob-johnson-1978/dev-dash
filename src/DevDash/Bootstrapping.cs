using Akka.Actor;
using Akka.Hosting;
using DevDash;
using DevDash.Features.Dashboard;
using DevDash.Features.Dashboard.Actors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace DevDash;

public static class Bootstrapping
{
    public static void RunDevDash(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.UseDevDash(args);

        var app = builder.Build();
        app.UseDevDash();

        app.Run();
    }

    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder UseDevDash(string[] args)
        {
            /* devdash - port configuration and static web assets */

            var port = 5285;
            var filePath = Path.GetRelativePath(Environment.CurrentDirectory, "dev-dash.yaml");

            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--ddsh-port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedPort))
                {
                    port = parsedPort;                    
                }

                if (args[i] == "--ddsh-file" && i + 1 < args.Length)
                {
                    filePath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, args[i + 1]));
                }
            }

            builder.WebHost.UseUrls($"http://localhost:{port}");

            var assembly = typeof(Bootstrapping).Assembly;
            var contentRoot = Path.GetDirectoryName(assembly.Location)!;
            builder.Environment.ContentRootPath = contentRoot;
            builder.Environment.WebRootPath = Path.Combine(contentRoot, "wwwroot");

            /* devdash */

            Console.WriteLine();
            Console.WriteLine($"Loading DevDash configuration from: {filePath}");
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine();

            var yaml = File.ReadAllText(filePath);
            Console.WriteLine(yaml);
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine();

            var deserializer = new DeserializerBuilder()
                .IncludeNonPublicProperties()
                .Build();

            try
            {
                var configuration = deserializer.Deserialize<Configuration>(yaml);
                builder.Services.AddSingleton(configuration);
            }
            catch (YamlException ex)
            {
                Console.WriteLine($"{ex.Message}");
                Console.WriteLine($"Inner exception: {ex.InnerException?.Message ?? "null"}");
                throw;
            }

            /* razor */

            builder.Services
                .AddRazorPages()
                .AddApplicationPart(typeof(Bootstrapping).Assembly)
                .AddRazorPagesOptions(options =>
                {
                    options.Conventions.AddAreaPageRoute("DevDash", "/Dashboard", "/");
                    options.Conventions.AddAreaPageRoute("DevDash", "/OtherPage", "/other-page");
                });

            /* akka */

            builder.Services.AddAkka("DevDashSystem", async (akkaBuilder, serviceProvider) =>
            {
                var devDashConfig = serviceProvider.GetRequiredService<Configuration>();

                akkaBuilder.ConfigureLoggers(loggerConfig =>
                {
                    loggerConfig.AddLoggerFactory();

                    loggerConfig.LogLevel = Akka.Event.LogLevel.DebugLevel;
                });

                akkaBuilder.WithActors((system, registry, _) =>
                {
                    var dashboardProps = Props.Create(() => new DashboardSupervisor(devDashConfig));
                    var dashboardActor = system.ActorOf(dashboardProps, "dashboard-supervisor");
                    registry.Register<DashboardSupervisor>(dashboardActor);
                });

                akkaBuilder.AddStartup(async (actorSystem, registry) =>
                {
                    var dashboardSupervisor = await registry.GetAsync<DashboardSupervisor>();
                    dashboardSupervisor.Tell(new ConfigureDashboard());
                });
            });

            return builder;
        }
    }

    extension(WebApplication app)
    {
        public WebApplication UseDevDash()
        {
            /* razor - serve embedded static files */

            var assembly = typeof(Bootstrapping).Assembly;
            var embeddedFileProvider = new ManifestEmbeddedFileProvider(assembly, "wwwroot");

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = embeddedFileProvider,
                RequestPath = "/_content/DevDash"
            });

            app.MapRazorPages();

            /* devdash */

            app.MapGet("/devdash/dashboard/event-stream", Endpoints.HandleEventStreamRequest);
            app.MapPost("/devdash/dashboard/{command}", Endpoints.HandleCommand);
            app.MapPost("/devdash/dashboard/process/{processId}/{command}", Endpoints.HandleProcessCommand);

            return app;
        }
    }
}
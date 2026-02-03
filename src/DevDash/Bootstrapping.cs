using Akka.Actor;
using Akka.Hosting;
using DevDash;
using DevDash.Features.Dashboard;
using DevDash.Features.Dashboard.Actors;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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
            /* devdash */

            Console.WriteLine();

            var filePath = Path.Combine(Environment.CurrentDirectory, "dev-dash.yaml");
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
            catch (YamlDotNet.Core.YamlException ex)
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
            /* razor */

            app.UseStaticFiles();
            app.MapRazorPages().WithStaticAssets();

            /* devdash */

            app.MapGet("/devdash/dashboard/event-stream", Endpoints.HandleEventStreamRequest);
            app.MapPost("/devdash/dashboard/{command}", Endpoints.HandleCommand);
            app.MapPost("/devdash/dashboard/process/{processId}/{command}", Endpoints.HandleProcessCommand);

            return app;
        }
    }
}
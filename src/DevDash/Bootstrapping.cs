using Akka.Actor;
using Akka.Hosting;
using DevDash.Features.Dashboard;
using DevDash.Features.Dashboard.Actors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevDash;

public static class Bootstrapping
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder UseDevDash(Action<DevDashConfiguration> configure)
        {
            /* devdash */

            var configuration = new DevDashConfiguration();
            configure(configuration);

            builder.Services.AddSingleton(configuration);

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
                var devDashConfig = serviceProvider.GetRequiredService<DevDashConfiguration>();

                akkaBuilder.ConfigureLoggers(loggerConfig =>
                {
                    loggerConfig.AddLoggerFactory();

                    loggerConfig.LogLevel = devDashConfig.LogLevel switch
                    {
                        LogLevel.Trace => Akka.Event.LogLevel.DebugLevel,
                        LogLevel.Debug => Akka.Event.LogLevel.DebugLevel,
                        LogLevel.Information => Akka.Event.LogLevel.InfoLevel,
                        LogLevel.Warning => Akka.Event.LogLevel.WarningLevel,
                        LogLevel.Error => Akka.Event.LogLevel.ErrorLevel,
                        LogLevel.Critical => Akka.Event.LogLevel.ErrorLevel,
                        _ => Akka.Event.LogLevel.InfoLevel,
                    };
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

            app.MapGet("/devdash/sse", Endpoints.HandleSseRequest);
            app.MapPost("/devdash/command/{command}/{applicationId}", Endpoints.HandleCommand);        
            
            return app;
        }
    }
}
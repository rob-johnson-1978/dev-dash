using Akka.Actor;
using Akka.Hosting;
using DevDash.Features.Dashboard;
using DevDash.Features.Dashboard.Actors;
using DevDash.Features.OpenTelemetry;
using DevDash.Features.OpenTelemetry.Actors;
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
                    options.Conventions.AddAreaPageRoute("DevDash", "/OpenTelemetry", "/open-telemetry");
                });

            /* env / kestrel / grpc for OpenTelemetry purposes */

            builder.Environment.EnvironmentName = Environments.Development;

            builder.WebHost
                .UseKestrelHttpsConfiguration()
                .UseKestrel(kestrel =>
                {
                    // HTTP/2 endpoint for gRPC (no TLS)
                    kestrel.ListenLocalhost(configuration.TelemetryPort, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);

                    // HTTPS endpoint for web UI (uses dev cert via UseKestrelHttpsConfiguration)
                    kestrel.ListenLocalhost(configuration.MainPort, o =>
                    {
                        o.UseHttps();
                        o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
                    });
                });

            builder.Services.AddGrpc();

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

                    var telemetryProps = Props.Create(() => new TelemetrySupervisor(devDashConfig));
                    var telemetryActor = system.ActorOf(telemetryProps, "telemetry-supervisor");
                    registry.Register<TelemetrySupervisor>(telemetryActor);
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

            /* otel */

            app.MapGrpcService<DevDashTraceService>();
            app.MapGrpcService<DevDashMetricsService>();
            app.MapGrpcService<DevDashLogsService>();

            /* devdash */

            app.MapGet("/devdash/sse", Endpoints.HandleSseRequest);
            app.MapPost("/devdash/command/{command}/{applicationId}", Endpoints.HandleCommand);        
            
            return app;
        }
    }
}
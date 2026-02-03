using Akka.Actor;
using Akka.Hosting;
using DevDash.Features.Dashboard.Actors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace DevDash.Features.Dashboard;

internal static class Endpoints
{
    internal static async Task<IResult> HandleEventStreamRequest(
        [FromServices] ActorSystem actorSystem,
        [FromServices] IHostApplicationLifetime applicationLifetime,
        [FromServices] IRequiredActor<DashboardSupervisor> dashboardSupervisorRequiredActor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<SseItem<string>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        var props = Props.Create(() => new DashboardEventStreamSubscriber(channel.Writer));
        var subscriber = actorSystem.ActorOf(props);

        actorSystem.EventStream.Subscribe(subscriber, typeof(DashboardEventRaised));

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            applicationLifetime.ApplicationStopping
        );

        applicationLifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                httpContext.Abort();
            }
            catch
            {
                // ignored
            }
        });

        dashboardSupervisorRequiredActor
            .ActorRef
            .Tell(new StartRunnableProcesses());

        return TypedResults.ServerSentEvents(YieldSseItems(channel, actorSystem, subscriber, linkedCts.Token));
    }

    internal static async Task<IResult> HandleCommand(
        [FromServices] IRequiredActor<DashboardSupervisor> dashboardSupervisorRequiredActor,
        [FromRoute] string command)
    {
        object message = command switch
        {
            "start-dashboard" => new StartDashboard(),
            "stop-dashboard" => new StopDashboard(),
            "restart-dashboard" => new RestartDashboard(),
            _ => throw new ArgumentException($"Unknown command: {command}")
        };

        dashboardSupervisorRequiredActor.ActorRef.Tell(message);

        return Results.Accepted();
    }

    internal static async Task<IResult> HandleProcessCommand(
        [FromServices] IRequiredActor<DashboardSupervisor> dashboardSupervisorRequiredActor,
        [FromRoute] string processId,
        [FromRoute] string command)
    {
        object message = command switch
        {
            "start-process" => new StartRunnableProcess(processId),
            "stop-process" => new StopRunnableProcess(processId),
            "restart-process" => new RestartRunnableProcess(processId),
            _ => throw new ArgumentException($"Unknown command: {command}")
        };

        dashboardSupervisorRequiredActor.ActorRef.Tell(message);

        return Results.Accepted();
    }

    private static async IAsyncEnumerable<SseItem<string>> YieldSseItems(
        Channel<SseItem<string>> channel,
        ActorSystem actorSystem,
        IActorRef subscriber,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }
        finally
        {
            actorSystem.EventStream.Unsubscribe(subscriber, typeof(DashboardEventRaised));
            actorSystem.Stop(subscriber);
            channel.Writer.TryComplete();
        }
    }
}
using Akka.Actor;
using Akka.Event;
using System.Net.ServerSentEvents;
using System.Threading.Channels;

namespace DevDash.Features.Dashboard.Actors;

internal class DashboardEventStreamSubscriber(ChannelWriter<SseItem<string>> channelWriter) : UntypedActor
{
    private readonly ILoggingAdapter _logger = Context.GetLogger();

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case DashboardEventRaised evt:
                {
                    var sseItem = new SseItem<string>(
                        data: evt.Json,
                        eventType: evt.Type
                    );

                    if (!channelWriter.TryWrite(sseItem))
                    {
                        _logger.Warning("Failed to write event to channel: {0}", evt);
                    }

                    break;
                }
            default:
                Unhandled(message);
                break;
        }
    }

    protected override void PostStop()
    {
        channelWriter.TryComplete();
        base.PostStop();
    }
}

using Akka.Actor;

namespace DevDash.Features.Dashboard.Actors;

internal class ComposeStatusProvider : UntypedActor
{
    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case WaitForComposeStatusToBecomeAvailable:
                {
                    // todo: kick off periodic checks of docker compose status, keep going until one of the below happens:
                        // once a successful status is retrieved, send a command to the parent actor    
                        // upon timeout, send a different command to the parent actor
                    break;
                }
            default:
                {
                    Unhandled(message);
                    break;
                }
        }        
    }
}

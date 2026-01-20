using Akka.Actor;
using Akka.Event;
using DevDash;
using DevDash.Infastructure;
using System.Collections.Immutable;

namespace DevDash.Features.Dashboard.Actors;

internal sealed class DashboardSupervisor(DevDashConfiguration configuration) : UntypedActor, IWithUnboundedStash, IWithTimers
{
    private const string UpdateTimerKey = "publish-runnable-application-timer-key";
    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private readonly DashboardSupervisorState _state = new();

    public IStash Stash { get; set; } = null!;

    public ITimerScheduler Timers { get; set; } = null!;

    protected override void PostStop()
    {
        Timers.CancelAll();

        if (configuration.OnShutdown is null)
        {
            return;
        }

        RunTask(async () =>
        {
            await configuration.OnShutdown();
        });
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case ConfigureDashboard:
                {
                    if (configuration.BeforeStart != null)
                    {
                        RunTask(async () => await configuration.BeforeStart());
                    }

                    if (configuration.ComposeConfiguration != null)
                    {
                        _state.RunnableApplications.Add(
                            Constants.DockerComposeApplicationId,
                            new RunnableApplicationWithActor(
                                configuration.ComposeConfiguration.StartupOrder,
                                Constants.DockerComposeApplicationId,
                                Running: false,
                                RunRequested: false,
                                [],
                                Context.ActorOf<DashboardProcessRunner>(Constants.DockerComposeApplicationId)
                            )
                        );
                    }

                    foreach (var applicationKeyValuePair in configuration.DotNetApplications)
                    {
                        _state.RunnableApplications.Add(
                            applicationKeyValuePair.Key,
                            new RunnableApplicationWithActor(
                                applicationKeyValuePair.Value.StartupOrder,
                                applicationKeyValuePair.Key,
                                Running: false,
                                RunRequested: false,
                                [],
                                Context.ActorOf<DashboardProcessRunner>(applicationKeyValuePair.Key)
                            )
                        );
                    }

                    // todo: more application types here

                    _state.CurrentGroupOfApplicationsToBeStarted = _state.RunnableApplications.Values.Min(x => x.StartupOrder);

                    _logger.Info("Dashboard configured with {0} runnable applications.", _state.RunnableApplications.Count);

                    Become(Configured);

                    Stash.UnstashAll();

                    break;
                }
            default:
                {
                    Stash.Stash();
                    break;
                }
        }
    }

    private void Configured(object message)
    {
        switch (message)
        {
            case StartRunnableApplications:
                {
                    _logger.Info("Starting runnable applications...");

                    RunTask(async () => await Task.Delay(2000));

                    Context.System.EventStream.Publish(DashboardEventRaised.Create(new RunnableApplicationsStarting()));

                    Become(StartingRunnableApplications);

                    Self.Tell(new CheckIfNextGroupOfRunnableApplicationsCanBeStarted());

                    /*
                     TODO:

                     Run the first "layer" of applications (0 or smallest)

                     Handle a new message "Application actually started" (will need to be different, per-type)

                     When this is received, add to a new collection of "started applications" in state

                     Also, when this is received, see if we can move to the next layer (or if there is one)

                     if there is a next layer, run those applications and repeat until all running

                     Should stash other commands until all layers are run through?
                     */



                    //if (
                    //    configuration.ComposeConfiguration != null &&
                    //    _state.RunnableApplications.TryGetValue(Constants.DockerComposeApplicationId, out var composeAppRunner)
                    //)
                    //{
                    //    composeAppRunner
                    //        .ActorRef?
                    //        .Tell(
                    //            new RunCompose(
                    //                configuration.ComposeConfiguration.FilePath, 
                    //                configuration.ComposeConfiguration.ComposeType
                    //            )
                    //        );
                    //}

                    //foreach (var applicationKeyValuePair in configuration.DotNetApplications)
                    //{
                    //    if (!_state.RunnableApplications.TryGetValue(applicationKeyValuePair.Key, out var dotNetApplicationRunner))
                    //    {
                    //        _logger.Warning("DotNet application runner not found for: {0}", applicationKeyValuePair.Key);
                    //        continue;
                    //    }

                    //    dotNetApplicationRunner.ActorRef.Tell(new RunDotNetApplication(applicationKeyValuePair.Value));
                    //} // todo: await "Ask" here instead, so the apps start sequentially, and dependencies can have a controlled start

                    //Timers.StartPeriodicTimer(
                    //    UpdateTimerKey,
                    //    new PublishUpdateForAllRunnableApplications(),
                    //    TimeSpan.FromSeconds(1),
                    //    TimeSpan.FromSeconds(1)
                    //);

                    //Become(ProcessesStartedForTheFirstTime);

                    //Stash.UnstashAll();

                    break;
                }
            case GetRunnableApplications:
                {
                    HandleGetRunnableApplications();
                    break;
                }
            default:
                {
                    Stash.Stash();
                    break;
                }
        }
    }

    private void StartingRunnableApplications(object message)
    {
        // may need to unstash some here because as the applications become started, they will send messages back
        // and the UI should reflect that

        switch (message)
        {
            case IShouldCheckIfNextGroupOfRunnableApplicationsCanBeStarted:
                {
                    if (message is RunnableApplicationStarted runnableApplicationStarted)
                    {
                        // todo: update state to mark application as started
                    }

                    // now do check to see if still waiting, or if can start next group
                    // if can start next group, get next group, update state next group number, and start those applications, then wait for their started messages

                    // if there is no next group, Become(RunnableApplicationsStartedForTheFirstTime) and unstash all

                    

                    break;
                }
            default:
                {
                    Stash.Stash();
                    break;
                }
        }
    }

    private void RunnableApplicationsStartedForTheFirstTime(object message)
    {
        switch (message)
        {
            case GetRunnableApplications:
                {
                    HandleGetRunnableApplications();
                    break;
                }
            case UpdateRunnableApplication command:
                {
                    if (!_state.RunnableApplications.TryGetValue(command.Application.Id, out var runnableApplication))
                    {
                        _logger.Warning("Received update for unknown application ID: {0}", command.Application.Id);
                        break;
                    }

                    var updatedApplication = runnableApplication with { Running = command.Application.Running, Urls = command.Application.Urls };
                    _state.RunnableApplications[command.Application.Id] = updatedApplication;

                    _logger.Info("Runnable application updated: {0}, Running: {1}, URLs: {2}", command.Application.Id, command.Application.Running, string.Join(", ", command.Application.Urls));

                    var @event = new RunnableApplicationUpdated(new RunnableApplication(updatedApplication.Id, updatedApplication.Running, updatedApplication.Urls));

                    Context.System.EventStream.Publish(DashboardEventRaised.Create(@event));

                    break;
                }
            case ICommmandRunnableApplicationsToChangeState command:
                {
                    if (!_state.RunnableApplications.TryGetValue(command.Id, out var runnableApplication))
                    {
                        _logger.Warning("Received stop request for unknown application ID: {0}", command.Id);
                        break;
                    }

                    runnableApplication.ActorRef.Forward(command);

                    break;
                }
            case PublishUpdateForAllRunnableApplications:
                {
                    foreach (var app in _state.RunnableApplications.Values)
                    {
                        Context.System.EventStream.Publish(
                            DashboardEventRaised.Create(
                                new RunnableApplicationUpdated(app, IsBackgroundUpdate: true)
                            )
                        );
                    }

                    break;
                }
            default:
                {
                    break;
                }
        }
    }

    private void HandleGetRunnableApplications()
    {
        Context.Sender.Tell(
            _state.RunnableApplications
                .Select(r => new RunnableApplication(r.Key, r.Value.Running, r.Value.Urls))
                .ToImmutableArray()
        );
    }
}
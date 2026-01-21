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
                                ApplicationType.Compose,
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
                                ApplicationType.DotNet,
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

                    _state.CurrentGroupOfApplicationsToBeStarted = _state.RunnableApplications.Values.Min(x => x.StartupOrder) - 1;

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

                    Timers.StartPeriodicTimer(
                        UpdateTimerKey,
                        new PublishUpdateForAllRunnableApplications(),
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(1)
                    );

                    Become(StartingRunnableApplications);

                    Self.Tell(new CheckIfNextGroupOfRunnableApplicationsCanBeStarted());

                    Stash.UnstashAll();

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
        switch (message)
        {
            case GetRunnableApplications:
                {
                    HandleGetRunnableApplications();
                    break;
                }
            case UpdateRunnableApplication command:
                {
                    HandleUpdateRunnableApplication(command);
                    break;
                }
            case PublishUpdateForAllRunnableApplications:
                {
                    HandlePublishUpdateForAllRunnableApplications();
                    break;
                }
            case IShouldCheckIfNextGroupOfRunnableApplicationsCanBeStarted:
                {
                    if (
                        message is RunnableApplicationStarted runnableApplicationStarted &&
                        _state.RunnableApplications.TryGetValue(runnableApplicationStarted.Id, out var runnableApplication)
                    )
                    {
                        _state.RunnableApplications[runnableApplicationStarted.Id] = runnableApplication with
                        {
                            RunRequested = false
                        };
                    }

                    var currentlyWaitingForProcessMessages = _state
                        .RunnableApplications
                        .Values
                        .Any(x => x.StartupOrder == _state.CurrentGroupOfApplicationsToBeStarted && x.RunRequested);

                    if(currentlyWaitingForProcessMessages)
                    {
                        _logger.Info("Waiting for more applications in the current group (Order: {0}) to start...", _state.CurrentGroupOfApplicationsToBeStarted);
                        break;
                    }

                    var moreGroupsToRun = _state
                        .RunnableApplications
                        .Values
                        .Any(x => x.StartupOrder > _state.CurrentGroupOfApplicationsToBeStarted);

                    if (!moreGroupsToRun)
                    {
                        _logger.Info("All runnable applications have been started.");

                        Become(RunnableApplicationsStartedForTheFirstTime);

                        Stash.UnstashAll();

                        break;
                    }

                    var nextGroupNumber = _state.CurrentGroupOfApplicationsToBeStarted = _state
                        .RunnableApplications
                        .Values
                        .Where(x => x.StartupOrder > _state.CurrentGroupOfApplicationsToBeStarted)
                        .Min(x => x.StartupOrder);

                    _logger.Info("Starting next group of runnable applications (Order: {0})...", nextGroupNumber);

                    var applicationsToStart = _state
                        .RunnableApplications
                        .Values
                        .Where(x => x.StartupOrder == nextGroupNumber);

                    foreach (var application in applicationsToStart)
                    {
                        _state.RunnableApplications[application.Id] = application with
                        {
                            RunRequested = true
                        };

                        switch (application.Type)
                        {
                            case ApplicationType.Compose:
                                {
                                    application
                                        .ActorRef?
                                        .Tell(
                                            new RunCompose(
                                                configuration.ComposeConfiguration!.FilePath,
                                                configuration.ComposeConfiguration!.ComposeType
                                            )
                                        );

                                    break;
                                }
                            case ApplicationType.DotNet:
                                {
                                    application
                                        .ActorRef?
                                        .Tell(
                                            new RunDotNetApplication(
                                                configuration.DotNetApplications[application.Id]
                                            )
                                        );

                                    break;
                                }
                            default:
                                throw new NotImplementedException();
                        }
                    }

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
                    HandleUpdateRunnableApplication(command);
                    break;
                }
            case ICommmandRunnableApplicationsToChangeState command:
                {

                    // todo: add "RunRequested" to state going back to UI - if true, then disable all controls!!!

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
                    HandlePublishUpdateForAllRunnableApplications();
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
                .Select(r => new RunnableApplication(r.Key, r.Value.Running, r.Value.RunRequested, r.Value.Urls))
                .ToImmutableArray()
        );
    }

    private void HandleUpdateRunnableApplication(UpdateRunnableApplication command)
    {
        if (!_state.RunnableApplications.TryGetValue(command.Application.Id, out var runnableApplication))
        {
            _logger.Warning("Received update for unknown application ID: {0}", command.Application.Id);
            return;
        }

        var updatedApplication = runnableApplication with
        {
            Running = command.Application.Running,
            Urls = command.Application.Urls
        };

        _state.RunnableApplications[command.Application.Id] = updatedApplication;

        _logger.Info(
            "Runnable application updated: {0}, Running: {1}, URLs: {2}", 
            command.Application.Id, 
            command.Application.Running, 
            string.Join(", ", command.Application.Urls)
        );

        var @event = new RunnableApplicationUpdated(
            new RunnableApplication(
                updatedApplication.Id,
                updatedApplication.Running,
                updatedApplication.RunRequested,
                updatedApplication.Urls
            )
        );

        Context.System.EventStream.Publish(DashboardEventRaised.Create(@event));
    }

    private void HandlePublishUpdateForAllRunnableApplications()
    {
        foreach (var app in _state.RunnableApplications.Values)
        {
            Context.System.EventStream.Publish(
                DashboardEventRaised.Create(
                    new RunnableApplicationUpdated(app, IsBackgroundUpdate: true)
                )
            );
        }
    }
}
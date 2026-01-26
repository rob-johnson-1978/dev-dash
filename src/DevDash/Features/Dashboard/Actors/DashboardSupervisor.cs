using Akka.Actor;
using Akka.Event;
using DevDash;
using DevDash.Infastructure;
using Google.Protobuf.WellKnownTypes;
using System.Collections.Immutable;

namespace DevDash.Features.Dashboard.Actors;

internal sealed class DashboardSupervisor(DevDashConfiguration configuration) : UntypedActor, IWithUnboundedStash, IWithTimers
{
    private const string UpdateTimerKey = "publish-runnable-application-timer-key";
    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private DashboardSupervisorState _state = new();

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

                    Timers.StartPeriodicTimer(
                            UpdateTimerKey,
                            new PublishDashboardUpdate(),
                            TimeSpan.Zero,
                            TimeSpan.FromMilliseconds(500)
                        );

                    if (configuration.ComposeConfiguration != null)
                    {
                        _state.RunnableApplications.Add(
                            Constants.DockerComposeApplicationId,
                            new RunnableApplicationWithActor(
                                ApplicationType.Compose,
                                configuration.ComposeConfiguration.StartupOrder,
                                Constants.DockerComposeApplicationId,
                                RunStatus.NeverStarted,
                                [],
                                Context.ActorOf<DashboardProcessRunner>(BuildProcessActorName(Constants.DockerComposeApplicationId))
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
                                RunStatus.NeverStarted,
                                [],
                                Context.ActorOf<DashboardProcessRunner>(BuildProcessActorName(applicationKeyValuePair.Key))
                            )
                        );
                    }

                    // todo: more application types here

                    _state.CurrentGroupOfApplicationsToBeStarted = _state.RunnableApplications.Values.Min(x => x.StartupOrder) - 1;

                    _logger.Info("Dashboard configured with {0} runnable applications.", _state.RunnableApplications.Count);

                    _state.RunStatus = RunStatus.Stopped;

                    Become(Configured);

                    Stash.UnstashAll();

                    break;
                }
            case PublishDashboardUpdate:
                {
                    HandlePublishDashboardUpdate();
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
                    _state.RunStatus = RunStatus.Started;

                    Context
                        .System
                        .EventStream
                        .Publish(DashboardEventRaised.Create(
                            new MessageAreaMessagePublished("Starting applications"))
                        );

                    _logger.Info("Starting runnable applications...");

                    HandlePublishUpdateForAllRunnableApplications();

                    RunTask(async () => await Task.Delay(2000));

                    Context.System.EventStream.Publish(DashboardEventRaised.Create(new RunnableApplicationsStarting()));

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
            case StartDashboard:
                {
                    HandleStartDashboardCommand();
                    break;
                }
            case PublishDashboardUpdate:
                {
                    HandlePublishDashboardUpdate();
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
            case StopDashboard:
                {
                    HandleStopDashboardCommand();
                    break;
                }
            case RestartDashboard:
                {
                    HandleRestartDashboardCommand();
                    break;
                }
            case PublishDashboardUpdate:
                {
                    HandlePublishDashboardUpdate();
                    break;
                }
            case IShouldCheckIfNextGroupOfRunnableApplicationsCanBeStarted:
                {
                    var currentlyWaitingForStartedMessages = _state
                        .RunnableApplications
                        .Values
                        .Any(x => x.StartupOrder == _state.CurrentGroupOfApplicationsToBeStarted && x.RunStatus == RunStatus.StartRequested);

                    if (currentlyWaitingForStartedMessages)
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

                        Timers.StartPeriodicTimer(
                            UpdateTimerKey,
                            new PublishUpdateForAllRunnableApplications(),
                            TimeSpan.Zero,
                            TimeSpan.FromMilliseconds(500)
                        );

                        Context
                            .System
                            .EventStream
                            .Publish(DashboardEventRaised.Create(
                                new MessageAreaMessagePublished("All applications started", "success"))
                            );

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
                        switch (application.Type)
                        {
                            case ApplicationType.Compose:
                                {
                                    application
                                        .ActorRef?
                                        .Tell(
                                            new RunCompose(
                                                configuration.ComposeConfiguration!.FilePath,
                                                configuration.ComposeConfiguration!.ComposeType,
                                                configuration.ComposeConfiguration!.CheckTimeoutInSeconds
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
            case StopDashboard:
                {
                    HandleStopDashboardCommand();
                    break;
                }
            case RestartDashboard:
                {
                    HandleRestartDashboardCommand();
                    break;
                }
            case PublishDashboardUpdate:
                {
                    HandlePublishDashboardUpdate();
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
                .Select(r => new RunnableApplication(r.Key, r.Value.RunStatus, r.Value.Urls))
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
            RunStatus = command.Application.RunStatus,
            Urls = command.Application.Urls
        };

        _state.RunnableApplications[command.Application.Id] = updatedApplication;

        _logger.Info(
            "Runnable application updated: {0}, RunStatus: {1}, URLs: {2}",
            command.Application.Id,
            command.Application.RunStatus,
            string.Join(", ", command.Application.Urls)
        );
    }

    private void HandlePublishDashboardUpdate()
    {
        Context.System.EventStream.Publish(
            DashboardEventRaised.Create(
                new DashboardStatusPublished(_state.RunStatus)
            )
        );
    }

    private void HandlePublishUpdateForAllRunnableApplications()
    {
        foreach (var app in _state.RunnableApplications.Values)
        {
            Context.System.EventStream.Publish(
                DashboardEventRaised.Create(
                    new RunnableApplicationStatusPublished(app.Id, app.RunStatus, app.Urls)
                )
            );
        }
    }

    private void HandleStartDashboardCommand()
    {
        Context.System.EventStream.Publish(
            DashboardEventRaised.Create(
                new MessageAreaMessagePublished("Starting dashboard..."))
            );

        _state.RunStatus = RunStatus.NeverStarted; // this disables all buttons then subsequent handling changes it

        _logger.Info("Starting dashboard after UI command...");

        Self.Tell(new StartRunnableApplications());
    }

    private void HandleStopDashboardCommand()
    {
        Context.System.EventStream.Publish(
            DashboardEventRaised.Create(
                new MessageAreaMessagePublished("Stopping dashboard..."))
            );

        _state.RunStatus = RunStatus.NeverStarted; // this disables all buttons then subsequent handling changes it

        _logger.Info("Stopping dashboard after UI command...");

        ReconfigureAll();
    }

    private void HandleRestartDashboardCommand()
    {
        Context.System.EventStream.Publish(
            DashboardEventRaised.Create(
                new MessageAreaMessagePublished("Restarting dashboard..."))
            );

        _state.RunStatus = RunStatus.NeverStarted; // this disables all buttons then subsequent handling changes it

        _logger.Info("Restarting dashboard after UI command...");

        ReconfigureAll();

        Self.Tell(new StartRunnableApplications());
    }

    private void ReconfigureAll()
    {
        RunTask(async () =>
        {
            foreach (var app in _state.RunnableApplications.Values)
            {
                await app.ActorRef.GracefulStop(TimeSpan.FromSeconds(5));
            }
        });

        Timers.CancelAll();
        Stash.ClearStash();

        _state = new();

        Become(OnReceive);

        Self.Tell(new ConfigureDashboard());
    }

    private static string BuildProcessActorName(string applicationId)
    {
        return $"dashboard-process-runner-{applicationId}-{Guid.NewGuid()}";
    }
}
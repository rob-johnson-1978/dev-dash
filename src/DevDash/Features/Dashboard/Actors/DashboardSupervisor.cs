using Akka.Actor;
using Akka.Event;
using DevDash;
using DevDash.Infastructure;
using System.Collections.Immutable;

namespace DevDash.Features.Dashboard.Actors;

internal sealed class DashboardSupervisor(Configuration configuration) : UntypedActor, IWithUnboundedStash, IWithTimers
{
    private const string UpdateTimerKey = "publish-runnable-process-timer-key";
    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private DashboardSupervisorState _state = new();

    public IStash Stash { get; set; } = null!;

    public ITimerScheduler Timers { get; set; } = null!;

    protected override void PostStop()
    {
        Timers.CancelAll();
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case ConfigureDashboard:
                {
                    Timers.StartPeriodicTimer(
                            UpdateTimerKey,
                            new PublishDashboardUpdate(),
                            TimeSpan.Zero,
                            TimeSpan.FromMilliseconds(500)
                        );

                    if (configuration.Compose != null)
                    {
                        _state.RunnableProcesses.Add(
                            Constants.DockerComposeProcessId,
                            new RunnableProcessWithActor(
                                ProcessType.Compose,
                                int.MinValue + 1,
                                Constants.DockerComposeProcessId,
                                RunStatus.NeverStarted,
                                [],
                                Context.ActorOf<DashboardProcessRunner>(BuildProcessActorName(Constants.DockerComposeProcessId))
                            )
                        );
                    }

                    foreach(var process in configuration.Processes)
                    {
                        _state.RunnableProcesses.Add(
                            process.Key,
                            new RunnableProcessWithActor(
                                ProcessType.Generic,
                                process.Value.StartupOrder,
                                process.Key,
                                RunStatus.NeverStarted,
                                [],
                                Context.ActorOf<DashboardProcessRunner>(BuildProcessActorName(process.Key))
                            )
                        );
                    }

                    _state.CurrentGroupOfProcessesToBeStarted = _state.RunnableProcesses.Values.Min(x => x.StartupOrder) - 1;

                    var runnableProcessesLog = string.Join(
                        ", ",
                        _state.RunnableProcesses.Values.Select(rp => $"{rp.Id} (Type: {rp.Type}, Order: {rp.StartupOrder})")
                    );

                    _logger.Info(
                        "Dashboard configured with {0} runnable processes: {1}",
                        _state.RunnableProcesses.Count, 
                        runnableProcessesLog
                    );

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
            case StartRunnableProcesses:
                {
                    _state.RunStatus = RunStatus.Started;

                    Context
                        .System
                        .EventStream
                        .Publish(DashboardEventRaised.Create(
                            new MessageAreaMessagePublished("Starting processes"))
                        );

                    _logger.Info("Starting runnable processes...");

                    HandlePublishUpdateForAllRunnableProcesses();

                    RunTask(async () => await Task.Delay(2000));

                    Context.System.EventStream.Publish(DashboardEventRaised.Create(new RunnableProcessesStarting()));

                    Become(StartingRunnableProcesses);

                    Self.Tell(new CheckIfNextGroupOfRunnableProcessesCanBeStarted());

                    Stash.UnstashAll();

                    break;
                }
            case GetRunnableProcesses:
                {
                    HandleGetRunnableProcesses();
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

    private void StartingRunnableProcesses(object message)
    {
        switch (message)
        {
            case GetRunnableProcesses:
                {
                    HandleGetRunnableProcesses();
                    break;
                }
            case UpdateRunnableProcess command:
                {
                    HandleUpdateRunnableProcess(command);
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
            case IShouldCheckIfNextGroupOfRunnableProcessesCanBeStarted:
                {
                    var currentlyWaitingForStartedMessages = _state
                        .RunnableProcesses
                        .Values
                        .Any(x => x.StartupOrder == _state.CurrentGroupOfProcessesToBeStarted && x.RunStatus == RunStatus.StartRequested);

                    if (currentlyWaitingForStartedMessages)
                    {
                        _logger.Info("Waiting for more processes in the current group (Order: {0}) to start...", _state.CurrentGroupOfProcessesToBeStarted);
                        break;
                    }

                    var moreGroupsToRun = _state
                        .RunnableProcesses
                        .Values
                        .Any(x => x.StartupOrder > _state.CurrentGroupOfProcessesToBeStarted);

                    if (!moreGroupsToRun)
                    {
                        _logger.Info("All runnable processes have been started.");

                        Become(RunnableProcessesStartedForTheFirstTime);

                        Stash.UnstashAll();

                        Timers.StartPeriodicTimer(
                            UpdateTimerKey,
                            new PublishUpdateForAllRunnableProcesses(),
                            TimeSpan.Zero,
                            TimeSpan.FromMilliseconds(500)
                        );

                        Context
                            .System
                            .EventStream
                            .Publish(DashboardEventRaised.Create(
                                new MessageAreaMessagePublished("All processes started", "success"))
                            );

                        break;
                    }

                    var nextGroupNumber = _state.CurrentGroupOfProcessesToBeStarted = _state
                        .RunnableProcesses
                        .Values
                        .Where(x => x.StartupOrder > _state.CurrentGroupOfProcessesToBeStarted)
                        .Min(x => x.StartupOrder);

                    _logger.Info("Starting next group of runnable processes (Order: {0})...", nextGroupNumber);

                    var processesToStart = _state
                        .RunnableProcesses
                        .Values
                        .Where(x => x.StartupOrder == nextGroupNumber);

                    foreach (var proc in processesToStart)
                    {
                        switch (proc.Type)
                        {
                            case ProcessType.Compose:
                                {
                                    proc
                                        .ActorRef?
                                        .Tell(
                                            new RunCompose(
                                                configuration.Compose!.Path,
                                                configuration.Compose!.Type,
                                                configuration.Compose!.CheckTimeoutSeconds
                                            )
                                        );

                                    break;
                                }
                            case ProcessType.Generic:
                                {
                                    proc
                                        .ActorRef?
                                        .Tell(
                                            new RunGenericProcess(
                                                proc.Id,
                                                configuration.Processes[proc.Id]
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

    private void RunnableProcessesStartedForTheFirstTime(object message)
    {
        switch (message)
        {
            case GetRunnableProcesses:
                {
                    HandleGetRunnableProcesses();
                    break;
                }
            case UpdateRunnableProcess command:
                {
                    HandleUpdateRunnableProcess(command);
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
            case ICommmandRunnableProcessesToChangeState command:
                {
                    if (!_state.RunnableProcesses.TryGetValue(command.Id, out var runnableProcess))
                    {
                        _logger.Warning("Received stop request for unknown process ID: {0}", command.Id);
                        break;
                    }

                    runnableProcess.ActorRef.Forward(command);

                    break;
                }
            case PublishUpdateForAllRunnableProcesses:
                {
                    HandlePublishUpdateForAllRunnableProcesses();
                    break;
                }
            default:
                {
                    break;
                }
        }
    }

    private void HandleGetRunnableProcesses()
    {
        Context.Sender.Tell(
            _state.RunnableProcesses
                .Select(r => new RunnableProcess(r.Key, r.Value.RunStatus, r.Value.Urls))
                .ToImmutableArray()
        );
    }

    private void HandleUpdateRunnableProcess(UpdateRunnableProcess command)
    {
        if (!_state.RunnableProcesses.TryGetValue(command.Process.Id, out var runnableProcess))
        {
            _logger.Warning("Received update for unknown process ID: {0}", command.Process.Id);
            return;
        }

        var updatedProcess = runnableProcess with
        {
            RunStatus = command.Process.RunStatus,
            Urls = command.Process.Urls
        };

        _state.RunnableProcesses[command.Process.Id] = updatedProcess;

        _logger.Info(
            "Runnable process updated: {0}, RunStatus: {1}, URLs: {2}",
            command.Process.Id,
            command.Process.RunStatus,
            string.Join(", ", command.Process.Urls)
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

    private void HandlePublishUpdateForAllRunnableProcesses()
    {
        foreach (var proc in _state.RunnableProcesses.Values)
        {
            Context.System.EventStream.Publish(
                DashboardEventRaised.Create(
                    new RunnableProcessStatusPublished(proc.Id, proc.RunStatus, proc.Urls)
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

        Self.Tell(new StartRunnableProcesses());
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

        Self.Tell(new StartRunnableProcesses());
    }

    private void ReconfigureAll()
    {
        RunTask(async () =>
        {
            foreach (var proc in _state.RunnableProcesses.Values)
            {
                await proc.ActorRef.GracefulStop(TimeSpan.FromSeconds(5));
            }
        });

        Timers.CancelAll();
        Stash.ClearStash();

        _state = new();

        Become(OnReceive);

        Self.Tell(new ConfigureDashboard());
    }

    private static string BuildProcessActorName(string processId)
    {
        return $"dashboard-process-runner-{processId}-{Guid.NewGuid()}";
    }
}
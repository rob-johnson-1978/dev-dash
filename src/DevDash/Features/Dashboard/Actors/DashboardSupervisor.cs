using Akka.Actor;
using Akka.Event;
using DevDash;
using System.Collections.Immutable;

namespace DevDash.Features.Dashboard.Actors;

internal sealed class DashboardSupervisor(DevDashConfiguration configuration) : UntypedActor, IWithUnboundedStash, IWithTimers
{
    private const string UpdateTimerKey = "publish-runnable-application-timer-key";
    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private readonly Dictionary<string, RunnableApplicationWithActor> _runnableApplications = [];

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

                    if (configuration.HasCompose)
                    {
                        _runnableApplications.Add(
                            Constants.DockerComposeApplicationId,
                            new RunnableApplicationWithActor(
                                Constants.DockerComposeApplicationId,
                                Running: false,
                                [],
                                Context.ActorOf<DashboardProcessRunner>(Constants.DockerComposeApplicationId)
                            )
                        );
                    }

                    foreach (var applicationKeyValuePair in configuration.DotNetApplications)
                    {
                        _runnableApplications.Add(
                            applicationKeyValuePair.Key,
                            new RunnableApplicationWithActor(
                                applicationKeyValuePair.Key,
                                Running: false,
                                [],
                                Context.ActorOf<DashboardProcessRunner>(applicationKeyValuePair.Key)
                            )
                        );
                    }

                    _logger.Info("Dashboard configured with {0} runnable applications.", _runnableApplications.Count);

                    Become(Configured);

                    Stash.UnstashAll();

                    break;
                }
            case StartRunnableApplications:
            case GetRunnableApplications:
            case RunnableApplicationUpdated:
                {
                    Stash.Stash();
                    break;
                }
            default:
                {
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

                    Context.System.EventStream.Publish(DashboardEventRaised.Create(new RunnableApplicationsStarted()));

                    if (_runnableApplications.TryGetValue(Constants.DockerComposeApplicationId, out var composeAppRunner))
                    {
                        composeAppRunner
                            .ActorRef?
                            .Tell(new RunCompose(configuration.ComposeFilePath, configuration.ComposeType));
                    }

                    foreach (var applicationKeyValuePair in configuration.DotNetApplications)
                    {
                        if (!_runnableApplications.TryGetValue(applicationKeyValuePair.Key, out var dotNetApplicationRunner))
                        {
                            _logger.Warning("DotNet application runner not found for: {0}", applicationKeyValuePair.Key);
                            continue;
                        }

                        dotNetApplicationRunner.ActorRef.Tell(new RunDotNetApplication(applicationKeyValuePair.Value));
                    } // todo: await "Ask" here instead, so the apps start sequentially, and dependencies can have a controlled start

                    Timers.StartPeriodicTimer(
                        UpdateTimerKey,
                        new PublishUpdateForAllRunnableApplications(),
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(1)
                    );

                    Become(ProcessesStartedForTheFirstTime);

                    Stash.UnstashAll();

                    break;
                }
            case GetRunnableApplications:
                {
                    HandleGetRunnableApplications();
                    break;
                }
            case ICommmandRunnableApplicationsToChangeState:
            case UpdateRunnableApplication:
            case PublishUpdateForAllRunnableApplications:
                {
                    Stash.Stash();
                    break;
                }
            default:
                {
                    break;
                }
        }
    }

    private void ProcessesStartedForTheFirstTime(object message)
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
                    if (!_runnableApplications.TryGetValue(command.Application.Id, out var runnableApplication))
                    {
                        _logger.Warning("Received update for unknown application ID: {0}", command.Application.Id);
                        break;
                    }

                    var updatedApplication = runnableApplication with { Running = command.Application.Running, Urls = command.Application.Urls };
                    _runnableApplications[command.Application.Id] = updatedApplication;

                    _logger.Info("Runnable application updated: {0}, Running: {1}, URLs: {2}", command.Application.Id, command.Application.Running, string.Join(", ", command.Application.Urls));

                    var @event = new RunnableApplicationUpdated(new RunnableApplication(updatedApplication.Id, updatedApplication.Running, updatedApplication.Urls));

                    Context.System.EventStream.Publish(DashboardEventRaised.Create(@event));

                    break;
                }
            case ICommmandRunnableApplicationsToChangeState command:
                {
                    if (!_runnableApplications.TryGetValue(command.Id, out var runnableApplication))
                    {
                        _logger.Warning("Received stop request for unknown application ID: {0}", command.Id);
                        break;
                    }

                    runnableApplication.ActorRef.Forward(command);

                    break;
                }
            case PublishUpdateForAllRunnableApplications:
                {
                    foreach (var app in _runnableApplications.Values)
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
            _runnableApplications
                .Select(r => new RunnableApplication(r.Key, r.Value.Running, r.Value.Urls))
                .ToImmutableArray()
        );
    }
}
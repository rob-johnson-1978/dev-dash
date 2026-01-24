using Akka.Actor;
using Akka.Event;
using DevDash.Infastructure;
using Google.Protobuf.WellKnownTypes;

namespace DevDash.Features.Dashboard.Actors;

internal partial class DashboardProcessRunner : UntypedActor, IWithUnboundedStash
{
    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private readonly DashboardProcessRunnerState _state = new();

    /* workflow */

    public IStash Stash { get; set; } = null!;

    protected override void PostStop()
    {
        EnsureProcessIsStopped();
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case RunDotNetApplication command:
                {
                    _state.RunStatus = RunStatus.StartRequested;
                    _state.ApplicationId = command.Application.Id;
                    _state.WorkingDirectory = command.Application.WorkingDirectoryPath;
                    _state.FileName = "dotnet";
                    _state.Args = command.Application.LaunchProfile == null
                        ? ["run"]
                        : ["run", "-lp", command.Application.LaunchProfile];

                    if (command.Application.StartDetectionPattern != null)
                    {
                        _state.DetectStartedViaStdOut =
                            line => line.Contains(command.Application.StartDetectionPattern, StringComparison.OrdinalIgnoreCase);
                    }

                    if (command.Application.LaunchProfile != null)
                    {
                        _state.FindUrlViaStdOut = line =>
                        {
                            var match = FindUrlInDotNetMessagePattern().Match(line);
                            if (match.Success && match.Groups.Count > 1)
                            {
                                return match.Groups[1].Value;
                            }

                            return null;
                        };
                    }

                    HandleStart();

                    break;
                }
            case RunCompose command:
                {
                    _state.RunStatus = RunStatus.StartRequested;

                    _state.ApplicationId = Constants.DockerComposeApplicationId;

                    _state.FullComposePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), command.ComposeFilePath));

                    _state.WorkingDirectory = Path.GetDirectoryName(_state.FullComposePath)
                        ?? throw new InvalidOperationException("Could not determine working directory");

                    _state.FileName = command.ComposeType switch
                    {
                        ComposeType.Docker => "docker",
                        ComposeType.Podman => "podman",
                        _ => throw new NotImplementedException()
                    };

                    _state.Args = ["compose", "-f", _state.FullComposePath, "up", "--build", "--force-recreate"];

                    _state.OnStarted = () =>
                    {
                        var composeStatusProvider = Context.ActorOf<ComposeStatusProvider>();

                        composeStatusProvider.Tell(
                            new WaitForComposeStatusToBecomeAvailable(
                                _state.WorkingDirectory,
                                _state.FullComposePath,
                                command.ComposeType,
                                command.CheckTimeoutInSeconds
                            )
                        );
                    };

                    HandleStart();

                    break;
                }
            case ICommmandRunnableApplicationsToChangeState:
            case ProcessExited:
            case ApplicationUrlDetected:
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

    private void Started(object message)
    {
        switch (message)
        {
            case ICommmandRunnableApplicationsToChangeState command:
                {
                    if (_state.Process == null)
                    {
                        _logger.Warning("Received {0} command but no dashboard process is available.", nameof(ICommmandRunnableApplicationsToChangeState));
                        break;
                    }

                    switch (command)
                    {
                        case StopRunnableApplication:
                            {
                                HandleStop();

                                break;
                            }
                        case RestartRunnableApplication:
                            {
                                Become(WaitingToStopBeforeRestart);

                                Self.Tell(new StopRunnableApplication(_state.ApplicationId));

                                break;
                            }
                        default:
                            {
                                break;
                            }
                    }

                    break;
                }
            case ProcessExited:
                {
                    HandleProcessExited();
                    break;
                }
            case ApplicationUrlDetected @event:
                {
                    var lowerUrl = @event.Url.ToLower();

                    if (_state.Urls.Contains(lowerUrl))
                    {
                        break;
                    }

                    _state.Urls.Add(lowerUrl);

                    ProcessApplicationStarted(Context.Parent, _state);

                    SendUpdateStateCommandToParent();

                    break;
                }
            case ComposeStarted:
                {
                    _logger.Info("Compose started, sending {0} to parent", nameof(RunnableApplicationStarted));

                    Context.Parent.Tell(new RunnableApplicationStarted(_state.ApplicationId));

                    break;
                }
            case ComposeStartFailed:
                {
                    PublishActionLogMessage("Compose failed to start, or start could not be detected. Please fix issues and restart the application.");
                    break;
                }
            default:
                {
                    break;
                }
        }
    }

    private void WaitingToStopBeforeRestart(object message)
    {
        switch (message)
        {
            case StopRunnableApplication:
                {
                    HandleStop();

                    Self.Tell(new StartRunnableApplication(_state.ApplicationId));

                    break;
                }
            default:
                {
                    break;
                }
        }
    }

    private void Stopped(object messsage)
    {
        switch (messsage)
        {
            case ICommmandRunnableApplicationsToChangeState command:
                {
                    if (_state.Process == null)
                    {
                        _logger.Warning("Received {0} command but no dashboard process is available.", nameof(ICommmandRunnableApplicationsToChangeState));
                        break;
                    }

                    switch (command)
                    {
                        case StartRunnableApplication:
                            {
                                HandleStart();
                                break;
                            }
                        default:
                            {
                                break;
                            }
                    }

                    break;
                }
            case ProcessExited:
                {
                    HandleProcessExited();
                    break;
                }
            default:
                {
                    break;
                }
        }
    }

    /* re-usable message handlers */

    private void HandleStart()
    {
        _state.RunStatus = RunStatus.StartRequested; // todo: make this immutable so that it can only be done via SendUpdateStateCommandToParent

        SendUpdateStateCommandToParent();

        PublishActionLogMessage("Starting process");

        _state.Process = CreateProcess();

        RunTask(async () => await StartProcess(_state.Process));

        _state.OnStarted();

        PublishActionLogMessage("Process started. Waiting for output...");

        Become(Started);

        Stash.UnstashAll();
    }

    private void HandleStop()
    {
        _state.ManuallyStopped = true;
        _state.Urls.Clear();

        PublishActionLogMessage("Stopping process");

        EnsureProcessIsStopped();

        PublishActionLogMessage("Process stopped");

        _state.RunStatus = RunStatus.Stopped;
        _state.Urls.Clear();

        SendUpdateStateCommandToParent();

        Become(Stopped);

        Stash.UnstashAll();
    }

    private void HandleProcessExited()
    {
        if (_state.ManuallyStopped)
        {
            _state.ManuallyStopped = false;
            return;
        }

        _state.ManuallyStopped = false;
        _state.RunStatus = RunStatus.Stopped;
        _state.Urls.Clear();

        SendUpdateStateCommandToParent();

        Become(Stopped);

        Stash.UnstashAll();
    }
}
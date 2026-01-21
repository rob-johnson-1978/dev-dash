using Akka.Actor;
using Akka.Event;
using DevDash.Infastructure;

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
                    _state.RunRequested = true;
                    _state.ApplicationId = command.Application.Id;
                    _state.WorkingDirectory = command.Application.WorkingDirectoryPath;
                    _state.FileName = "dotnet";
                    _state.Args = command.Application.LaunchProfile == null
                        ? ["run"]
                        : ["run", "-lp", command.Application.LaunchProfile];

                    _state.DetectStartedViaStdOut = LogsIndicateDotNetAppHasStarted;

                    _state.FindUrlInMessageViaStdOut = line =>
                    {
                        // Look for lines like:
                        // Now listening on: https://localhost:5001
                        var match = FindUrlInDotNetMessagePattern().Match(line);
                        if (match.Success && match.Groups.Count > 1)
                        {
                            return match.Groups[1].Value;
                        }

                        return null;
                    };                    

                    HandleStart();

                    break;
                }
            case RunCompose command:
                {
                    _state.RunRequested = true;

                    _state.ApplicationId = Constants.DockerComposeApplicationId;

                    var fullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), command.ComposeFilePath));

                    _state.WorkingDirectory = Path.GetDirectoryName(fullPath)
                        ?? throw new InvalidOperationException("Could not determine working directory");

                    _state.FileName = command.ComposeType switch
                    {
                        ComposeType.Docker => "docker",
                        ComposeType.Podman => "podman",
                        _ => throw new NotImplementedException()
                    };

                    _state.Args = ["compose", "-f", fullPath, "up", "--build", "--force-recreate"];

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

                    SendUpdateStateCommandToParent();

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
        PublishActionLogMessage("Starting process");

        _state.Process = CreateProcess();

        RunTask(async () => await StartProcess(_state.Process));

        _state.Running = true;

        PublishActionLogMessage("Process started. Waiting for output...");

        SendUpdateStateCommandToParent();

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

        _state.Running = false;

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
        _state.Running = false;        

        SendUpdateStateCommandToParent();

        Become(Stopped);

        Stash.UnstashAll();
    }    
}
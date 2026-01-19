using Akka.Actor;
using Akka.Event;
using DevDash.Infastructure;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DevDash.Features.Dashboard.Actors;

internal partial class DashboardProcessRunner : UntypedActor, IWithUnboundedStash
{
    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private string _applicationId = string.Empty;
    private string _workingDirectory = string.Empty;
    private string _fileName = string.Empty;
    private string[] _args = [];
    private Process? _process;
    private bool _manuallyStopped = false;
    private Func<string, string?>? _findUrlInMessage;
    private readonly HashSet<string> _urls = [];
    private bool _running = false;

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
                    _applicationId = command.Application.Id;
                    _workingDirectory = command.Application.WorkingDirectoryPath;
                    _fileName = "dotnet";
                    _args = command.Application.LaunchProfile == null
                        ? ["run"]
                        : ["run", "-lp", command.Application.LaunchProfile];

                    _findUrlInMessage = line =>
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
                    _applicationId = Constants.DockerComposeApplicationId;

                    var fullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), command.ComposeFilePath));

                    _workingDirectory = Path.GetDirectoryName(fullPath)
                        ?? throw new InvalidOperationException("Could not determine working directory");

                    _fileName = command.ComposeType switch
                    {
                        ComposeType.Docker => "docker",
                        ComposeType.Podman => "podman",
                        _ => throw new NotImplementedException()
                    };

                    _args = ["compose", "-f", fullPath, "up", "--build", "--force-recreate"];

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
                    if (_process == null)
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

                                Self.Tell(new StopRunnableApplication(_applicationId));

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

                    if (_urls.Contains(lowerUrl))
                    {
                        break;
                    }

                    _urls.Add(lowerUrl);

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

                    Self.Tell(new StartRunnableApplication(_applicationId));

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
                    if (_process == null)
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

        _process = CreateProcess();

        RunTask(async () => await StartProcess(_process));

        _running = true;

        PublishActionLogMessage("Process started. Waiting for output...");

        SendUpdateStateCommandToParent();

        Become(Started);

        Stash.UnstashAll();
    }

    private void HandleStop()
    {
        _manuallyStopped = true;
        _urls.Clear();

        PublishActionLogMessage("Stopping process");

        EnsureProcessIsStopped();

        PublishActionLogMessage("Process stopped");

        _running = false;

        SendUpdateStateCommandToParent();

        Become(Stopped);

        Stash.UnstashAll();
    }

    private void HandleProcessExited()
    {
        if (_manuallyStopped)
        {
            _manuallyStopped = false;
            return;
        }

        _manuallyStopped = false;
        _running = false;        

        SendUpdateStateCommandToParent();

        Become(Stopped);

        Stash.UnstashAll();
    }    
}
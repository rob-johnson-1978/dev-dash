using Akka.Actor;
using Akka.Event;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

    /* helpers */

    private Process CreateProcess()
    {
        var actorSystem = Context.System;
        var self = Self;

        var processStartInfo = new ProcessStartInfo
        {
            FileName = _fileName,
            Arguments = string.Join(" ", _args),
            WorkingDirectory = _workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = new Process
        {
            StartInfo = processStartInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            var line = StripAnsiCodes(e.Data);

            actorSystem.EventStream.Publish(
                DashboardEventRaised.Create(new ApplicationOutputLineEmitted(_applicationId, line))
            );

            if (_findUrlInMessage == null)
            {
                return;
            }

            var url = _findUrlInMessage.Invoke(line);

            if (url != null && !string.IsNullOrWhiteSpace(url))
            {
                self.Tell(new ApplicationUrlDetected(url));
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            var line = StripAnsiCodes(e.Data);

            actorSystem.EventStream.Publish(
                DashboardEventRaised.Create(new ApplicationErrorOutputLineEmitted(_applicationId, line))
            );
        };

        process.Exited += (sender, e) =>
        {
            self.Tell(new ProcessExited());
        };

        return process;
    }

    private static async Task StartProcess(Process process)
    {
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private void SendUpdateStateCommandToParent()
    {
        Context.Parent.Tell(new UpdateRunnableApplication(
            new RunnableApplication(_applicationId, _running, [.._urls])
        ));
    }

    private void PublishActionLogMessage(string action)
    {
        Context.System.EventStream.Publish(
            DashboardEventRaised.Create(
                new ApplicationOutputLineEmitted(_applicationId, $"---- [DEVDASH: {action}] ----")
            )
        );
    }

    private void EnsureProcessIsStopped()
    {
        if (_process == null)
        {
            return;
        }

        if (_process.HasExited)
        {
            try
            {
                _process.Dispose();
                return;
            }
            catch
            {
            }
        }

        try
        {
            // Kill the entire process tree
            KillProcessTree(_process.Id);
        }
        catch
        {
            // Fallback to simple kill if tree kill fails
            try
            {
                _process.Kill();
            }
            catch
            {
                // Process might have already exited
            }
        }
    }

    private static void KillProcessTree(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);

            // In .NET 5+, Kill() has an optional parameter to kill the entire tree
            process.Kill(entireProcessTree: true);
            process.WaitForExit(1000);
        }
        catch (ArgumentException)
        {
            // Process already exited
        }
        catch (PlatformNotSupportedException)
        {
            // Fall back to platform-specific approach
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                KillProcessTreeWindowsCmd(processId);
            }
            else
            {
                KillProcessTreeUnix(processId);
            }
        }
    }

    private static void KillProcessTreeWindowsCmd(int processId)
    {
        // Use taskkill command to kill process tree
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c taskkill /PID {processId} /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var killProcess = Process.Start(startInfo);
            killProcess?.WaitForExit(1000);
        }
        catch
        {
            // Fallback to just killing the main process
            try
            {
                var process = Process.GetProcessById(processId);
                process.Kill();
            }
            catch (ArgumentException)
            {
                // Process already exited
            }
        }
    }

    private static void KillProcessTreeUnix(int processId)
    {
        // On Unix, we can use process groups or pkill
        try
        {
            // Try to kill the process group
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"kill -TERM -{processId}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var killProcess = Process.Start(startInfo);
            killProcess?.WaitForExit(1000);
        }
        catch
        {
            // Fallback to killing just the process
            try
            {
                var process = Process.GetProcessById(processId);
                process.Kill();
            }
            catch (ArgumentException)
            {
                // Process already exited
            }
        }
    }

    private static string StripAnsiCodes(string line) =>
        AnsiCodePattern()
            .Replace(line, string.Empty)
            .TrimStart();

    [GeneratedRegex(@"\x1B\[[0-9;]*[mK]")]
    private static partial Regex AnsiCodePattern();

    [GeneratedRegex(@"Now listening on:\s+(https?://\S+)", RegexOptions.IgnoreCase, "en-GB")]
    private static partial Regex FindUrlInDotNetMessagePattern();
}
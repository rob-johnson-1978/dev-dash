using Akka.Actor;
using Akka.Event;
using DevDash.Infastructure;
using Google.Protobuf.WellKnownTypes;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DevDash.Features.Dashboard.Actors;

internal partial class DashboardProcessRunner : UntypedActor, IWithUnboundedStash
{
    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private readonly DashboardProcessRunnerState _state = new();

    /* workflow */

    public IStash Stash { get; set; } = null!;

    protected override void PostStop()
    {
        EnsureProcessIsStopped(_state, _logger);
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case RunGenericProcess command:
                {
                    _state.ApplicationId = command.Configuration.Id;
                    _state.WorkingDirectory = command.Configuration.PathToFolder;
                    _state.Instructions = command.Configuration.Instructions;

                    if (command.Configuration.StartDetectionRegex != null)
                    {
                        _state.DetectRunnableApplicationStartedViaStdOut =
                            DetectStartByRegex(new Regex(command.Configuration.StartDetectionRegex));
                    }

                    if (command.Configuration.PreDefinedStartDetection != null)
                    {
                        _state.DetectRunnableApplicationStartedViaStdOut =
                            DetectStartByRegex(GetPreDefinedRegex(command.Configuration.PreDefinedStartDetection));
                    }

                    if (command.Configuration.UrlDetections.Length > 0)
                    {
                        _state.DetectRunnableApplicationStartedUrlViaStdOut =
                            DetectUrlByRegex(
                                [.. command
                                    .Configuration
                                    .UrlDetections
                                    .Select(d => new UrlDetectionWithRegex(new Regex(d.RegexPattern), d.IsPortOnly, d.IsHttpsWhenPortOnly))
                                ]
                            );
                    }

                    if (command.Configuration.PreDefinedUrlDetections != null)
                    {
                        _state.DetectRunnableApplicationStartedUrlViaStdOut =
                            DetectUrlByRegex(GetPreDefinedUrlDetections(command.Configuration.PreDefinedUrlDetections));
                    }

                    HandleProcessStart();
                    break;
                }
            case RunDotNetApplication command:
                {
                    var instructions = command.Application.LaunchProfile == null
                        ? "dotnet run"
                        : $"dotnet run -lp {command.Application.LaunchProfile}";

                    _state.ApplicationId = command.Application.Id;
                    _state.WorkingDirectory = command.Application.WorkingDirectoryPath;
                    _state.Instructions = instructions;

                    if (command.Application.StartDetectionPattern != null)
                    {
                        _state.DetectRunnableApplicationStartedViaStdOut =
                            line => line.Contains(command.Application.StartDetectionPattern, StringComparison.OrdinalIgnoreCase);
                    }

                    if (command.Application.LaunchProfile != null)
                    {
                        _state.DetectRunnableApplicationStartedUrlViaStdOut = line =>
                        {
                            var match = FindUrlInDotNetMessagePattern().Match(line);
                            if (match.Success && match.Groups.Count > 1)
                            {
                                return match.Groups[1].Value;
                            }

                            return null;
                        };
                    }

                    HandleProcessStart();
                    break;
                }
            case RunCompose command:
                {
                    _state.ApplicationId = Constants.DockerComposeApplicationId;

                    _state.FullComposePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), command.ComposeFilePath));

                    _state.WorkingDirectory = Path.GetDirectoryName(_state.FullComposePath)
                        ?? throw new InvalidOperationException("Could not determine working directory");

                    var fileName = command.ComposeType switch
                    {
                        ComposeType.Docker => "docker",
                        ComposeType.Podman => "podman",
                        _ => throw new NotImplementedException()
                    };

                    _state.Instructions = $"{fileName} compose -f {_state.FullComposePath} up --build --force-recreate";

                    _state.DetectRunnableApplicationStartedAfterProcessStarted = () =>
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

                    HandleProcessStart();
                    break;
                }
            default:
                {
                    Stash.Stash();
                    break;
                }
        }
    }

    private void ProcessStarted(object message)
    {
        switch (message)
        {
            case ICommmandRunnableApplicationsToChangeState command:
                {
                    if (_state.Process == null || _state.RunStatus != RunStatus.Started)
                    {
                        break;
                    }

                    switch (command)
                    {
                        case StopRunnableApplication:
                            {
                                HandleProcessStop();
                                break;
                            }
                        case RestartRunnableApplication:
                            {
                                Become(WaitingForProcessToStopBeforeRestart);

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

                    if (_state.DetectRunnableApplicationStartedUrlViaStdOut != null)
                    {
                        SetAsRunnableApplicationStarted(Context.System, Context.Parent, _state);
                    }

                    break;
                }
            case ComposeStarted:
                {
                    SetAsRunnableApplicationStarted(Context.System, Context.Parent, _state);
                    break;
                }
            case ComposeStartFailed:
                {
                    PublishApplicationLogMessage(
                        Context.System,
                        _state,
                        "Compose failed to start, or start could not be detected. Please fix issues and restart the application."
                    );

                    break;
                }
            default:
                {
                    break;
                }
        }
    }

    private void WaitingForProcessToStopBeforeRestart(object message)
    {
        switch (message)
        {
            case StopRunnableApplication:
                {
                    HandleProcessStop();

                    Self.Tell(new StartRunnableApplication(_state.ApplicationId));

                    break;
                }
            default:
                {
                    break;
                }
        }
    }

    private void ProcessStopped(object messsage)
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
                                HandleProcessStart();
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

    private void HandleProcessStart()
    {
        UpdateRunStatusAndTellParent(RunStatus.StartRequested, Context.Parent, _state);

        PublishApplicationLogMessage(
            Context.System,
            _state,
            "Starting process"
        );

        _state.Process = CreateProcess(Context.System, Self, Context.Parent, _state);

        _state.DetectRunnableApplicationStartedAfterProcessStarted?.Invoke();

        RunTask(async () => await StartProcess(_state.Process));

        PublishApplicationLogMessage(
            Context.System,
            _state,
            "Process started. Waiting for output..."
        );

        Become(ProcessStarted);

        Stash.UnstashAll();
    }

    private void HandleProcessStop()
    {
        _state.Urls.Clear();

        PublishApplicationLogMessage(
            Context.System,
            _state,
            "Stopping process"
        );

        EnsureProcessIsStopped(_state, _logger);

        PublishApplicationLogMessage(
            Context.System,
            _state,
            "Process stopped"
        );

        _state.Urls.Clear();

        UpdateRunStatusAndTellParent(RunStatus.Stopped, Context.Parent, _state);

        Become(ProcessStopped);

        Stash.UnstashAll();
    }

    private void HandleProcessExited()
    {
        _state.Urls.Clear();

        UpdateRunStatusAndTellParent(RunStatus.Stopped, Context.Parent, _state);

        Become(ProcessStopped);

        Stash.UnstashAll();
    }

    /* helpers */

    private static (string fileName, string args) BuildFileNameAndArgs(string instructions)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("cmd.exe", $"/c {instructions}");
        }

        return ("/bin/sh", $"-c {instructions}");
    }

    private static Process CreateProcess(ActorSystem system, IActorRef self, IActorRef parent, DashboardProcessRunnerState state)
    {
        var (fileName, args) = BuildFileNameAndArgs(state.Instructions);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = state.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment =
            {
                // Force .NET apps to emit ANSI codes even when stdout is redirected
                ["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "true",
                // For older .NET Core versions
                ["DOTNET_ConsoleColors"] = "true",
                // For Node.js/npm tools
                ["FORCE_COLOR"] = "1",
                // For many CLI tools
                ["CLICOLOR_FORCE"] = "1"
            }
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

            if (state.DetectRunnableApplicationStartedUrlViaStdOut != null)
            {
                var url = state.DetectRunnableApplicationStartedUrlViaStdOut(e.Data);

                if (url != null && !string.IsNullOrWhiteSpace(url))
                {
                    self.Tell(new ApplicationUrlDetected(url));
                }
            }
            else if (state.DetectRunnableApplicationStartedViaStdOut != null)
            {
                var started = state.DetectRunnableApplicationStartedViaStdOut(e.Data);

                if (started)
                {
                    SetAsRunnableApplicationStarted(system, parent, state);
                }
            }
            else
            {
                SetAsRunnableApplicationStarted(system, parent, state);
            }

            var htmlFormattedLine = BuildHtmlFromOutput(e.Data);

            system.EventStream.Publish(
                DashboardEventRaised.Create(new ApplicationOutputLineEmitted(state.ApplicationId, htmlFormattedLine))
            );
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            var line = BuildHtmlFromOutput(e.Data);

            system.EventStream.Publish(
                DashboardEventRaised.Create(new ApplicationErrorOutputLineEmitted(state.ApplicationId, line))
            );
        };

        process.Exited += (sender, e) =>
        {
            self.Tell(new ProcessExited());
        };

        return process;
    }

    private static (string fileName, string args) BuildFileNameAndArgs(object instructions)
    {
        throw new NotImplementedException();
    }

    private static async Task StartProcess(Process process)
    {
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private static void SetAsRunnableApplicationStarted(ActorSystem system, IActorRef parent, DashboardProcessRunnerState state)
    {
        if (state.RunStatus == RunStatus.Started)
        {
            return;
        }

        UpdateRunStatusAndTellParent(RunStatus.Started, parent, state);

        PublishApplicationLogMessage(
            system,
            state,
            "Application started successfully."
        );

        parent.Tell(new RunnableApplicationStarted(state.ApplicationId));
    }

    private static void UpdateRunStatusAndTellParent(RunStatus runStatus, IActorRef parent, DashboardProcessRunnerState state)
    {
        state.RunStatus = runStatus;

        parent.Tell(new UpdateRunnableApplication(
            new RunnableApplication(state.ApplicationId, state.RunStatus, [.. state.Urls])
        ));
    }

    private static void PublishApplicationLogMessage(ActorSystem system, DashboardProcessRunnerState state, string message)
    {
        var typeName = typeof(DashboardProcessRunner).FullName ?? nameof(DashboardProcessRunner);
        var formattedMessage = $"<span class=\"ansi-devdash\">ddsh</span>: {typeName}[0]{Environment.NewLine}      {System.Net.WebUtility.HtmlEncode(message)}";

        system.EventStream.Publish(
            DashboardEventRaised.Create(
                new ApplicationOutputLineEmitted(state.ApplicationId, formattedMessage)
            )
        );
    }

    private static void EnsureProcessIsStopped(DashboardProcessRunnerState state, ILoggingAdapter logger)
    {
        if (state.Process == null)
        {
            return;
        }

        if (state.Process.HasExited)
        {
            state.Process.Dispose();
            return;
        }

        try
        {
            KillProcessTree(state.Process.Id);

            if (!state.Process.WaitForExit(2000))
            {
                state.Process.Kill(entireProcessTree: true);
                state.Process.WaitForExit(1000);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error while stopping process with ID {0}", state.Process.Id);
        }
        finally
        {
            state.Process.Dispose();
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

    private static string BuildHtmlFromOutput(string line)
    {
        var result = new StringBuilder();
        var currentStyles = new AnsiStyles();
        var lastIndex = 0;

        foreach (Match match in AnsiCodePattern().Matches(line))
        {
            // Append text before this ANSI code
            if (match.Index > lastIndex)
            {
                var text = System.Net.WebUtility.HtmlEncode(line[lastIndex..match.Index]);
                var classes = currentStyles.ToCssClasses();
                if (classes.Length > 0)
                {
                    result.Append($"<span class=\"{classes}\">{text}</span>");
                }
                else
                {
                    result.Append(text);
                }
            }

            var codes = match.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries);

            // Empty escape sequence (ESC[m) is equivalent to reset (ESC[0m)
            if (codes.Length == 0)
            {
                currentStyles.Reset();
            }
            else
            {
                foreach (var code in codes)
                {
                    if (int.TryParse(code, out var num))
                    {
                        currentStyles.ApplyCode(num);
                    }
                }
            }

            lastIndex = match.Index + match.Length;
        }

        // Append remaining text
        if (lastIndex < line.Length)
        {
            var text = System.Net.WebUtility.HtmlEncode(line[lastIndex..]);
            var classes = currentStyles.ToCssClasses();
            if (classes.Length > 0)
            {
                result.Append($"<span class=\"{classes}\">{text}</span>");
            }
            else
            {
                result.Append(text);
            }
        }

        return result.ToString();
    }

    private static Regex GetPreDefinedRegex(string type) => throw new NotImplementedException("TODO");

    private static ImmutableArray<UrlDetectionWithRegex> GetPreDefinedUrlDetections(string type) => throw new NotImplementedException("TODO");

    private static Func<string, string?> DetectUrlByRegex(ImmutableArray<UrlDetectionWithRegex> urlDetections) => line =>
    {
        foreach (var detection in urlDetections)
        {
            var match = detection.Regex.Match(line);

            if (!match.Success)
            {
                continue;
            }

            var matchedValue = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;

            if (string.IsNullOrWhiteSpace(matchedValue))
            {
                continue;
            }

            if (detection.IsPortOnly && int.TryParse(matchedValue, out var port))
            {
                var scheme = detection.IsHttpsWhenPortOnly ? "https" : "http";
                return $"{scheme}://localhost:{port}";
            }

            return matchedValue;
        }

        return null;
    };

    private static Func<string, bool> DetectStartByRegex(Regex regex) => regex.IsMatch;

    [GeneratedRegex(@"\x1B\[([0-9;]*)m|\x1B\][^\x07]*\x07|\x1B\[[0-9;]*[A-Za-ln-z]")]
    private static partial Regex AnsiCodePattern();

    [GeneratedRegex(@"Now listening on:\s+(https?://\S+)", RegexOptions.IgnoreCase, "en-GB")]
    private static partial Regex FindUrlInDotNetMessagePattern();
}
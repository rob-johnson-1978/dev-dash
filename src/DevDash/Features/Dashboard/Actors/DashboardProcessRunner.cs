using Akka.Actor;
using Akka.Event;
using DevDash;
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
                    _state.ProcessId = command.Id;
                    _state.WorkingDirectory = command.Configuration.PathToFolder;
                    _state.Instructions = command.Configuration.Instructions;

                    if (command.Configuration.StartDetectionRegex != null)
                    {
                        _state.DetectRunnableProcessStartedViaStdOut =
                            DetectStartByRegex(new Regex(command.Configuration.StartDetectionRegex));
                    }

                    if (command.Configuration.PreDefinedStartDetection != null)
                    {
                        _state.DetectRunnableProcessStartedViaStdOut =
                            DetectStartByRegex(GetPreDefinedRegex(command.Configuration.PreDefinedStartDetection));
                    }

                    if (command.Configuration.UrlDetections.Length > 0)
                    {
                        _state.DetectRunnableProcessStartedUrlViaStdOut =
                            DetectUrlByRegex(
                                [.. command
                                    .Configuration
                                    .UrlDetections
                                    .Select(d => new UrlDetectionWithRegex(new Regex(d.Pattern), d.PortOnly, d.HttpsWhenPortOnly))
                                ]
                            );
                    }

                    if (command.Configuration.PreDefinedUrlDetections != null)
                    {
                        _state.DetectRunnableProcessStartedUrlViaStdOut =
                            DetectUrlByRegex(GetPreDefinedUrlDetections(command.Configuration.PreDefinedUrlDetections));
                    }

                    HandleProcessStart();
                    break;
                }
            case RunCompose command:
                {
                    _state.ProcessId = Constants.DockerComposeProcessId;

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

                    _state.DetectRunnableProcessStartedAfterProcessStarted = () =>
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
            case ICommmandRunnableProcessesToChangeState command:
                {
                    if (_state.Process == null || _state.RunStatus != RunStatus.Started)
                    {
                        break;
                    }

                    switch (command)
                    {
                        case StopRunnableProcess:
                            {
                                HandleProcessStop();
                                break;
                            }
                        case RestartRunnableProcess:
                            {
                                Become(WaitingForProcessToStopBeforeRestart);

                                Self.Tell(new StopRunnableProcess(_state.ProcessId));

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
            case ProcessUrlDetected @event:
                {
                    var lowerUrl = @event.Url.ToLower();

                    if (_state.Urls.Contains(lowerUrl))
                    {
                        break;
                    }

                    _state.Urls.Add(lowerUrl);

                    if (_state.DetectRunnableProcessStartedUrlViaStdOut != null && _state.RunStatus != RunStatus.Started)
                    {
                        SetAsRunnableProcessStarted(Context.System, Context.Parent, _state);
                    }
                    else
                    {
                        UpdateRunStatusAndTellParent(_state.RunStatus, Context.Parent, _state);
                    }

                    break;
                }
            case ComposeStarted:
                {
                    SetAsRunnableProcessStarted(Context.System, Context.Parent, _state);
                    break;
                }
            case ComposeStartFailed:
                {
                    PublishProcessLogMessage(
                        Context.System,
                        _state,
                        "Compose failed to start, or start could not be detected. Please fix issues and restart the process."
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
            case StopRunnableProcess:
                {
                    HandleProcessStop();

                    Self.Tell(new StartRunnableProcess(_state.ProcessId));

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
            case ICommmandRunnableProcessesToChangeState command:
                {
                    if (_state.Process == null)
                    {
                        _logger.Warning("Received {0} command but no dashboard process is available.", nameof(ICommmandRunnableProcessesToChangeState));
                        break;
                    }

                    switch (command)
                    {
                        case StartRunnableProcess:
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

        PublishProcessLogMessage(
            Context.System,
            _state,
            "Starting process"
        );

        _state.Process = CreateProcess(Context.System, Self, Context.Parent, _state);

        _state.DetectRunnableProcessStartedAfterProcessStarted?.Invoke();

        RunTask(async () => await StartProcess(_state.Process));

        PublishProcessLogMessage(
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

        PublishProcessLogMessage(
            Context.System,
            _state,
            "Stopping process"
        );

        EnsureProcessIsStopped(_state, _logger);

        PublishProcessLogMessage(
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

            var cleanLine = StripAnsiForDetection(e.Data);

            if (state.DetectRunnableProcessStartedUrlViaStdOut != null)
            {
                foreach (var url in state.DetectRunnableProcessStartedUrlViaStdOut(cleanLine))
                {
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        self.Tell(new ProcessUrlDetected(url));
                    }
                }
            }
            else if (state.DetectRunnableProcessStartedViaStdOut != null)
            {
                var started = state.DetectRunnableProcessStartedViaStdOut(cleanLine);

                if (started)
                {
                    SetAsRunnableProcessStarted(system, parent, state);
                }
            }
            else
            {
                SetAsRunnableProcessStarted(system, parent, state);
            }

            var htmlFormattedLine = BuildHtmlFromOutput(e.Data);

            system.EventStream.Publish(
                DashboardEventRaised.Create(new ProcessOutputLineEmitted(state.ProcessId, htmlFormattedLine))
            );
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            var cleanLine = StripAnsiForDetection(e.Data);

            if (state.DetectRunnableProcessStartedUrlViaStdOut != null)
            {
                foreach (var url in state.DetectRunnableProcessStartedUrlViaStdOut(cleanLine))
                {
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        self.Tell(new ProcessUrlDetected(url));
                    }
                }
            }
            else if (state.DetectRunnableProcessStartedViaStdOut != null)
            {
                var started = state.DetectRunnableProcessStartedViaStdOut(cleanLine);

                if (started)
                {
                    SetAsRunnableProcessStarted(system, parent, state);
                }
            }

            var line = BuildHtmlFromOutput(e.Data);

            system.EventStream.Publish(
                DashboardEventRaised.Create(new ProcessErrorOutputLineEmitted(state.ProcessId, line))
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

    private static void SetAsRunnableProcessStarted(ActorSystem system, IActorRef parent, DashboardProcessRunnerState state)
    {
        if (state.RunStatus == RunStatus.Started)
        {
            return;
        }

        UpdateRunStatusAndTellParent(RunStatus.Started, parent, state);

        PublishProcessLogMessage(
            system,
            state,
            "Process started successfully."
        );

        parent.Tell(new RunnableProcessStarted(state.ProcessId));
    }

    private static void UpdateRunStatusAndTellParent(RunStatus runStatus, IActorRef parent, DashboardProcessRunnerState state)
    {
        state.RunStatus = runStatus;

        parent.Tell(new UpdateRunnableProcess(
            new RunnableProcess(state.ProcessId, state.RunStatus, [.. state.Urls])
        ));
    }

    private static void PublishProcessLogMessage(ActorSystem system, DashboardProcessRunnerState state, string message)
    {
        var typeName = typeof(DashboardProcessRunner).FullName ?? nameof(DashboardProcessRunner);
        var formattedMessage = $"<span class=\"ansi-devdash\">ddsh</span>: {typeName}[0]{Environment.NewLine}      {System.Net.WebUtility.HtmlEncode(message)}";

        system.EventStream.Publish(
            DashboardEventRaised.Create(
                new ProcessOutputLineEmitted(state.ProcessId, formattedMessage)
            )
        );
    }

    private static void EnsureProcessIsStopped(DashboardProcessRunnerState state, ILoggingAdapter logger)
    {
        if (state.Process == null)
        {
            return;
        }

        var processId = state.Process.Id;
        var processHasExited = state.Process.HasExited;

        try
        {
            // Always attempt to kill descendants even if the tracked process has already exited
            KillProcessTree(processId, logger);

            if (!processHasExited && !state.Process.WaitForExit(2000))
            {
                state.Process.Kill(entireProcessTree: true);
                state.Process.WaitForExit(1000);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error while stopping process with ID {0}", processId);
        }
        finally
        {
            state.Process.Dispose();
        }
    }

    private static void KillProcessTree(int processId, ILoggingAdapter logger)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            KillProcessTreeWindows(processId, logger);
            return;
        }

        KillProcessTreeUnix(processId, logger);
    }

    private static void KillProcessTreeWindows(int processId, ILoggingAdapter logger)
    {
        // Use taskkill to forcibly terminate the process and its entire tree
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/PID {processId} /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var killProcess = Process.Start(startInfo);
            killProcess?.WaitForExit(3000);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "taskkill failed for PID {0}", processId);
            try
            {
                var process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
            }
            catch (ArgumentException)
            {
                // Process already exited
            }
            catch (Exception fallbackEx)
            {
                logger.Error(fallbackEx, "Fallback kill failed for PID {0}", processId);
            }
        }
    }

    private static void KillProcessTreeUnix(int processId, ILoggingAdapter logger)
    {
        try
        {
            var allPids = CollectProcessTreeUnix(processId, logger);

            // First attempt a graceful shutdown
            foreach (var pid in allPids)
            {
                TrySendSignal(pid, "-TERM", logger);
            }

            Thread.Sleep(250);

            // Forcefully kill anything still running
            foreach (var pid in allPids)
            {
                TrySendSignal(pid, "-KILL", logger);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to kill process tree for PID {0}", processId);
            // Fallback to killing just the tracked process
            try
            {
                var process = Process.GetProcessById(processId);
                process.Kill();
            }
            catch (ArgumentException)
            {
                // Process already exited
            }
            catch (Exception fallbackEx)
            {
                logger.Error(fallbackEx, "Fallback single kill failed for PID {0}", processId);
            }
        }
    }

    private static int[] CollectProcessTreeUnix(int rootPid, ILoggingAdapter logger)
    {
        var result = new HashSet<int>();
        var queue = new Queue<int>();

        queue.Enqueue(rootPid);

        while (queue.Count > 0)
        {
            var pid = queue.Dequeue();

            if (!result.Add(pid))
            {
                continue;
            }

            foreach (var childPid in GetChildProcessIdsUnix(pid, logger))
            {
                queue.Enqueue(childPid);
            }
        }

        // Kill deepest children first so parents do not immediately respawn them
        return [.. result.OrderByDescending(id => id != rootPid)];
    }

    private static int[] GetChildProcessIdsUnix(int pid, ILoggingAdapter logger)
    {
        var childPids = new List<int>();
       
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/ps",
                Arguments = $"-o pid= --ppid {pid}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var ps = Process.Start(startInfo);
            var output = ps?.StandardOutput.ReadToEnd() ?? string.Empty;
            ps?.WaitForExit(1000);

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(line, out var childPid))
                {
                    childPids.Add(childPid);
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to get child PIDs for PID {0}", pid);
        }

        return [.. childPids];
    }

    private static void TrySendSignal(int pid, string signal, ILoggingAdapter logger)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/kill",
                Arguments = $"{signal} {pid}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var kill = Process.Start(startInfo);
            kill?.WaitForExit(1000);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to send signal {0} to PID {1}", signal, pid);
            try
            {
                var process = Process.GetProcessById(pid);
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception fallbackEx)
            {
                logger.Error(fallbackEx, "Fallback kill failed for PID {0}", pid);
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

    private static string StripAnsiForDetection(string line) =>
        AnsiCodePattern()
            .Replace(line, string.Empty)
            .TrimStart();

    private static Func<string, IEnumerable<string>> DetectUrlByRegex(ImmutableArray<UrlDetectionWithRegex> urlDetections)
    {
        return line => EnumerateMatches(line, urlDetections);

        static IEnumerable<string> EnumerateMatches(string line, ImmutableArray<UrlDetectionWithRegex> detections)
        {
            foreach (var detection in detections)
            {
                foreach (Match match in detection.Regex.Matches(line))
                {
                    if (!match.Success)
                    {
                        continue;
                    }

                    var matchedValue = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;

                    if (string.IsNullOrWhiteSpace(matchedValue))
                    {
                        continue;
                    }

                    if (detection.PortOnly && int.TryParse(matchedValue, out var port))
                    {
                        var scheme = detection.HttpsWhenPortOnly ? "https" : "http";
                        yield return $"{scheme}://localhost:{port}";
                        continue;
                    }

                    yield return matchedValue;
                }
            }
        }
    }

    private static Func<string, bool> DetectStartByRegex(Regex regex) => regex.IsMatch;

    [GeneratedRegex(@"\x1B\[([0-9;]*)m|\x1B\][^\x07]*\x07|\x1B\[[0-9;]*[A-Za-ln-z]")]
    private static partial Regex AnsiCodePattern();
}
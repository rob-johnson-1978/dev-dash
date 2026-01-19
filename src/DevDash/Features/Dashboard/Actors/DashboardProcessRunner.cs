using Akka.Actor;
using Akka.Event;
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

        // Force .NET apps to emit ANSI codes even when stdout is redirected
        processStartInfo.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "true";

        // For older .NET Core versions
        processStartInfo.Environment["DOTNET_ConsoleColors"] = "true";

        // For Node.js/npm tools
        processStartInfo.Environment["FORCE_COLOR"] = "1";

        // For many CLI tools
        processStartInfo.Environment["CLICOLOR_FORCE"] = "1";  

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

            var line = BuildHtmlFromOutput(e.Data);

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

            var line = BuildHtmlFromOutput(e.Data);

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

    private void PublishActionLogMessage(string message)
    {
        var typeName = GetType().FullName ?? nameof(DashboardProcessRunner);
        var formattedMessage = $"<span class=\"ansi-devdash\">ddsh</span>: {typeName}[0]{Environment.NewLine}      {System.Net.WebUtility.HtmlEncode(message)}";

        Context.System.EventStream.Publish(
            DashboardEventRaised.Create(
                new ApplicationOutputLineEmitted(_applicationId, formattedMessage)
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

    private sealed class AnsiStyles
    {
        public bool Bold { get; private set; }
        public bool Dim { get; private set; }
        public bool Italic { get; private set; }
        public bool Underline { get; private set; }
        public bool Strikethrough { get; private set; }
        public string? ForegroundColor { get; private set; }
        public string? BackgroundColor { get; private set; }

        public void Reset()
        {
            Bold = false;
            Dim = false;
            Italic = false;
            Underline = false;
            Strikethrough = false;
            ForegroundColor = null;
            BackgroundColor = null;
        }

        public void ApplyCode(int code)
        {
            switch (code)
            {
                // Reset
                case 0:
                    Reset();
                    break;

                // Text styles
                case 1:
                    Bold = true;
                    break;
                case 2:
                    Dim = true;
                    break;
                case 3:
                    Italic = true;
                    break;
                case 4:
                    Underline = true;
                    break;
                case 9:
                    Strikethrough = true;
                    break;

                // Reset text styles
                case 22:
                    Bold = false;
                    Dim = false;
                    break;
                case 23:
                    Italic = false;
                    break;
                case 24:
                    Underline = false;
                    break;
                case 29:
                    Strikethrough = false;
                    break;

                // Standard foreground colors (30-37)
                case 30:
                    ForegroundColor = "ansi-black";
                    break;
                case 31:
                    ForegroundColor = "ansi-red";
                    break;
                case 32:
                    ForegroundColor = "ansi-green";
                    break;
                case 33:
                    ForegroundColor = "ansi-yellow";
                    break;
                case 34:
                    ForegroundColor = "ansi-blue";
                    break;
                case 35:
                    ForegroundColor = "ansi-magenta";
                    break;
                case 36:
                    ForegroundColor = "ansi-cyan";
                    break;
                case 37:
                    ForegroundColor = "ansi-white";
                    break;

                // Default foreground color
                case 39:
                    ForegroundColor = null;
                    break;

                // Standard background colors (40-47)
                case 40:
                    BackgroundColor = "ansi-bg-black";
                    break;
                case 41:
                    BackgroundColor = "ansi-bg-red";
                    break;
                case 42:
                    BackgroundColor = "ansi-bg-green";
                    break;
                case 43:
                    BackgroundColor = "ansi-bg-yellow";
                    break;
                case 44:
                    BackgroundColor = "ansi-bg-blue";
                    break;
                case 45:
                    BackgroundColor = "ansi-bg-magenta";
                    break;
                case 46:
                    BackgroundColor = "ansi-bg-cyan";
                    break;
                case 47:
                    BackgroundColor = "ansi-bg-white";
                    break;

                // Default background color
                case 49:
                    BackgroundColor = null;
                    break;

                // Bright foreground colors (90-97)
                case 90:
                    ForegroundColor = "ansi-bright-black";
                    break;
                case 91:
                    ForegroundColor = "ansi-bright-red";
                    break;
                case 92:
                    ForegroundColor = "ansi-bright-green";
                    break;
                case 93:
                    ForegroundColor = "ansi-bright-yellow";
                    break;
                case 94:
                    ForegroundColor = "ansi-bright-blue";
                    break;
                case 95:
                    ForegroundColor = "ansi-bright-magenta";
                    break;
                case 96:
                    ForegroundColor = "ansi-bright-cyan";
                    break;
                case 97:
                    ForegroundColor = "ansi-bright-white";
                    break;

                // Bright background colors (100-107)
                case 100:
                    BackgroundColor = "ansi-bg-bright-black";
                    break;
                case 101:
                    BackgroundColor = "ansi-bg-bright-red";
                    break;
                case 102:
                    BackgroundColor = "ansi-bg-bright-green";
                    break;
                case 103:
                    BackgroundColor = "ansi-bg-bright-yellow";
                    break;
                case 104:
                    BackgroundColor = "ansi-bg-bright-blue";
                    break;
                case 105:
                    BackgroundColor = "ansi-bg-bright-magenta";
                    break;
                case 106:
                    BackgroundColor = "ansi-bg-bright-cyan";
                    break;
                case 107:
                    BackgroundColor = "ansi-bg-bright-white";
                    break;
            }
        }

        public string ToCssClasses()
        {
            var classes = new List<string>(4);

            if (Bold) classes.Add("ansi-bold");
            if (Dim) classes.Add("ansi-dim");
            if (Italic) classes.Add("ansi-italic");
            if (Underline) classes.Add("ansi-underline");
            if (Strikethrough) classes.Add("ansi-strikethrough");
            if (ForegroundColor != null) classes.Add(ForegroundColor);
            if (BackgroundColor != null) classes.Add(BackgroundColor);

            return string.Join(" ", classes);
        }
    }

    [GeneratedRegex(@"\x1B\[([0-9;]*)m|\x1B\][^\x07]*\x07|\x1B\[[0-9;]*[A-Za-ln-z]")]
    private static partial Regex AnsiCodePattern();

    [GeneratedRegex(@"Now listening on:\s+(https?://\S+)", RegexOptions.IgnoreCase, "en-GB")]
    private static partial Regex FindUrlInDotNetMessagePattern();
}
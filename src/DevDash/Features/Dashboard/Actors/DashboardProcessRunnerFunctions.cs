using Akka.Actor;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DevDash.Features.Dashboard.Actors;

internal partial class DashboardProcessRunner
{
    /* helpers */

    private Process CreateProcess()
    {
        var actorSystem = Context.System;
        var parent = Context.Parent;
        var self = Self;
        var state = _state;

        var processStartInfo = new ProcessStartInfo
        {
            FileName = _state.FileName,
            Arguments = string.Join(" ", _state.Args),
            WorkingDirectory = _state.WorkingDirectory,
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

            if (_state.DetectStartedViaStdOut != null)
            {
                var started = _state.DetectStartedViaStdOut(e.Data);

                if (started)
                {
                    ProcessApplicationStarted(parent, state);
                }
            }

            if (_state.FindUrlViaStdOut != null)
            {
                var url = _state.FindUrlViaStdOut(e.Data);

                if (url != null && !string.IsNullOrWhiteSpace(url))
                {
                    self.Tell(new ApplicationUrlDetected(url));
                }
            }

            var htmlFormattedLine = BuildHtmlFromOutput(e.Data);

            actorSystem.EventStream.Publish(
                DashboardEventRaised.Create(new ApplicationOutputLineEmitted(_state.ApplicationId, htmlFormattedLine))
            );
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            var line = BuildHtmlFromOutput(e.Data);

            actorSystem.EventStream.Publish(
                DashboardEventRaised.Create(new ApplicationErrorOutputLineEmitted(_state.ApplicationId, line))
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
            new RunnableApplication(_state.ApplicationId, _state.RunStatus, [.. _state.Urls])
        ));
    }

    private static void ProcessApplicationStarted(IActorRef parent, DashboardProcessRunnerState state)
    {
        if (state.RunStatus == RunStatus.Started)
        {
            return;
        }

        state.RunStatus = RunStatus.Started;

        parent.Tell(new RunnableApplicationStarted(state.ApplicationId));
    }

    private void PublishActionLogMessage(string message)
    {
        var typeName = GetType().FullName ?? nameof(DashboardProcessRunner);
        var formattedMessage = $"<span class=\"ansi-devdash\">ddsh</span>: {typeName}[0]{Environment.NewLine}      {System.Net.WebUtility.HtmlEncode(message)}";

        Context.System.EventStream.Publish(
            DashboardEventRaised.Create(
                new ApplicationOutputLineEmitted(_state.ApplicationId, formattedMessage)
            )
        );
    }

    private void EnsureProcessIsStopped()
    {
        if (_state.Process == null)
        {
            return;
        }

        if (_state.Process.HasExited)
        {
            try
            {
                _state.Process.Dispose();
                return;
            }
            catch
            {
            }
        }

        try
        {
            // Kill the entire process tree
            KillProcessTree(_state.Process.Id);
        }
        catch
        {
            // Fallback to simple kill if tree kill fails
            try
            {
                _state.Process.Kill();
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

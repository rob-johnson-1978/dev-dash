using Akka.Actor;
using Akka.Event;
using System.Diagnostics;
using System.Text.Json;

namespace DevDash.Features.Dashboard.Actors;

internal class ComposeStatusProvider : UntypedActor, IWithTimers
{
    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private readonly ComposeStatusProviderState _state = new();
    private const string CheckStatusTimerKey = "CheckComposeStatusTimer";
    private const string TimeoutTimerKey = "ComposeStatusTimeoutTimer";

    public ITimerScheduler Timers { get; set; } = null!;

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case WaitForComposeStatusToBecomeAvailable command:
                {
                    _state.WorkingDirectory = command.WorkingDirectory;
                    _state.FullComposePath = command.FullComposePath;
                    _state.ComposeType = command.ComposeType;

                    Timers.StartSingleTimer(
                        CheckStatusTimerKey,
                        new CheckComposeStatus(),
                        TimeSpan.FromSeconds(2)
                    );

                    Timers.StartSingleTimer(
                        TimeoutTimerKey,
                        new ComposeStatusCheckTimedOut(),
                        TimeSpan.FromSeconds(command.CheckTimeoutInSeconds)
                    );

                    break;
                }
            case CheckComposeStatus:
                {
                    var statusResult = GetStatusCheckResult();

                    if (statusResult.Success)
                    {
                        Context.Parent.Tell(new ComposeStarted());

                        Context.Stop(Self);

                        break;
                    }

                    Timers.StartSingleTimer(
                        CheckStatusTimerKey,
                        new CheckComposeStatus(),
                        TimeSpan.FromSeconds(2)
                    );

                    break;
                }
            case ComposeStatusCheckTimedOut:
                {
                    Timers.Cancel(CheckStatusTimerKey);

                    Context.Parent.Tell(new ComposeStartFailed());

                    Context.Stop(Self);

                    break;
                }
            default:
                {
                    Unhandled(message);
                    break;
                }
        }
    }

    private ComposeCheckStatusResult GetStatusCheckResult()
    {
        var isDocker = _state.ComposeType == ComposeType.Docker;

        var processStartInfo = new ProcessStartInfo
        {
            FileName = isDocker ? "docker" : "podman",
            WorkingDirectory = _state.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        processStartInfo.ArgumentList.Add("compose");
        processStartInfo.ArgumentList.Add("-f");
        processStartInfo.ArgumentList.Add(_state.FullComposePath);
        processStartInfo.ArgumentList.Add("ps");
        processStartInfo.ArgumentList.Add("--format");
        processStartInfo.ArgumentList.Add("json");

        try
        {
            using var process = Process.Start(processStartInfo);

            if (process is null)
            {
                _logger.Error("Failed to start process for compose status check.");

                return new ComposeCheckStatusResult(false);
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            process.WaitForExit();

            var output = outputTask.Result;
            var errorOutput = errorTask.Result;

            if (process.ExitCode != 0)
            {
                _logger.Error("Compose status check process exited with code {0}. Error output: {1}", process.ExitCode, errorOutput);
                return new ComposeCheckStatusResult(false);
            }

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                _logger.Debug("{0}", line);

                if (!line.StartsWith('{'))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var state = root.GetProperty("State").GetString();
                if (state is not "running")
                {
                    _logger.Info("Compose service is not running. State: {0}", state);

                    return new ComposeCheckStatusResult(false);
                }

                if (root.TryGetProperty("Health", out var healthElement))
                {
                    var health = healthElement.GetString();

                    if (!string.IsNullOrEmpty(health) && health is not "healthy")
                    {
                        _logger.Info("Compose service health is not healthy. Health: {0}", health);

                        return new ComposeCheckStatusResult(false);
                    }
                }
            }

            var ok = lines.Length > 0;

            if (ok)
            {
                _logger.Info("All compose services are running and healthy.");

                return new ComposeCheckStatusResult(ok);
            }

            _logger.Info("No compose services found in compose status output.");

            return new ComposeCheckStatusResult(false);
        }
        catch
        {
            return new ComposeCheckStatusResult(false);
        }
    }
}

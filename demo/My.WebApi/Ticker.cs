using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace My.WebApi;

public class Ticker(ILogger<Ticker> logger) : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("My.WebApi.Ticker");
    private static readonly Meter Meter = new("My.WebApi.Ticker");
    private readonly Counter<long> _tickCounter = Meter.CreateCounter<long>("ticker.ticks", description: "Number of ticker iterations");
    private readonly Histogram<double> _delayHistogram = Meter.CreateHistogram<double>("ticker.delay", unit: "ms", description: "Delay between ticks");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var spanId = Guid.NewGuid().ToString()[..8];

        while (!stoppingToken.IsCancellationRequested)
        {
            using var activity = ActivitySource.StartActivity("Ticker.Tick", ActivityKind.Internal);

            activity?.SetTag("span.id", spanId);
            activity?.SetTag("iteration.random", Random.Shared.Next(1, 100));

#pragma warning disable CA1873 // Avoid potentially expensive logging
            logger.LogInformation("{a},{b},{c}", Faker.Lorem.Sentence(), Faker.Lorem.Sentence(), Faker.Lorem.Sentence());
#pragma warning restore CA1873 // Avoid potentially expensive logging

            _tickCounter.Add(1, new KeyValuePair<string, object?>("span.id", spanId));
            
            var delay = Random.Shared.Next(500, 1000);
            _delayHistogram.Record(delay, new KeyValuePair<string, object?>("span.id", spanId));
            
            activity?.SetTag("delay.ms", delay);
            activity?.AddEvent(new ActivityEvent("TickCompleted"));
            
            await Task.Delay(delay, stoppingToken);
        }
    }
}

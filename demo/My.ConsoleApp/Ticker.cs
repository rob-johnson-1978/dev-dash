using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace My.ConsoleApp;

public class Ticker(ILogger<Ticker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
#pragma warning disable CA1873 // Avoid potentially expensive logging
            logger.LogInformation("{a},{b},{c}", Faker.Lorem.Sentence(), Faker.Lorem.Sentence(), Faker.Lorem.Sentence());
#pragma warning restore CA1873 // Avoid potentially expensive logging

            var delay = Random.Shared.Next(5000, 15000);

            await Task.Delay(delay, stoppingToken);
        }
    }
}

namespace My.WebApi;

public class Ticker(ILogger<Ticker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
#pragma warning disable CA1873 // Avoid potentially expensive logging
            
            logger.LogTrace("{a},{b},{c}", Faker.Lorem.Sentence(), Faker.Lorem.Sentence(), Faker.Lorem.Sentence());                       
            await Task.Delay(Random.Shared.Next(100, 500), stoppingToken);

            logger.LogDebug("{a},{b},{c}", Faker.Lorem.Sentence(), Faker.Lorem.Sentence(), Faker.Lorem.Sentence());
            await Task.Delay(Random.Shared.Next(100, 500), stoppingToken);

            logger.LogInformation("{a},{b},{c}", Faker.Lorem.Sentence(), Faker.Lorem.Sentence(), Faker.Lorem.Sentence());
            await Task.Delay(Random.Shared.Next(100, 500), stoppingToken);

            logger.LogWarning("{a},{b},{c}", Faker.Lorem.Sentence(), Faker.Lorem.Sentence(), Faker.Lorem.Sentence());
            await Task.Delay(Random.Shared.Next(100, 500), stoppingToken);

            logger.LogError(new NotImplementedException(), "{a},{b},{c}", Faker.Lorem.Sentence(), Faker.Lorem.Sentence(), Faker.Lorem.Sentence());
            await Task.Delay(Random.Shared.Next(100, 500), stoppingToken);

            logger.LogCritical(new NotImplementedException(), "{a},{b},{c}", Faker.Lorem.Sentence(), Faker.Lorem.Sentence(), Faker.Lorem.Sentence());
            await Task.Delay(Random.Shared.Next(100, 500), stoppingToken);

#pragma warning restore CA1873 // Avoid potentially expensive logging
        }
    }
}

using Microsoft.Extensions.Options;

namespace SolarOps.Api.Features.Quotes;

/// <summary>Periodically marks sent quotes past their validity as Expired.</summary>
internal sealed class QuoteExpirationService(
    IServiceScopeFactory scopeFactory,
    IOptions<QuoteOptions> options,
    ILogger<QuoteExpirationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.ExpirationCheckMinutes)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var expired = await scope.ServiceProvider.GetRequiredService<IQuoteService>().ExpireOverdueAsync(stoppingToken);
                if (expired > 0)
                {
                    logger.LogInformation("Expired {Count} quotes", expired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Quote expiration run failed");
            }
        }
    }
}

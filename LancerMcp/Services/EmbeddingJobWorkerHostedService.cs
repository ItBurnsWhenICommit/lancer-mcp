using Microsoft.Extensions.Hosting;

namespace LancerMcp.Services;

public sealed class EmbeddingJobWorkerHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly EmbeddingJobWorker _worker;
    private readonly ILogger<EmbeddingJobWorkerHostedService> _logger;

    public EmbeddingJobWorkerHostedService(EmbeddingJobWorker worker, ILogger<EmbeddingJobWorkerHostedService> logger)
    {
        _worker = worker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _worker.ProcessOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Embedding job worker loop failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }
}

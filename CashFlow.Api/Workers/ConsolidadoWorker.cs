using CashFlow.Api.Infrastructure.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Api.Workers;

public class ConsolidadoWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;

    public ConsolidadoWorker(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<ConsolidadoRepository>();

                await repo.AtualizarSaldo(DateTime.UtcNow, 10);
            }

            await Task.Delay(5000, stoppingToken);
        }
    }
}

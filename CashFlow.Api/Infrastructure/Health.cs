

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace CashFlow.Api.Infrastructure.Health;

/// <summary>
/// Verifica se o OutboxWorker está processando.
/// Considera unhealthy se o último processamento
/// foi há mais de 2 minutos (sinal de worker travado).
/// </summary>
public class OutboxWorkerHealthCheck : IHealthCheck
{
    private static DateTime _ultimoProcessamento = DateTime.UtcNow;

    // Chamado pelo worker a cada ciclo bem-sucedido
    public static void RegistrarAtividade() =>
        _ultimoProcessamento = DateTime.UtcNow;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var tempoParado = DateTime.UtcNow - _ultimoProcessamento;

        if (tempoParado > TimeSpan.FromMinutes(2))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Worker parado há {tempoParado.TotalMinutes:F0} minutos"
            ));
        }

        if (tempoParado > TimeSpan.FromSeconds(30))
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Worker lento: último ciclo há {tempoParado.TotalSeconds:F0}s"
            ));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Worker ativo: último ciclo há {tempoParado.TotalSeconds:F0}s"
        ));
    }
}
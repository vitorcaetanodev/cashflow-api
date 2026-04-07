using Microsoft.Extensions.Hosting;
using Polly;
using Polly.CircuitBreaker;
using CashFlow.Api.Domain.Entities;
using CashFlow.Api.Infrastructure.Repositories;
using CashFlow.Api.Infrastructure.Health;

namespace CashFlow.Api.Workers;

public class OutboxWorker : BackgroundService
{
    private readonly IOutboxRepository          _outboxRepo;
    private readonly IConsolidadoRepository     _consolidadoRepo;
    private readonly IEventosProcessadosRepository _idempotencia;
    private readonly ILogger<OutboxWorker>      _logger;
    private readonly IConfiguration             _config;

    private readonly IAsyncPolicy _retryPolicy;
    private readonly IAsyncPolicy _circuitBreakerPolicy;
    private readonly IAsyncPolicy _resiliencePolicy;

    public OutboxWorker(
        IOutboxRepository          outboxRepo,
        IConsolidadoRepository     consolidadoRepo,
        IEventosProcessadosRepository idempotencia,
        ILogger<OutboxWorker>      logger,
        IConfiguration             config)
    {
        _outboxRepo      = outboxRepo;
        _consolidadoRepo = consolidadoRepo;
        _idempotencia    = idempotencia;
        _logger          = logger;
        _config          = config;

        // Retry: 3 tentativas com backoff exponencial
        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (ex, delay, attempt, _) =>
                    _logger.LogWarning(ex,
                        "Retry {Attempt}/3 em {Delay}s", attempt, delay.TotalSeconds)
            );

        // Circuit breaker: abre após 5 falhas consecutivas
        _circuitBreakerPolicy = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak:   (ex, duration) => _logger.LogError(ex,
                    "Circuit breaker ABERTO por {Sec}s", duration.TotalSeconds),
                onReset:   ()             => _logger.LogInformation("Circuit breaker FECHADO"),
                onHalfOpen: ()            => _logger.LogInformation("Circuit breaker EM TESTE")
            );

        _resiliencePolicy = Policy.WrapAsync(_retryPolicy, _circuitBreakerPolicy);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMs = _config.GetValue<int>("Worker:IntervalMs", 2000);
        var batchSize  = _config.GetValue<int>("Worker:BatchSize", 100);
        var maxRetries = _config.GetValue<int>("Worker:MaxRetries", 5);

        _logger.LogInformation(
            "OutboxWorker iniciado. Intervalo: {Ms}ms, Batch: {Batch}",
            intervalMs, batchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessarLote(batchSize, maxRetries, stoppingToken);
                OutboxWorkerHealthCheck.RegistrarAtividade();
            }
            catch (BrokenCircuitException)
            {
                _logger.LogWarning("Circuit breaker aberto — aguardando recuperação");
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Erro inesperado no worker");
            }

            await Task.Delay(intervalMs, stoppingToken);
        }

        _logger.LogInformation("OutboxWorker encerrado");
    }

    private async Task ProcessarLote(
        int batchSize, int maxRetries, CancellationToken ct)
    {
        var eventos = await _outboxRepo.BuscarPendentes(batchSize);
        if (!eventos.Any()) return;

        _logger.LogInformation("Processando {Count} eventos", eventos.Count());

        foreach (var evento in eventos)
        {
            if (ct.IsCancellationRequested) break;

            await ProcessarEvento(evento, maxRetries);
        }
    }

    private async Task ProcessarEvento(OutboxEvent evento, int maxRetries)
    {
        using var scope = _logger.BeginScope(
            new Dictionary<string, object> { ["EventoId"] = evento.Id });

        try
        {
            // Idempotência: ignora se já processado
            if (await _idempotencia.JaProcessado(evento.Id))
            {
                _logger.LogDebug("Evento {Id} já processado — ignorando", evento.Id);
                await _outboxRepo.MarcarProcessado(evento.Id);
                return;
            }

            await _resiliencePolicy.ExecuteAsync(async () =>
            {
                var payload = System.Text.Json.JsonSerializer
                    .Deserialize<LancamentoCriadoPayload>(evento.Payload)!;

                // Débito reduz saldo
                var valor = payload.Tipo == "DEBITO" ? -payload.Valor : payload.Valor;

                await _consolidadoRepo.AtualizarSaldo(payload.Data.Date, valor);
                await _idempotencia.Registrar(evento.Id);
                await _outboxRepo.MarcarProcessado(evento.Id);

                _logger.LogInformation(
                    "Evento {Id} processado. Tipo: {Tipo}, Valor: {Valor}",
                    evento.Id, payload.Tipo, valor);
            });
        }
        catch (Exception ex)
        {
            var novasTentativas = evento.Tentativas + 1;
            await _outboxRepo.IncrementarTentativas(evento.Id);

            _logger.LogError(ex,
                "Falha ao processar evento {Id}. Tentativa {N}/{Max}",
                evento.Id, novasTentativas, maxRetries);

            if (novasTentativas >= maxRetries)
            {
                await _outboxRepo.MoverParaDeadLetter(evento, ex.Message);
                _logger.LogWarning(
                    "Evento {Id} movido para dead letter após {N} falhas",
                    evento.Id, novasTentativas);
            }
        }
    }

    private record LancamentoCriadoPayload(
        decimal  Valor,
        string   Tipo,
        DateTime Data
    );
}

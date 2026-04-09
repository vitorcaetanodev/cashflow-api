using CashFlow.Api.Domain.Entities;
using CashFlow.Api.Infrastructure.Repositories;

namespace CashFlow.Api.Application;

public class LancamentoService
{
    private readonly ILancamentoRepository _repo;
    private readonly ILogger<LancamentoService> _logger;
    private readonly IKafkaProducer _kafka;

    public LancamentoService(
        ILancamentoRepository repo,
        ILogger<LancamentoService> logger,
        IKafkaProducer kafka)
    {
        _repo = repo;
        _logger = logger;
        _kafka = kafka;
    }

    public async Task Criar(decimal valor, TipoLancamento tipo)
    {
        if (valor <= 0)
            throw new ArgumentException("O valor deve ser maior que zero", nameof(valor));

        try
        {
            var lancamento = new Lancamento
            {
                Valor = valor,
                Tipo = tipo,
                Data = DateTime.UtcNow
            };

            await _repo.Inserir(lancamento);

            _logger.LogInformation("Lançamento criado {Id}", lancamento.Id);
            
                await _kafka.PublishAsync("lancamentos", new
                {
                    Id = lancamento.Id,
                    Valor = lancamento.Valor,
                    Tipo = lancamento.Tipo,
                    Data = lancamento.Data
                }); 
            
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar lançamento");
            throw;
        }
    }
}
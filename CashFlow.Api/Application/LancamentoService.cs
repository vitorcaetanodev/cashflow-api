using CashFlow.Api.Domain.Entities;
using CashFlow.Api.Infrastructure.Repositories;

namespace CashFlow.Api.Application;

public class LancamentoService
{
   private readonly LancamentoRepository _repo;
   private readonly ILogger<LancamentoService> _logger;
   private readonly KafkaProducer _kafka;


   public LancamentoService(LancamentoRepository repo, ILogger<LancamentoService> logger, KafkaProducer kafka)
   {
       _repo = repo;
       _logger = logger;
       _kafka = kafka;
   }

   public async Task Criar(decimal valor, TipoLancamento tipo)
   {
       try
       {
           var l = new Lancamento { Valor = valor, Tipo = tipo };

           await _repo.Inserir(l);

           _logger.LogInformation("Lançamento criado {Id}", l.Id);

           await _kafka.PublishAsync("lancamentos", new
            {
                Id = l.Id,
                Valor = l.Valor,
                Tipo = l.Tipo,
                Data = l.Data
            });
       }
       catch (Exception ex)
       {
           _logger.LogError(ex, "Erro ao criar lançamento");
           throw;
       }
   }
}

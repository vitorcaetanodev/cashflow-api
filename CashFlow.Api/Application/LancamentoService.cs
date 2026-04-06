using CashFlow.Api.Domain.Entities;
using CashFlow.Api.Infrastructure.Repositories;

namespace CashFlow.Api.Application;

public class LancamentoService
{
   private readonly LancamentoRepository _repo;
   private readonly ILogger<LancamentoService> _logger;

   public LancamentoService(LancamentoRepository repo, ILogger<LancamentoService> logger)
   {
       _repo = repo;
       _logger = logger;
   }

   public async Task Criar(decimal valor, TipoLancamento tipo)
   {
       try
       {
           var l = new Lancamento { Valor = valor, Tipo = tipo };

           await _repo.Inserir(l);

           _logger.LogInformation("Lançamento criado {Id}", l.Id);
       }
       catch (Exception ex)
       {
           _logger.LogError(ex, "Erro ao criar lançamento");
           throw;
       }
   }
}

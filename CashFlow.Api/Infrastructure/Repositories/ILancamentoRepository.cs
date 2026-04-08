using CashFlow.Api.Domain.Entities;
namespace CashFlow.Api.Infrastructure.Repositories;
public interface ILancamentoRepository
{
    Task Inserir(Lancamento lancamento);

    Task InserirComOutbox(Lancamento lancamento, OutboxEvent evento);

    Task<IEnumerable<Lancamento>> ObterTodos();
}
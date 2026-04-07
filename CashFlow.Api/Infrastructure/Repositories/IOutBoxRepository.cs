using CashFlow.Api.Domain.Entities;

namespace CashFlow.Api.Infrastructure.Repositories;

public interface IOutboxRepository
{
    Task<IEnumerable<OutboxEvent>> BuscarPendentes(int limit);
    Task MarcarProcessado(Guid id);
    Task IncrementarTentativas(Guid id);
    Task MoverParaDeadLetter(OutboxEvent evento, string erro);
}
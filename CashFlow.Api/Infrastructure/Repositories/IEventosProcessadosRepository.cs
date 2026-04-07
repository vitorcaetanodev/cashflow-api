namespace CashFlow.Api.Infrastructure.Repositories;

public interface IEventosProcessadosRepository
{
    Task<bool> JaProcessado(Guid id);
    Task Registrar(Guid id);
}
using CashFlow.Api.Infrastructure;
using Dapper;

namespace CashFlow.Api.Infrastructure.Repositories;

public class EventosProcessadosRepository : IEventosProcessadosRepository
{
    private readonly DbConnectionFactory _factory;

    public EventosProcessadosRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<bool> JaProcessado(Guid id)
    {
        using var conn = _factory.Create();

        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM eventos_processados WHERE id = @Id)",
            new { Id = id });
    }

    public async Task Registrar(Guid id)
    {
        using var conn = _factory.Create();

        await conn.ExecuteAsync(
            "INSERT INTO eventos_processados (id) VALUES (@Id) ON CONFLICT (id) DO NOTHING",
            new { Id = id });
    }
}
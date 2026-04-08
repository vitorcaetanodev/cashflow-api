using CashFlow.Api.Infrastructure;
using CashFlow.Api.Domain.Entities;
using Dapper;

namespace CashFlow.Api.Infrastructure.Repositories;public class LancamentoRepository : ILancamentoRepository
{
    private readonly DbConnectionFactory _factory;

    public LancamentoRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task Inserir(Lancamento l)
    {
        using var conn = _factory.Create();

        var sql = @"INSERT INTO lancamentos (id, valor, tipo, data)
                    VALUES (@Id, @Valor, @Tipo, @Data)";

        await conn.ExecuteAsync(sql, new
        {
            l.Id,
            l.Valor,
            Tipo = l.Tipo.ToString().ToUpper(),
            l.Data
        });
    }

    // 🔥 AQUI ESTÁ O CORE DA ARQUITETURA (OUTBOX PATTERN)
    public async Task InserirComOutbox(Lancamento l, OutboxEvent evento)
    {
        using var conn = _factory.Create();
        conn.Open();

        using var transaction = conn.BeginTransaction();

        try
        {
            // 1. Inserir lançamento
            var sqlLancamento = @"INSERT INTO lancamentos (id, valor, tipo, data)
                                  VALUES (@Id, @Valor, @Tipo, @Data)";

            await conn.ExecuteAsync(sqlLancamento, new
            {
                l.Id,
                l.Valor,
                Tipo = l.Tipo.ToString().ToUpper(),
                l.Data
            }, transaction);

            // 2. Inserir evento no OUTBOX
            var sqlOutbox = @"INSERT INTO outbox_events 
                                (id, tipo_evento, payload, processado, tentativas)
                              VALUES 
                                (@Id, @TipoEvento, @Payload::jsonb, false, 0)";

            await conn.ExecuteAsync(sqlOutbox, new
            {
                evento.Id,
                evento.TipoEvento,
                evento.Payload
            }, transaction);

            // 3. Commit
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IEnumerable<Lancamento>> ObterTodos()
    {
        using var conn = _factory.Create();
        return await conn.QueryAsync<Lancamento>("SELECT * FROM lancamentos");
    }
}

using CashFlow.Api.Domain.Entities;
using CashFlow.Api.Infrastructure;
using Dapper;

namespace CashFlow.Api.Infrastructure.Repositories;

public class OutboxRepository : IOutboxRepository
{
    private readonly DbConnectionFactory _factory;

    public OutboxRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IEnumerable<OutboxEvent>> BuscarPendentes(int limit)
    {
        using var conn = _factory.Create();

        var sql = @"
            SELECT id, tipo_evento AS TipoEvento, payload, processado, tentativas, criado_em AS CriadoEm
            FROM outbox_events
            WHERE processado = FALSE
              AND tentativas < 5
            ORDER BY criado_em
            LIMIT @Limit
            FOR UPDATE SKIP LOCKED";

        return await conn.QueryAsync<OutboxEvent>(sql, new { Limit = limit });
    }

    public async Task MarcarProcessado(Guid id)
    {
        using var conn = _factory.Create();

        var sql = @"
            UPDATE outbox_events
            SET processado    = TRUE,
                processado_em = CURRENT_TIMESTAMP
            WHERE id = @Id";

        await conn.ExecuteAsync(sql, new { Id = id });
    }

    public async Task IncrementarTentativas(Guid id)
    {
        using var conn = _factory.Create();

        await conn.ExecuteAsync(
            "UPDATE outbox_events SET tentativas = tentativas + 1 WHERE id = @Id",
            new { Id = id });
    }

    public async Task MoverParaDeadLetter(OutboxEvent evento, string erro)
    {
        using var conn = _factory.Create();
        conn.Open();
        using var tx = conn.BeginTransaction();

        await conn.ExecuteAsync(@"
            INSERT INTO dead_letter_events (outbox_id, tipo_evento, payload, tentativas, erro)
            VALUES (@OutboxId, @TipoEvento, @Payload::jsonb, @Tentativas, @Erro)",
            new {
                OutboxId   = evento.Id,
                evento.TipoEvento,
                evento.Payload,
                evento.Tentativas,
                Erro       = erro
            }, tx);

        await conn.ExecuteAsync(@"
            UPDATE outbox_events
            SET processado = TRUE, processado_em = CURRENT_TIMESTAMP
            WHERE id = @Id",
            new { evento.Id }, tx);

        tx.Commit();
    }
}
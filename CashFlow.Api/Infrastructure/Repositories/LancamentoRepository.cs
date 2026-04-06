using CashFlow.Api.Infrastructure;
using CashFlow.Api.Domain.Entities;
using Dapper;

namespace CashFlow.Api.Infrastructure.Repositories;
public class LancamentoRepository
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

        await conn.ExecuteAsync(sql, new {
            l.Id,
            l.Valor,
            Tipo = l.Tipo.ToString().ToUpper(),  // ← converte "Credito" para "CREDITO"
            l.Data
        });
    }

   public async Task<IEnumerable<Lancamento>> ObterTodos()
   {
       using var conn = _factory.Create();
       return await conn.QueryAsync<Lancamento>("SELECT * FROM lancamentos");
   }
}

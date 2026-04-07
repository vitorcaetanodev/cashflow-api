using CashFlow.Api.Infrastructure;
using Dapper;
public class ConsolidadoRepository : IConsolidadoRepository
{
   private readonly DbConnectionFactory _factory;

   public ConsolidadoRepository(DbConnectionFactory factory)
   {
       _factory = factory;
   }


    

    public async Task AtualizarSaldo(DateTime data, decimal valor)
    {
        using var conn = _factory.Create();

        var sql = @"
        INSERT INTO consolidado_diario (data, saldo, atualizado_em)
        VALUES (@Data, @Valor, CURRENT_TIMESTAMP)
        ON CONFLICT (data)
        DO UPDATE SET
            saldo         = consolidado_diario.saldo + @Valor,
            atualizado_em = CURRENT_TIMESTAMP";

        await conn.ExecuteAsync(sql, new { Data = data.Date, Valor = valor });
    }

    public async Task<decimal> ObterSaldo(DateTime data)
    {
        using var conn = _factory.Create();

        var sql = "SELECT COALESCE(saldo,0) FROM consolidado_diario WHERE data = @Data";

        return await conn.ExecuteScalarAsync<decimal>(sql, new { Data = data.Date });
    }
}

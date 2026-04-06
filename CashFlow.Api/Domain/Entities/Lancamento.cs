namespace CashFlow.Api.Domain.Entities;

public enum TipoLancamento
{
   Credito,
   Debito
}

public class Lancamento
{
   public Guid Id { get; set; } = Guid.NewGuid();
   public decimal Valor { get; set; }
   public TipoLancamento Tipo { get; set; }
   public DateTime Data { get; set; } = DateTime.UtcNow;
}

namespace CashFlow.Api.Domain.Entities;

public class OutboxEvent
{
    public Guid     Id          { get; set; }
    public string   TipoEvento  { get; set; } = string.Empty;
    public string   Payload     { get; set; } = string.Empty;
    public bool     Processado  { get; set; }
    public int      Tentativas  { get; set; }
    public DateTime CriadoEm   { get; set; }
}
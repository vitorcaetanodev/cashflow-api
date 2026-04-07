

    public interface IConsolidadoRepository
    {
        Task AtualizarSaldo(DateTime data, decimal valor);
        Task<decimal> ObterSaldo(DateTime data);
    }
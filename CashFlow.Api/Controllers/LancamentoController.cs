using Microsoft.AspNetCore.Mvc;
using CashFlow.Api.Domain.Entities;
using CashFlow.Api.Application;
using CashFlow.Api.Infrastructure.Repositories;

namespace CashFlow.Api.Controllers;

[ApiController]
[Route("lancamentos")]
public class LancamentoController : ControllerBase
{
    private readonly LancamentoService _service;
    private readonly LancamentoRepository _repo;

    public LancamentoController(LancamentoService service, LancamentoRepository repo)
    {
        _service = service;
        _repo = repo;
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] Request req)
    {
        await _service.Criar(req.Valor, req.Tipo);
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var lista = await _repo.ObterTodos();
        return Ok(lista);
    }

    [HttpGet("relatorio")]
    public async Task<IActionResult> Relatorio()
    {
        var lista = await _repo.ObterTodos();

        var totalCredito = lista
            .Where(x => x.Tipo == TipoLancamento.Credito)
            .Sum(x => x.Valor);

        var totalDebito = lista
            .Where(x => x.Tipo == TipoLancamento.Debito)
            .Sum(x => x.Valor);

        return Ok(new
        {
            Creditos = totalCredito,
            Debitos = totalDebito,
            Saldo = totalCredito - totalDebito
        });
    }

    public class Request
    {
        public decimal Valor { get; set; }
        public TipoLancamento Tipo { get; set; }
    }
}
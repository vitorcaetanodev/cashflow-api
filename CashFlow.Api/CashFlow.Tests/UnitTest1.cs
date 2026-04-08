using Xunit;
using Moq;
using System;
using System.Threading.Tasks;
using CashFlow.Api.Domain.Entities;
using CashFlow.Api.Infrastructure.Repositories;
using CashFlow.Api.Application;
using Microsoft.Extensions.Logging;

namespace CashFlow.Tests;

public class UnitTest1
{
    private readonly Mock<ILancamentoRepository> _repoMock;
    private readonly Mock<IOutboxRepository> _outboxMock;
    private readonly Mock<ILogger<LancamentoService>> _loggerMock;
    private readonly Mock<IKafkaProducer> _producerMock;

    private readonly LancamentoService _service;

    public UnitTest1()
    {
        _repoMock = new Mock<ILancamentoRepository>();
        _outboxMock = new Mock<IOutboxRepository>();
        _loggerMock = new Mock<ILogger<LancamentoService>>();
        _producerMock = new Mock<IKafkaProducer>();

        _service = new LancamentoService(
            _repoMock.Object,
            _loggerMock.Object,
            _producerMock.Object
        );
    }

    [Fact]
    public async Task Criar_DeveExecutarSemErro_ComDadosValidos()
    {
        await _service.Criar(100m, TipoLancamento.Credito);

        _repoMock.Verify(r => r.Inserir(It.IsAny<Lancamento>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Criar_ComValorInvalido_DeveLancarExcecao(decimal valor)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.Criar(valor, TipoLancamento.Credito));
    }
}
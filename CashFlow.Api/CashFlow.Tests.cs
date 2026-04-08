// ============================================================
// CATEGORIA 1 — TESTES
// xUnit + Moq + FluentAssertions + Testcontainers
// dotnet add package xunit
// dotnet add package Moq
// dotnet add package FluentAssertions
// dotnet add package Testcontainers.PostgreSql
// ============================================================

using System;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using Domain.Entities;
using Infrastructure.Repositories;
using Application.Services;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Dapper;
using Npgsql;

namespace CashFlow.Tests;

// ─────────────────────────────────────────────
// 1.1 TESTES UNITÁRIOS — LancamentoService
// ─────────────────────────────────────────────

public class LancamentoServiceTests
{
    private readonly Mock<ILancamentoRepository> _repoMock;
    private readonly Mock<IOutboxRepository>     _outboxMock;
    private readonly Mock<ILogger<LancamentoService>> _loggerMock;
    private readonly LancamentoService _service;

    public LancamentoServiceTests()
    {
        _repoMock   = new Mock<ILancamentoRepository>();
        _outboxMock = new Mock<IOutboxRepository>();
        _loggerMock = new Mock<ILogger<LancamentoService>>();
        _service    = new LancamentoService(_repoMock.Object, _outboxMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Criar_DeveInserirLancamentoEEventoOutbox()
    {
        // Arrange
        _repoMock.Setup(r => r.InserirComOutbox(It.IsAny<Lancamento>(), It.IsAny<OutboxEvent>()))
                 .Returns(Task.CompletedTask);

        // Act
        await _service.Criar(100m, TipoLancamento.Credito);

        // Assert
        _repoMock.Verify(r => r.InserirComOutbox(
            It.Is<Lancamento>(l => l.Valor == 100m && l.Tipo == TipoLancamento.Credito),
            It.Is<OutboxEvent>(e => e.TipoEvento == "LancamentoCriado")
        ), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public async Task Criar_ComValorInvalido_DeveLancarException(decimal valorInvalido)
    {
        // Act
        Func<Task> act = () => _service.Criar(valorInvalido, TipoLancamento.Credito);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*valor*");
    }

    [Fact]
    public async Task Criar_QuandoRepositorioFalha_DevePropagarExcecao()
    {
        // Arrange
        _repoMock.Setup(r => r.InserirComOutbox(It.IsAny<Lancamento>(), It.IsAny<OutboxEvent>()))
                 .ThrowsAsync(new Exception("DB connection failed"));

        // Act
        Func<Task> act = () => _service.Criar(100m, TipoLancamento.Debito);

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("*DB connection*");
    }
}

// ─────────────────────────────────────────────
// 1.2 TESTES UNITÁRIOS — ConsolidadoService
// ─────────────────────────────────────────────

public class ConsolidadoServiceTests
{
    private readonly Mock<IConsolidadoRepository> _repoMock;
    private readonly Mock<IEventosProcessadosRepository> _idempotenciaMock;
    private readonly ConsolidadoEventHandler _handler;

    public ConsolidadoServiceTests()
    {
        _repoMock         = new Mock<IConsolidadoRepository>();
        _idempotenciaMock = new Mock<IEventosProcessadosRepository>();
        _handler = new ConsolidadoEventHandler(_repoMock.Object, _idempotenciaMock.Object);
    }

    [Fact]
    public async Task ProcessarEvento_Credito_DeveAumentarSaldo()
    {
        // Arrange
        var evento = new OutboxEvent
        {
            Id         = Guid.NewGuid(),
            TipoEvento = "LancamentoCriado",
            Payload    = """{"valor":200,"tipo":"CREDITO","data":"2026-01-15"}"""
        };

        _idempotenciaMock.Setup(i => i.JaProcessado(evento.Id)).ReturnsAsync(false);

        // Act
        await _handler.Processar(evento);

        // Assert
        _repoMock.Verify(r => r.AtualizarSaldo(
            It.Is<DateTime>(d => d.Date == new DateTime(2026, 1, 15)),
            200m
        ), Times.Once);
    }

    [Fact]
    public async Task ProcessarEvento_Debito_DeveSubtrairSaldo()
    {
        // Arrange
        var evento = new OutboxEvent
        {
            Id         = Guid.NewGuid(),
            TipoEvento = "LancamentoCriado",
            Payload    = """{"valor":50,"tipo":"DEBITO","data":"2026-01-15"}"""
        };

        _idempotenciaMock.Setup(i => i.JaProcessado(evento.Id)).ReturnsAsync(false);

        // Act
        await _handler.Processar(evento);

        // Assert
        _repoMock.Verify(r => r.AtualizarSaldo(
            It.IsAny<DateTime>(),
            -50m   // débito converte para negativo
        ), Times.Once);
    }

    [Fact]
    public async Task ProcessarEvento_Duplicado_DeveIgnorar()
    {
        // Arrange — evento já foi processado
        var eventoId = Guid.NewGuid();
        _idempotenciaMock.Setup(i => i.JaProcessado(eventoId)).ReturnsAsync(true);

        var evento = new OutboxEvent { Id = eventoId, TipoEvento = "LancamentoCriado", Payload = "{}" };

        // Act
        await _handler.Processar(evento);

        // Assert — repositório NÃO deve ser chamado
        _repoMock.Verify(r => r.AtualizarSaldo(It.IsAny<DateTime>(), It.IsAny<decimal>()), Times.Never);
    }
}

// ─────────────────────────────────────────────
// 1.3 TESTES DE INTEGRAÇÃO — PostgreSQL real
// Usa Testcontainers: sobe um container PostgreSQL
// temporário para cada classe de teste
// ─────────────────────────────────────────────

public class ConsolidadoRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("cashflow_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private DbConnectionFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        _factory = new DbConnectionFactory(_db.GetConnectionString());
        await SeedSchema();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task SeedSchema()
    {
        using var conn = new NpgsqlConnection(_db.GetConnectionString());
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS consolidado_diario (
                data         DATE PRIMARY KEY,
                saldo        NUMERIC(16,2) NOT NULL DEFAULT 0,
                atualizado_em TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
        """);
    }

    [Fact]
    public async Task AtualizarSaldo_PrimeiroLancamento_DeveCriarRegistro()
    {
        // Arrange
        var repo = new ConsolidadoRepository(_factory);
        var data = new DateTime(2026, 3, 1);

        // Act
        await repo.AtualizarSaldo(data, 500m);

        // Assert
        var saldo = await repo.ObterSaldo(data);
        saldo.Should().Be(500m);
    }

    [Fact]
    public async Task AtualizarSaldo_MultiplosLancamentos_DeveAcumular()
    {
        // Arrange
        var repo = new ConsolidadoRepository(_factory);
        var data = new DateTime(2026, 3, 2);

        // Act
        await repo.AtualizarSaldo(data, 300m);
        await repo.AtualizarSaldo(data, 200m);
        await repo.AtualizarSaldo(data, -100m); // débito

        // Assert
        var saldo = await repo.ObterSaldo(data);
        saldo.Should().Be(400m);
    }

    [Fact]
    public async Task ObterSaldo_DataSemLancamentos_DeveRetornarZero()
    {
        // Arrange
        var repo = new ConsolidadoRepository(_factory);

        // Act
        var saldo = await repo.ObterSaldo(new DateTime(2020, 1, 1));

        // Assert
        saldo.Should().Be(0m);
    }
}

// ─────────────────────────────────────────────
// 1.4 TESTES DE INTEGRAÇÃO — OutboxWorker
// ─────────────────────────────────────────────

public class OutboxWorkerIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private DbConnectionFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        _factory = new DbConnectionFactory(_db.GetConnectionString());
        await SeedSchema();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task SeedSchema()
    {
        using var conn = new NpgsqlConnection(_db.GetConnectionString());
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

            CREATE TABLE outbox_events (
                id            UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                tipo_evento   VARCHAR(100) NOT NULL,
                payload       JSONB NOT NULL,
                processado    BOOLEAN NOT NULL DEFAULT FALSE,
                tentativas    INT NOT NULL DEFAULT 0,
                criado_em     TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                processado_em TIMESTAMP
            );

            CREATE TABLE eventos_processados (
                id           UUID PRIMARY KEY,
                processado_em TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
        """);
    }

    [Fact]
    public async Task BuscarEventosPendentes_DeveRetornarApenasNaoProcessados()
    {
        // Arrange
        var repo = new OutboxRepository(_factory);
        using var conn = new NpgsqlConnection(_db.GetConnectionString());

        await conn.ExecuteAsync("""
            INSERT INTO outbox_events (tipo_evento, payload, processado)
            VALUES
                ('LancamentoCriado', '{}', false),
                ('LancamentoCriado', '{}', false),
                ('LancamentoCriado', '{}', true)   -- já processado, não deve aparecer
        """);

        // Act
        var pendentes = await repo.BuscarPendentes(limit: 100);

        // Assert
        pendentes.Should().HaveCount(2);
    }

    [Fact]
    public async Task MarcarProcessado_DeveAtualizarFlag()
    {
        // Arrange
        var repo = new OutboxRepository(_factory);
        using var conn = new NpgsqlConnection(_db.GetConnectionString());

        var id = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO outbox_events (id, tipo_evento, payload) VALUES (@Id, 'X', '{}')",
            new { Id = id }
        );

        // Act
        await repo.MarcarProcessado(id);

        // Assert
        var processado = await conn.ExecuteScalarAsync<bool>(
            "SELECT processado FROM outbox_events WHERE id = @Id", new { Id = id }
        );
        processado.Should().BeTrue();
    }
}

// ─────────────────────────────────────────────
// 1.5 TESTES DE CONTROLLER (HTTP)
// Microsoft.AspNetCore.Mvc.Testing
// dotnet add package Microsoft.AspNetCore.Mvc.Testing
// ─────────────────────────────────────────────

public class LancamentoControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public LancamentoControllerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Substituir banco por in-memory ou mock aqui se necessário
            });
        }).CreateClient();
    }

    [Fact]
    public async Task POST_Lancamento_Valido_Retorna200()
    {
        // Arrange
        var payload = new StringContent(
            """{"valor":100,"tipo":"Credito"}""",
            System.Text.Encoding.UTF8,
            "application/json"
        );

        // Act
        var response = await _client.PostAsync("/lancamentos", payload);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_Lancamento_ValorZero_Retorna400()
    {
        var payload = new StringContent(
            """{"valor":0,"tipo":"Credito"}""",
            System.Text.Encoding.UTF8,
            "application/json"
        );

        var response = await _client.PostAsync("/lancamentos", payload);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_Consolidado_Retorna200ComSaldo()
    {
        var response = await _client.GetAsync($"/consolidado/{DateTime.UtcNow:yyyy-MM-dd}");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("saldo");
    }
}

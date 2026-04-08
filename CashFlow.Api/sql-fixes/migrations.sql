-- ============================================================
-- CASHFLOW — SCRIPT COMPLETO DE BANCO DE DADOS
-- PostgreSQL 16+
-- Compatível com o código C# ASP.NET Core + Dapper
--
-- Execute na ordem: psql -U cashflow -d cashflow -f cashflow_migrations.sql
-- Ou via docker-compose: mapeie este arquivo em
--   ./cashflow_migrations.sql:/docker-entrypoint-initdb.d/01_init.sql
-- ============================================================


-- ─────────────────────────────────────────────
-- EXTENSÕES
-- ─────────────────────────────────────────────

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";


-- ─────────────────────────────────────────────
-- TABELA: lancamentos
-- Fonte da verdade de todos os lançamentos.
-- Nunca deletar ou alterar registros aqui.
--
-- Mapeamento C#:
--   Domain.Entities.Lancamento
--   Infrastructure.Repositories.LancamentoRepository
-- ─────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS lancamentos (
    id        UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    valor     NUMERIC(14,2) NOT NULL CHECK (valor > 0),
    tipo      VARCHAR(10)  NOT NULL CHECK (tipo IN ('CREDITO', 'DEBITO')),
    data      TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    criado_em TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Índice por data: consultas de lançamentos de um período
CREATE INDEX IF NOT EXISTS idx_lancamentos_data
    ON lancamentos(data);

-- Índice por tipo: relatórios separados por crédito/débito
CREATE INDEX IF NOT EXISTS idx_lancamentos_tipo
    ON lancamentos(tipo);


-- ─────────────────────────────────────────────
-- TABELA: outbox_events
-- Chave do padrão Outbox Pattern.
-- Gravada na MESMA transação que o lançamento,
-- garantindo que nenhum evento se perde.
--
-- Mapeamento C#:
--   Domain.Entities.OutboxEvent
--   Infrastructure.Repositories.OutboxRepository
-- ─────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS outbox_events (
    id            UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    tipo_evento   VARCHAR(100) NOT NULL,
    payload       JSONB        NOT NULL,
    processado    BOOLEAN      NOT NULL DEFAULT FALSE,
    tentativas    INT          NOT NULL DEFAULT 0,
    criado_em     TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    processado_em TIMESTAMP
);

-- Índice PARCIAL — indexa só eventos pendentes.
-- Muito mais eficiente que índice full na coluna processado,
-- pois a tabela cresce mas os pendentes são sempre poucos.
CREATE INDEX IF NOT EXISTS idx_outbox_pendentes
    ON outbox_events(criado_em)
    WHERE processado = FALSE AND tentativas < 5;


-- ─────────────────────────────────────────────
-- TABELA: consolidado_diario
-- Projeção de leitura (Read Model / CQRS).
-- Atualizada incrementalmente pelo OutboxWorker.
-- Consultas em O(1) — busca por chave primária (data).
--
-- Mapeamento C#:
--   Infrastructure.Repositories.ConsolidadoRepository
-- ─────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS consolidado_diario (
    data          DATE         PRIMARY KEY,
    saldo         NUMERIC(16,2) NOT NULL DEFAULT 0,
    atualizado_em TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);


-- ─────────────────────────────────────────────
-- TABELA: eventos_processados
-- Controle de idempotência.
-- Antes de processar um evento, o worker verifica
-- se o id já existe aqui. Se sim, ignora.
-- Garante que crashes entre processar e marcar
-- como processado não causem duplicação no saldo.
--
-- Mapeamento C#:
--   Infrastructure.Repositories.EventosProcessadosRepository
-- ─────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS eventos_processados (
    id            UUID      PRIMARY KEY,
    processado_em TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);


-- ─────────────────────────────────────────────
-- TABELA: dead_letter_events
-- Eventos que falharam mais de 5 vezes consecutivas.
-- O worker para de tentar e move para cá.
-- Permite análise manual e reprocessamento futuro
-- sem bloquear o fluxo normal.
--
-- Mapeamento C#:
--   Infrastructure.Repositories.OutboxRepository.MoverParaDeadLetter()
-- ─────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS dead_letter_events (
    id          UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    outbox_id   UUID         NOT NULL,
    tipo_evento VARCHAR(100) NOT NULL,
    payload     JSONB        NOT NULL,
    tentativas  INT          NOT NULL,
    erro        TEXT,
    criado_em   TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================
-- QUERIES DE REFERÊNCIA
-- Use exatamente estas queries nos repositórios C#.
-- Não altere sem revisar o código correspondente.
-- ============================================================


-- ─────────────────────────────────────────────
-- INSERT DE LANÇAMENTO + EVENTO (mesma transação)
--
-- Use em: LancamentoRepository.InserirComOutbox()
-- O BEGIN/COMMIT é gerenciado pela camada C# via IDbTransaction.
-- ─────────────────────────────────────────────

-- BEGIN;
--
-- INSERT INTO lancamentos (id, valor, tipo, data)
-- VALUES (@Id, @Valor, @Tipo, @Data);
--
-- INSERT INTO outbox_events (tipo_evento, payload)
-- VALUES (
--     'LancamentoCriado',
--     jsonb_build_object(
--         'id',    @Id::text,
--         'valor', @Valor,
--         'tipo',  @Tipo,
--         'data',  @Data
--     )
-- );
--
-- COMMIT;


-- ─────────────────────────────────────────────
-- BUSCA DE EVENTOS PENDENTES (Worker)
--
-- Use em: OutboxRepository.BuscarPendentes()
-- FOR UPDATE SKIP LOCKED garante que múltiplos
-- workers paralelos não peguem o mesmo evento.
-- ─────────────────────────────────────────────

-- SELECT id, tipo_evento, payload, tentativas
-- FROM outbox_events
-- WHERE processado  = FALSE
--   AND tentativas  < 5
-- ORDER BY criado_em
-- LIMIT 100
-- FOR UPDATE SKIP LOCKED;


-- ─────────────────────────────────────────────
-- UPSERT DE CONSOLIDADO (Worker)
--
-- Use em: ConsolidadoRepository.AtualizarSaldo()
-- @Valor é positivo para CREDITO, negativo para DEBITO
-- (conversão feita na camada de aplicação C#).
--
-- ATENÇÃO: atualizado_em DEVE ser atualizado no ON CONFLICT.
-- Omitir isso é um bug — o timestamp fica estático no valor
-- do INSERT original para sempre.
-- ─────────────────────────────────────────────

-- INSERT INTO consolidado_diario (data, saldo, atualizado_em)
-- VALUES (@Data, @Valor, CURRENT_TIMESTAMP)
-- ON CONFLICT (data)
-- DO UPDATE SET
--     saldo         = consolidado_diario.saldo + EXCLUDED.saldo,
--     atualizado_em = CURRENT_TIMESTAMP;


-- ─────────────────────────────────────────────
-- MARCAR EVENTO COMO PROCESSADO
--
-- Use em: OutboxRepository.MarcarProcessado()
-- ─────────────────────────────────────────────

-- UPDATE outbox_events
-- SET processado    = TRUE,
--     processado_em = CURRENT_TIMESTAMP
-- WHERE id = @Id;


-- ─────────────────────────────────────────────
-- INCREMENTAR TENTATIVAS (falha no worker)
--
-- Use em: OutboxRepository.IncrementarTentativas()
-- ─────────────────────────────────────────────

-- UPDATE outbox_events
-- SET tentativas = tentativas + 1
-- WHERE id = @Id;


-- ─────────────────────────────────────────────
-- MOVER PARA DEAD LETTER (após max tentativas)
--
-- Use em: OutboxRepository.MoverParaDeadLetter()
-- Executa os dois statements na mesma transação.
-- ─────────────────────────────────────────────

-- BEGIN;
--
-- INSERT INTO dead_letter_events
--     (outbox_id, tipo_evento, payload, tentativas, erro)
-- SELECT id, tipo_evento, payload, tentativas, @Erro
-- FROM outbox_events
-- WHERE id = @Id;
--
-- UPDATE outbox_events
-- SET processado    = TRUE,
--     processado_em = CURRENT_TIMESTAMP
-- WHERE id = @Id;
--
-- COMMIT;


-- ─────────────────────────────────────────────
-- VERIFICAR IDEMPOTÊNCIA
--
-- Use em: EventosProcessadosRepository.JaProcessado()
-- ─────────────────────────────────────────────

-- SELECT EXISTS (
--     SELECT 1 FROM eventos_processados WHERE id = @Id
-- );


-- ─────────────────────────────────────────────
-- REGISTRAR EVENTO PROCESSADO (idempotência)
--
-- Use em: EventosProcessadosRepository.Registrar()
-- ─────────────────────────────────────────────

-- INSERT INTO eventos_processados (id)
-- VALUES (@Id)
-- ON CONFLICT (id) DO NOTHING;


-- ─────────────────────────────────────────────
-- CONSULTAR SALDO DO DIA
--
-- Use em: ConsolidadoRepository.ObterSaldo()
-- COALESCE garante que retorna 0 se não há registro,
-- em vez de null (que quebraria o mapeamento C#).
-- ─────────────────────────────────────────────

-- SELECT COALESCE(saldo, 0)
-- FROM consolidado_diario
-- WHERE data = @Data;


-- ============================================================
-- PARTICIONAMENTO (EVOLUÇÃO FUTURA — NÃO ATIVAR AGORA)
--
-- Ativar apenas quando lancamentos > 10 milhões de linhas.
-- Requer recriar a tabela do zero — plan com cuidado.
-- Documentado aqui para referência futura.
-- ============================================================

-- DROP TABLE IF EXISTS lancamentos;
--
-- CREATE TABLE lancamentos (
--     id        UUID          NOT NULL,
--     valor     NUMERIC(14,2) NOT NULL CHECK (valor > 0),
--     tipo      VARCHAR(10)   NOT NULL CHECK (tipo IN ('CREDITO', 'DEBITO')),
--     data      TIMESTAMP     NOT NULL,
--     criado_em TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP
-- ) PARTITION BY RANGE (data);
--
-- CREATE TABLE lancamentos_2026
--     PARTITION OF lancamentos
--     FOR VALUES FROM ('2026-01-01') TO ('2027-01-01');
--
-- CREATE TABLE lancamentos_2027
--     PARTITION OF lancamentos
--     FOR VALUES FROM ('2027-01-01') TO ('2028-01-01');
--
-- ALTER TABLE lancamentos ADD PRIMARY KEY (id, data);
--
-- CREATE INDEX idx_lancamentos_data ON lancamentos(data);
-- CREATE INDEX idx_lancamentos_tipo ON lancamentos(tipo);


-- ============================================================
-- VERIFICAÇÃO FINAL
-- Rode após aplicar o script para confirmar que tudo foi criado.
-- ============================================================

-- SELECT table_name, pg_size_pretty(pg_total_relation_size(quote_ident(table_name))) AS tamanho
-- FROM information_schema.tables
-- WHERE table_schema = 'public'
-- ORDER BY table_name;

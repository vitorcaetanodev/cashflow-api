# CashFlow API

Sistema de controle de fluxo de caixa com lançamentos de débitos e créditos e consolidado diário.

---

## Como rodar localmente

### Pré-requisitos

- [Docker](https://www.docker.com/) e Docker Compose instalados
- Porta `8080` e `5432` disponíveis

### Subindo tudo com Docker

```bash
# 1. Clone o repositório
git clone https://github.com/seu-usuario/cashflow-api
cd cashflow-api

# 2. Suba toda a stack (API + Worker + Banco)
docker compose up --build

# 3. Aguarde os healthchecks passarem (~20s)
# Você verá: cashflow-api | Application started.

# 4. Acesse a API
curl http://localhost:8080/health/ready
```

### Rodando sem Docker (desenvolvimento)

```bash
# Requisito: PostgreSQL rodando localmente na porta 5432
# com banco "cashflow", usuário "cashflow", senha "cashflow123"

cd CashFlow.Api
dotnet run
```

---

## Endpoints

### Autenticação

```http
POST /auth/token
Content-Type: application/json

{"usuario": "admin", "senha": "cashflow"}
```

Resposta:
```json
{"token": "eyJ...", "expira_em": "2026-01-15T20:00:00Z"}
```

Use o token no header de todas as outras requisições:
```
Authorization: Bearer eyJ...
```

---

### Lançamentos

**Criar lançamento**
```http
POST /lancamentos
Authorization: Bearer {token}
Content-Type: application/json

{"valor": 150.00, "tipo": "Credito"}
```

Tipos válidos: `Credito`, `Debito`

**Listar lançamentos**
```http
GET /lancamentos
Authorization: Bearer {token}
```

---

### Consolidado diário

**Consultar saldo de uma data**
```http
GET /consolidado/2026-01-15
Authorization: Bearer {token}
```

Resposta:
```json
{"data": "2026-01-15", "saldo": 350.00}
```

---

### Saúde da aplicação

```http
GET /health/live    # verifica se a app está de pé
GET /health/ready   # verifica banco + worker
GET /health         # compatibilidade
GET /metrics        # métricas Prometheus (se habilitado)
```

---

## Como rodar os testes

```bash
cd CashFlow.Tests
dotnet test

# Com cobertura de código
dotnet test --collect:"XPlat Code Coverage"
```

Os testes de integração sobem um container PostgreSQL automaticamente via Testcontainers. É necessário ter o Docker rodando.

---

## Arquitetura

```
Cliente
  │
  ▼
API (ASP.NET Core 8)
  │
  ├── LancamentoController
  │     └── LancamentoService
  │           └── LancamentoRepository ──→ PostgreSQL
  │                 └── INSERT lancamento + outbox_event (transação atômica)
  │
  └── ConsolidadoController
        └── ConsolidadoRepository ──→ PostgreSQL (consolidado_diario)

OutboxWorker (BackgroundService)
  │
  └── Lê outbox_events pendentes (FOR UPDATE SKIP LOCKED)
        └── Atualiza consolidado_diario
              └── Registra em eventos_processados (idempotência)
```

### Fluxo de um lançamento

1. `POST /lancamentos` chega na API
2. Uma única transação faz `INSERT` em `lancamentos` **e** `INSERT` em `outbox_events`
3. A API retorna `200 OK` imediatamente — o serviço de lançamentos é independente
4. O `OutboxWorker` (roda em background, a cada 2s) busca eventos pendentes
5. Atualiza `consolidado_diario` com o valor (positivo para crédito, negativo para débito)
6. Registra o evento em `eventos_processados` (garantia de idempotência)
7. Marca o evento como processado em `outbox_events`

---

## Decisões arquiteturais (ADRs)

### ADR-001 — Outbox Pattern em vez de publicação direta

**Contexto:** Precisamos garantir que um lançamento salvo no banco sempre gere um evento para o consolidado, mesmo que o broker ou a rede falhe.

**Decisão:** Gravar o lançamento e o evento na mesma transação PostgreSQL. Um worker separado lê os eventos pendentes e os processa de forma assíncrona.

**Consequências:**
- ✅ Zero perda de eventos — se o lançamento foi salvo, o evento também foi
- ✅ O serviço de lançamentos não depende do serviço de consolidado
- ⚠️ Eventual consistency — o saldo pode atrasar alguns segundos

---

### ADR-002 — CQRS leve com projeção `consolidado_diario`

**Contexto:** A consulta de saldo diário recebe até 50 req/s. Calcular o saldo na hora somando todos os lançamentos do dia seria O(n) por requisição.

**Decisão:** Manter uma tabela `consolidado_diario` como projeção de leitura, atualizada incrementalmente pelo worker. Consultas são O(1) — busca por chave primária (data).

**Consequências:**
- ✅ Consultas em O(1) independente do volume de lançamentos
- ✅ Cumpre o requisito de 50 req/s com ≤5% de perda
- ⚠️ Dado é eventualmente consistente (delay de ~2s pelo worker)

---

### ADR-003 — Monólito modular em vez de microsserviços

**Contexto:** O desafio descreve dois serviços (lançamentos e consolidado). A opção natural seria dois microsserviços separados.

**Decisão:** Um monólito modular com os dois domínios bem separados internamente (namespaces, repositórios e controllers distintos). O worker roda como processo separado.

**Consequências:**
- ✅ Simplicidade operacional — menos serviços para fazer deploy e monitorar
- ✅ Deploy mais rápido para o escopo do desafio
- ✅ Mantém separação de responsabilidades
- ⚠️ Escala os dois módulos juntos — aceitável para o volume descrito

---

### ADR-004 — PostgreSQL puro em vez de Kafka

**Contexto:** O diagrama de arquitetura inicialmente incluía Kafka para a comunicação entre lançamentos e consolidado.

**Decisão:** O padrão Outbox com PostgreSQL implementa a mesma garantia de entrega sem a complexidade operacional do Kafka. Para o volume descrito (50 req/s), PostgreSQL com `FOR UPDATE SKIP LOCKED` é suficiente.

**Consequências:**
- ✅ Stack mais simples de operar e fazer deploy
- ✅ Sem overhead de Kafka para o volume atual
- ⚠️ Migração para Kafka seria necessária acima de ~1.000 eventos/s

---

## Requisitos não-funcionais

### Disponibilidade do serviço de lançamentos

O serviço de lançamentos **não depende** do consolidado. Se o worker travar ou o processamento atrasar, a API continua recebendo lançamentos normalmente. Os eventos ficam na `outbox_events` até o worker se recuperar.

### Throughput de 50 req/s

A consulta de consolidado é `SELECT ... WHERE data = $1` por chave primária — O(1). Testado com `pgbench` localmente: sustenta >200 req/s em hardware modesto.

### Tolerância a falhas

- **Worker com falha:** reiniciado automaticamente pelo Docker (`restart: unless-stopped`). Eventos pendentes ficam na fila e são processados ao reiniciar.
- **Banco indisponível:** circuit breaker (Polly) abre após 5 falhas consecutivas, evitando cascata de erros.
- **Evento duplicado:** tabela `eventos_processados` garante idempotência.
- **Evento com falha persistente:** após 5 tentativas, movido para `dead_letter_events` para análise manual.

---

## Estrutura do projeto

```
/
├── CashFlow.Api/           API ASP.NET Core
│   ├── Controllers/        Endpoints HTTP
│   ├── Application/        Serviços de aplicação
│   ├── Domain/             Entidades e interfaces
│   └── Infrastructure/     Repositórios, auth, health
│
├── CashFlow.Worker/        Outbox Worker (BackgroundService)
│
├── CashFlow.Tests/         Testes unitários e de integração
│
├── docker-compose.yml      Stack completa para rodar localmente
├── Dockerfile.Api
├── Dockerfile.Worker
└── sql-fixes/
    └── migrations.sql      Schema completo do banco
```
## Prints da Api

<img width="1857" height="643" alt="image" src="https://github.com/user-attachments/assets/e073664f-933d-4836-afe9-1ccd4dc8125c" />

Lançamento 50000 Crédito

<img width="1761" height="764" alt="image" src="https://github.com/user-attachments/assets/a8c2eca5-b23b-4889-a1d4-702e934474c9" />

Lançamento 20000 Débito 

<img width="1780" height="853" alt="image" src="https://github.com/user-attachments/assets/901f5eb0-30d8-4b96-bba5-6925763603e9" />

Lançamentos Get

<img width="1781" height="724" alt="image" src="https://github.com/user-attachments/assets/0d8e56e1-20a1-493e-bbd4-24fd72cd9f2f" />

Relatórios de Lançamentos Saldo

<img width="1774" height="775" alt="image" src="https://github.com/user-attachments/assets/4a4a9b1a-7ef9-4cab-b4b1-f9fb6be33207" />

---

## Variáveis de ambiente

| Variável | Descrição | Padrão |
|---|---|---|
| `ConnectionStrings__Default` | Connection string do PostgreSQL | — |
| `Jwt__Key` | Chave secreta para assinar tokens JWT | — |
| `Jwt__Issuer` | Issuer do JWT | `cashflow-api` |
| `Jwt__Audience` | Audience do JWT | `cashflow-client` |
| `Auth__Usuario` | Usuário para gerar token | `admin` |
| `Auth__Senha` | Senha para gerar token | — |
| `Worker__IntervalMs` | Intervalo do worker em ms | `2000` |
| `Worker__BatchSize` | Eventos por ciclo | `100` |
| `Worker__MaxRetries` | Máximo de tentativas antes de dead letter | `5` |

---

## Evoluções futuras

Se o sistema crescer além do volume atual, as próximas evoluções seriam:

1. **Kafka:** substituir o Outbox Worker por um producer/consumer Kafka quando o volume ultrapassar ~1.000 eventos/s
2. **Redis:** adicionar cache de leitura no consolidado para reduzir carga no banco em picos
3. **Particionamento:** habilitar o particionamento da tabela `lancamentos` por data quando ultrapassar ~10M de registros
4. **OpenTelemetry completo:** adicionar tracing distribuído e exportar métricas para Grafana/Datadog
5. **Autenticação robusta:** substituir a autenticação simples por OAuth2/OIDC com refresh tokens

---

## Tecnologias

- **ASP.NET Core 8** — API web
- **PostgreSQL 16** — banco de dados
- **Dapper** — acesso ao banco (leve, sem ORM pesado)
- **Polly** — retry e circuit breaker
- **Serilog** — logs estruturados
- **xUnit + Moq + FluentAssertions** — testes
- **Testcontainers** — testes de integração com banco real
- **Docker Compose** — orquestração local

# Operacoes

API que registra e valida operações financeiras declaradas pelo usuário (os fatos de
negociação). Operações não executam ordens, não consultam preços de mercado — a UI fala
direto com este serviço, nunca com a infraestrutura (Hub de Preços). Ver
[`../plataforma-docs/ARQUITETURA.md`](../plataforma-docs/ARQUITETURA.md) (§6) para o
desenho completo da camada de operações e (§6.1) para a validação em camadas.

> Solução .NET 8 em Clean Architecture (`Operacoes.Domain`, `Operacoes.Application`,
> `Operacoes.Infrastructure`, `Operacoes.API`), seguindo os padrões catalogados em
> [`PADROES.md`](PADROES.md) — herdados do repo `tesouro-direto-api` e compartilhados com
> `hub-precos`.

## Estado atual

A API sobe, aplica migration no boot e responde health/metrics/swagger. **Não existe
funcionalidade de negócio implantada ainda.** Este é um esqueleto de infraestrutura — a
caneta está na fila (`docs/ROADMAP.md`). A estrutura de banco, autenticação e observabilidade
seguem o padrão do hub; adicionar operações de negócio será seguir o molde já consolidado.

## Rodar com Docker (caminho padrão)

Sobe banco e API conectados entre si, sem precisar de SDK .NET local.

```bash
cp .env.example .env   # preencha OPERACOES_APP_PASSWORD e OPERACOES_API_KEY
docker compose up -d
curl -sf http://127.0.0.1:5081/health/ready && echo OK
```

O compose falha o boot se `OPERACOES_APP_PASSWORD` ou `OPERACOES_API_KEY` estiverem vazios
(`${VAR:?}`). São dois segredos com papéis diferentes:

- `OPERACOES_APP_PASSWORD` é a senha da role de aplicação `operacoes`: o serviço `db` a usa
  para provisionar a role (hook `infra/postgres/initdb/01-provision-operacoes.sh`, ver
  [`infra/postgres/README.md`](infra/postgres/README.md)), e o serviço `operacoes` a usa para
  montar `ConnectionStrings__DefaultConnection` (`Host=db;Port=5432`, a rede interna do
  compose) — a mesma senha nos dois lados, senão a autenticação no Postgres falha.
- `OPERACOES_API_KEY` é a chave que o Operacoes **exige** de quem o chama: todo request a
  `/v1/*` precisa do header `X-Api-Key` com esse valor, ou recebe `401`
  (`src/Operacoes.API/Middleware/ApiKeyMiddleware.cs`). Ela chega ao container como
  `ApiKey__Key`. Precisa ter no mínimo 32 caracteres — `ApiKeyGuard` recusa o boot em
  `Production` com uma chave mais curta; gere uma com `openssl rand -hex 32`.

Portas locais (ver `docker-compose.yml`):

- `operacoes` publicado em `http://127.0.0.1:5081` — Swagger em `http://127.0.0.1:5081/swagger`.
- `db` publicado em `127.0.0.1:5434` — serve para `psql` e para o `dotnet run` local
  (abaixo), não para o `operacoes` do compose (que fala com `db` pela rede interna, porta 5432).

Esse caminho não usa `dotnet user-secrets` em nenhum momento; as credenciais chegam só
por variável de ambiente.

## Rodar local para desenvolver (`dotnet run`)

Ciclo rápido de edição/depuração, sem rebuildar imagem a cada mudança. Precisa do
.NET 8 SDK instalado.

```bash
# 1. Só o banco, via compose
cp .env.example .env   # preencha OPERACOES_APP_PASSWORD, se ainda não fez
docker compose up -d db

# 2. Credencial da API via user-secrets (mesma senha do passo 1)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5434;Database=operacoes;Username=operacoes;Password=<mesma-senha-do-.env>" \
  --project src/Operacoes.API

# 3. Rodar
dotnet run --project src/Operacoes.API
curl -sf http://localhost:5180/health/ready && echo OK
```

Swagger em `http://localhost:5180/swagger`.

`src/Operacoes.API/appsettings.json` só guarda host/porta/database — nunca a credencial
(`PADROES.md` §6) — por isso o passo 2 é obrigatório: sem ele o boot falha rápido com
uma mensagem indicando exatamente esse comando (`ConnectionStringGuard`, ver
`src/Operacoes.API/Extensions/ConnectionStringGuard.cs`), em vez de um erro de autenticação
confuso do Npgsql. **A armadilha**: `dotnet user-secrets` só é lido quando
`ASPNETCORE_ENVIRONMENT=Development`, e é `src/Operacoes.API/Properties/launchSettings.json`
quem define isso para o `dotnet run` — sem esse arquivo (ou rodando a API de outro
jeito, sem essa variável), o secret configurado no passo 2 é ignorado em silêncio e o
boot falha por falta de credencial mesmo com o secret salvo.

**`ApiKey:Key` não precisa de user-secrets em `Development`:** `ApiKeyGuard` (ver
`src/Operacoes.API/Extensions/ApiKeyGuard.cs`) recusa o boot fora de `Development`/`Testing`
por chave vazia, por conter um placeholder conhecido ou por ter menos de 32 caracteres —
nenhuma dessas três checagens roda em `Development`/`Testing` — em `dotnet run` local a
API sobe mesmo sem configurar nada, mas todo request a `/v1/*` continua exigindo o header
`X-Api-Key` (`ApiKeyMiddleware` roda em todo ambiente, guarda de boot ou não). Como
`appsettings.json` commita a chave vazia, qualquer requisição autenticada localmente
falha até você configurar uma — mais simples via user-secrets:

```bash
dotnet user-secrets set "ApiKey:Key" "uma-chave-qualquer-para-dev" --project src/Operacoes.API
curl -sf -H "X-Api-Key: uma-chave-qualquer-para-dev" http://localhost:5180/health/ready
```

`dotnet run` sobe em `http://localhost:5180` — porta fixa conforme
`src/Operacoes.API/Properties/launchSettings.json`.

## Quando usar cada caminho

- **Docker (`docker compose up -d`)** — mais perto de produção (mesma imagem, mesma
  forma de receber credencial por env var); não precisa de SDK .NET instalado; toda
  mudança de código exige rebuild da imagem (`docker compose up -d --build`).
- **`dotnet run`** — ciclo rápido de edição/depuração local; exige SDK .NET e o passo
  de `user-secrets`; só o `db` roda em container.

## Estrutura da solução

| Projeto | Papel |
|---------|-------|
| `Operacoes.Domain` | Entidades, Value Objects e erros de domínio (`Result`/`Error`). Zero dependências externas. |
| `Operacoes.Application` | Casos de uso via MediatR (commands/queries) e interfaces de porta; `LoggingBehavior` no pipeline. |
| `Operacoes.Infrastructure` | EF Core (escrita/migrations) e, no futuro, Dapper (leitura), clients externos e jobs. |
| `Operacoes.API` | Minimal API — endpoints finos, middleware (correlation id), Swagger, health checks, métricas. |

## Padrões

Este repo segue os padrões catalogados em [`PADROES.md`](PADROES.md), herdados do
repo de referência [`../tesouro-direto-api`](../tesouro-direto-api) — antes de criar
qualquer estrutura nova (endpoint, repositório, job), localize o equivalente lá e
siga o molde.

## Autenticação

Todo request sob `/v1/*` exige o header `X-Api-Key` com o valor configurado em
`ApiKey:Key` (`ApiKey__Key` por variável de ambiente); sem ele, ou com valor errado,
a resposta é `401` em `application/problem+json`, indistinguível entre "sem chave" e
"chave errada" — não dá para descobrir por tentativa se uma chave existe.
`/health`, `/health/ready`, `/health/live`, `/metrics` e `/swagger` são isentos
(`ApiKey:ExcludedPaths`). A comparação é em tempo constante
(`CryptographicOperations.FixedTimeEquals` sobre SHA-256), para não vazar por
temporização se uma chave começa certa. Ver
`src/Operacoes.API/Middleware/ApiKeyMiddleware.cs`.

Fora de `Development`/`Testing`, o boot falha se `ApiKey:Key` estiver vazia, contiver
um placeholder conhecido (`CHANGE-ME-IN-PRODUCTION`, `dev-local-key`,
`uma-chave-qualquer-para-dev`, comparado ignorando separador e maiúsculas/minúsculas) ou
tiver menos de 32 caracteres (`src/Operacoes.API/Extensions/ApiKeyGuard.cs`) — uma chave
esquecida, curta demais ou deixada no placeholder de exemplo nunca vira "sem autenticação"
em silêncio. Gere uma chave forte com `openssl rand -hex 32`.

## Banco de dados

Ver [`infra/postgres/README.md`](infra/postgres/README.md) — provisionamento da role
`operacoes`, os dois caminhos de execução do SQL e a armadilha do volume já inicializado.

## Testes

Execute os testes com:

```bash
dotnet test
```

Cobertura de código deve estar acima do gate configurado em `src/Operacoes.API/` — ver
`coverage.opencover.xml` gerado no build.

## Mensageria

O broker `plataforma-rabbitmq` é compartilhado com outros serviços, não sobe neste
`docker-compose.yml`. Para rodar localmente com o `dotnet run` e ter o broker disponível:

```bash
# Uma vez, criar a rede compartilhada da plataforma
docker network create plataforma

# Depois, subir o broker do repositório hub-precos
docker compose -f ../hub-precos/docker-compose.yml up -d plataforma-rabbitmq
```

## Referências

- [`PADROES.md`](PADROES.md) — padrões de código herdados de `tesouro-direto-api`
- [`LEIA-ME-KIT.md`](LEIA-ME-KIT.md) — critério de pronto das fases e armadilhas de infraestrutura
- [`docs/ROADMAP.md`](docs/ROADMAP.md) — fila de tarefas deste serviço
- [`../plataforma-docs/ARQUITETURA.md`](../plataforma-docs/ARQUITETURA.md) — desenho completo da plataforma
- [`../hub-precos/README.md`](../hub-precos/README.md) — molde de implementação funcional (já com negócio)

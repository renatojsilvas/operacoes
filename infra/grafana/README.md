# Dashboard e alertas do Operações

`dashboards/operacoes.json` — painel do Grafana Cloud para este serviço.
`cloud/rules-operacoes.yaml` — regras de alerta do Operações, publicadas no Grafana Cloud.

## Por que os arquivos moram aqui e são aplicados de outro repo

Log é *push*: quem inicia a conexão é a própria aplicação, então ela só precisa saber o
endereço do destino (`Loki__Uri=http://alloy:3100`) — resolve inteiro neste repo.
**Métrica é *pull*: quem inicia é o coletor.** O Alloy do `tesouro-direto-api` é quem
abre a conexão para raspar `/metrics` deste serviço, então é ele quem precisa saber o
endereço do alvo (`operacoes-app:8080`) — e o coletor mora no repo vizinho, não aqui.
Por isso o dashboard e as regras deste serviço só viram observabilidade de verdade depois
de quatro edições em `../tesouro-direto-api` (ver `LEIA-ME-KIT.md`, seção "No repo do
`tesouro-direto`"):

1. alvo do scrape em `infra/alloy/config.alloy`, com `job="operacoes"`;
2. dashboard em `infra/grafana/dashboards/operacoes.json`;
3. **o nome do dashboard e do arquivo de regras citados no `apply-cloud.sh`** — copiar o
   JSON/YAML para lá não basta, o publicador só aplica o que está na lista;
4. regras de alerta em `infra/grafana/cloud/rules-operacoes.yaml` (nunca `rules.yaml`:
   esse nome já é das 21 regras do TD, e o PUT do publicador as sobrescreveria).

Mas o dashboard e as regras **descrevem o Operações**, então é aqui — neste repo — que
devem ser versionados: quem muda uma métrica do Operações tem que ver o painel (ou o
alerta) quebrar no mesmo diff. Quem publica é o
`scripts/grafana-cloud/apply-cloud.sh`, que vive no `tesouro-direto-api` — ele lê
`infra/grafana/` daquele repo e converge por API (inclusive apagando da nuvem o que sai
da fonte). Enquanto não houver um mecanismo de publicação próprio, aplicar exige copiar
os arquivos para lá, sempre que um deles mudar aqui:

```
cp infra/grafana/dashboards/operacoes.json ../tesouro-direto-api/infra/grafana/dashboards/
cp infra/grafana/cloud/rules-operacoes.yaml ../tesouro-direto-api/infra/grafana/cloud/
cd ../tesouro-direto-api && ./scripts/grafana-cloud/apply-cloud.sh
```

Duplicação consciente, e o custo é real: as duas cópias divergem em silêncio se só uma
for editada. Ao mexer nestes arquivos, copie de novo e rode o `apply-cloud.sh` — ou o
painel/alerta na nuvem descreve uma versão que não existe mais. O `apply-cloud.sh`
precisa ser invocado com `GC_GRAFANA_URL`, `GC_GRAFANA_TOKEN` e `TELEGRAM_BOT_TOKEN`
**exportados na invocação** — ele não lê o `.env` do serviço.

**O nome do arquivo de regras já nasce `rules-operacoes.yaml` aqui** (diferente do
`hub-precos`, cujo arquivo próprio se chama `cloud/rules.yaml` e só ganha o sufixo
`-hub` ao ser copiado para o repo vizinho). Decisão deliberada: este repo descreve um
único serviço, sem risco de colisão de nome local, então manter o mesmo nome nas duas
pontas evita um `cp` com rename e a chance de esquecer o sufixo. O que importa —
`rules-operacoes.yaml` e nunca `rules.yaml` no repo vizinho — está garantido porque o
nome já nasce certo.

## O que o dashboard mostra hoje

Pós-F4, o Operações tem `POST /operacoes` (F3), outbox e relay para o RabbitMQ (F4). O
`operacoes.json` tem 14 painéis: os 10 de infraestrutura de sempre —

`Target`, `Uptime do processo`, `Health checks`, `Requisições em andamento`,
`Requisições por status`, `Latência (p95 / p50)`, `Pool de conexões Postgres`,
`Memória`, `CPU`, `Coletas de lixo por geração`

— mais os 4 de negócio que o F4 trouxe, sobre as métricas `operacoes_outbox_*` e
`operacoes_relay_*` emitidas por `RelayOutboxJob`/`BusinessMetrics`:

`Backlog da outbox`, `Idade do backlog mais antigo`, `Ciclos de relay por desfecho`,
`Eventos publicados no broker`.

Cada painel novo só entrou no mesmo diff que a métrica que ele lê — copiar um painel
sem métrica real por trás cria um painel permanentemente vazio, o mesmo modo de falha
silenciosa que o `apply-cloud.sh` já documenta para o dashboard `load-test-k6` (ver
comentário lá).

Dois painéis carregam contexto que não é óbvio pelo número (mesma nota do Hub, com a
diferença real deste serviço):

- **Pool de conexões** — o teto é 5 (`Operacoes.Infrastructure/DependencyInjection.cs`,
  `NpgsqlMaxPoolSize`), por decisão de ORÇAMENTO: em produção o Operações conecta no
  cluster Postgres COMPARTILHADO (`tesouro-direto-db`, ver
  `docker-compose.prod.yml`), dividido com `td_api`, `custodia` e `hub-precos`.
  Encostar no teto é motivo para rever o orçamento do cluster, não só para subir o
  número.
- **Memória** — diferente do Hub (que não tem limite), o container do Operações **tem**
  teto (192MB hoje, `docker-compose.prod.yml`, `deploy.resources.limits.memory` +
  `memswap_limit`). Crescimento sustentado aqui derruba o próprio Operações primeiro
  (OOM do container) — mas o runtime .NET por padrão não enxerga esse teto (PADROES
  §10.12), então o painel mostra o consumo visto de DENTRO do processo, não o que o
  cgroup aplicaria por fora.

## Regras de alerta (`cloud/rules-operacoes.yaml`)

Quatro regras, grupo `operacoes-alertas`, pasta `Operacoes`:

- **Operações — App down** (`operacoes-app-down`): `up{job="operacoes"} == 0`,
  `for: 2m`, `noDataState: Alerting`. Mesma forma de `td-app-down` (repo
  `tesouro-direto-api`, `rules.yaml`) — `up == 0`, não `absent()`, porque o alvo já
  está declarado em `infra/alloy/config.alloy`; o que este alerta vigia é o alvo parar
  de responder. `noDataState: Alerting` porque a série pode sumir por completo se o
  alvo for removido do scrape ou o container renomeado, e isso também precisa soar.
- **Operações — Backlog da outbox envelhecido** (`operacoes-outbox-backlog-velho`):
  `max(operacoes_outbox_pendente_mais_antiga_segundos{job="operacoes"}) > 900`,
  `for: 5m`, `noDataState: Alerting`. Mesma forma de `hub-outbox-backlog-velho` (repo
  `hub-precos`) — idade, não contagem, porque backlog transitório é normal (o relay
  drena a cada 5s); backlog VELHO (15 minutos de folga sobre essa cadência) é o
  sintoma real. `noDataState: Alerting` porque a métrica só é gravada quando um ciclo
  do `RelayOutboxJob` termina com sucesso; ausência prolongada significa que o relay
  nunca conseguiu drenar desde o boot.
- **Operações — Relay outbox falhando persistentemente**
  (`operacoes-relay-falha-persistente`):
  `increase(operacoes_relay_ciclos_total{job="operacoes",outcome="failure"}[5m]) > 30`,
  `for: 5m`, `noDataState: OK`. Existe porque o alerta de idade do backlog não cobre o
  caso em que o próprio ciclo falha (o gauge de idade não é atualizado nesse caminho e
  CONGELA). `noDataState: OK` porque o desfecho do ciclo é registrado
  incondicionalmente a cada execução; ausência de dado aqui é "app não está rodando",
  já coberto por `operacoes-app-down`. A descrição da regra distingue
  `Outbox.PublicacaoRejeitada` (broker vivo, fila destino rejeitou) de
  `Outbox.BrokerIndisponivel` (broker fora do ar/inalcançável) — só o segundo caso
  aponta para o `hub-precos`.
- **Operações — DB/readiness down** (`operacoes-db-readiness-down`):
  `aspnetcore_healthcheck_status{job="operacoes",name="AppDbContext"} == 0`, `for: 1m`,
  `noDataState: Alerting`. Mesma forma de `td-db-readiness-down`. A métrica só é
  publicada quando algo chama `/health*` — em produção quem garante isso 24/7 é o
  healthcheck do próprio `docker-compose.prod.yml` (curl em `/health/ready` a cada
  30s), não o scrape do Alloy (que roda a cada 30s também, mas por um caminho
  diferente). `noDataState: Alerting` pelo mesmo motivo da regra acima.

Sem `contactpoints.yaml` nem `policies.yaml` neste repo, pelo mesmo motivo do Hub: quem
define o roteamento do Telegram é o repo de referência. Lá existe um terceiro contact
point para o MESMO bot e MESMO chat id — `telegram-operacoes` — diferindo só no
`message`, que prefixa a origem (🟢 TESOURO DIRETO / 🔵 HUB DE PRECOS / 🟠 OPERACOES).
O `policies.yaml` de lá ganhou uma rota FILHA casando `service = operacoes` →
`telegram-operacoes`; a raiz e a rota do Hub continuam byte a byte iguais a antes.

**O label `service: operacoes` das quatro regras acima virou contrato** — é ele que a
rota filha casa no repo de referência. Quem remover ou renomear esse label aqui quebra
o roteamento do lado de lá, sem erro visível na hora — o YAML continua válido, o
`apply-cloud.sh` continua aplicando com sucesso, só o Telegram passa a rotular errado.
O modo de falha, como no Hub, **não é silêncio**: o roteamento do Alertmanager cai para
a rota raiz quando nenhuma rota filha casa, então o alerta ainda chega — só pelo
`telegram-tesouro`, com o prefixo errado. Vale saber disso antes de sair caçando alerta
sumido.

## Procedimento de publicação (resumo)

1. Edite `dashboards/operacoes.json` e/ou `cloud/rules-operacoes.yaml` aqui.
2. Copie os dois para `../tesouro-direto-api/infra/grafana/{dashboards,cloud}/` (ver
   comandos acima).
3. Do `tesouro-direto-api`, exporte `GC_GRAFANA_URL`, `GC_GRAFANA_TOKEN` e
   `TELEGRAM_BOT_TOKEN` e rode `./scripts/grafana-cloud/apply-cloud.sh`.
4. Confira a saída: o script conta as regras por pasta e reconsulta cada dashboard para
   garantir que os datasources resolveram — falha alta (`ABORTADO`) se algo não bateu.
5. Rode `./scripts/verificar-f1.sh` neste repo para conferir a fiação (alvo do scrape,
   dashboard e regras citados no repo vizinho) — não substitui o passo 4, cobre o "os
   arquivos existem e estão referenciados", não o "a publicação na nuvem funcionou".

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

## O que o dashboard mostra hoje, e por que não tem mais

O F1 do Operações é um esqueleto: **não há endpoint de negócio, ingestão, outbox nem
relay** (ver `ROADMAP.md`). Por isso o `operacoes.json` só tem os 10 painéis de
infraestrutura que têm métrica real por trás (confirmados raspando `/metrics` da
aplicação rodando, não copiados de memória do dashboard do Hub):

`Target`, `Uptime do processo`, `Health checks`, `Requisições em andamento`,
`Requisições por status`, `Latência (p95 / p50)`, `Pool de conexões Postgres`,
`Memória`, `CPU`, `Coletas de lixo por geração`.

Os ~9 painéis de negócio do `hub-precos.json` (frescor de ingestão, backlog da outbox,
ciclos de relay etc.) **não têm equivalente aqui** — as métricas `hub_ingestao_*`,
`hub_outbox_*` e `hub_relay_*` não existem no Operações porque o código que as emitiria
ainda não existe. Copiá-los criaria painéis permanentemente vazios, o mesmo modo de
falha silenciosa que o `apply-cloud.sh` já documenta para o dashboard `load-test-k6`
(ver comentário lá). Conforme fases futuras (F2+) adicionarem ingestão/outbox/relay ao
Operações, os painéis correspondentes entram aqui no mesmo diff que adicionar a
métrica — não antes.

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

Duas regras, grupo `operacoes-alertas`, pasta `Operacoes` — deliberadamente o mínimo, não
um porte 1:1 do Hub. Sem tráfego de negócio (nenhum endpoint além de
`/health`/`/metrics`/`/swagger`), uma regra de taxa de erro 5xx ou de latência ficaria
sem dado o tempo todo — não é alerta, é ruído silencioso à espera de significado:

- **Operações — App down**: `up{job="operacoes"} == 0`, `for: 2m`,
  `noDataState: Alerting`. Mesma forma de `td-app-down` (repo `tesouro-direto-api`,
  `rules.yaml`) — `up == 0`, não `absent()`, porque o alvo já está declarado em
  `infra/alloy/config.alloy`; o que este alerta vigia é o alvo parar de responder.
  `noDataState: Alerting` porque a série pode sumir por completo se o alvo for removido
  do scrape ou o container renomeado, e isso também precisa soar.
- **Operações — DB/readiness down**:
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

**O label `service: operacoes` das duas regras acima virou contrato** — é ele que a
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

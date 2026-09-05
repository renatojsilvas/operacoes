# Comece aqui — operacoes

Repo preparado por `scripts/novo-repo.sh` do `hub-precos` em 2026-09-05.
O kit, a infra e o CI ja vieram **com as guardas da secao 10 do PADROES**, que o repo
de referencia nao tem. Falta o que um script nao pode fazer.

## 1. Leia antes de despachar o primeiro executor

- `PADROES.md` secao 10 — cada item nasceu de um incidente real.
- `LEIA-ME-KIT.md`, secao **"O que o F1 tem que alcancar"** — o criterio de pronto
  desta primeira fase. Ele **nao** termina quando compila.
- `LEIA-ME-KIT.md`, secao **"Erros de orquestracao"** — se voce vai conduzir os
  agents, e ali que voce vai errar.

## 2. Ainda por fazer neste repo

- [x] `git init` e primeiro commit; criar o repo no GitHub. Confirmado: `git log`
      mostra `a93e750` (first commit) e `18e730d` (kit); `gh repo view
      renatojsilvas/operacoes` resolve o repo remoto.
- [x] Clonar o molde como irmao: `../hub-precos` (o molde) e `../tesouro-direto-api`
      (referencia secundaria) existem os dois em disco, ao lado deste repo.
- [x] **Portar o CODIGO do molde** (nao deste repo): os 4 projetos existem em `src/`
      (`Operacoes.API/Application/Domain/Infrastructure`), com `Result`/`IResult`
      (`Domain/Common`), `ResultExtensions` e `SerilogExtensions` (`API/Extensions`),
      `ApiKeyMiddleware` e `CorrelationIdMiddleware` (`API/Middleware`). Confirmado por
      `find src -iname "*Result*" -o -iname "*ApiKey*" -o -iname "*Correlation*"`.
      **Nota:** `MapReadGet`/`ReadEndpointExtensions` ainda **não** existe — e não
      deveria: este F1 não tem endpoint de leitura de negócio (`GET /v1/instruments`
      equivalente só nasce no F5). Portar o helper sem um GET que o use seria molde sem
      consumidor; confirme de novo ao abrir o F5.
- [x] Copiar `tests/*.Architecture.Tests/` do `hub-precos` — `tests/
      Operacoes.Architecture.Tests/` existe com `CodeConventionTests.cs`,
      `DependencyTests.cs`, `DomainConventionTests.cs`,
      `ExceptionHandlingConventionTests.cs` (o controle positivo da §10.8).
- [x] `Operacoes.sln` e os csproj. Confirmado: `Operacoes.sln` na raiz, 9 `.csproj`
      (4 em `src/`, 5 em `tests/`).
- [x] Reescrever o `README.md`. Confirmado: conteúdo é específico do `operacoes`
      (descrição do serviço, `docker compose up -d`, os dois segredos obrigatórios),
      não o template do hub.
- [x] Revisar `docs/ROADMAP.md`: já não é o template do hub — tem a fila F1–F5
      derivada do `ARQUITETURA.md` §6/§9, com prompt próprio por fase.
- [x] Conferir `.env.example` e os composes: os nomes foram substituidos e os
      **valores** já refletem decisão, não herança cega — o serviço no compose local e
      no de produção chama-se `operacoes` (não `app`; ver `PADROES.md` §10.1), as
      portas (5081 app, 5434 db) evitam colisão com as do hub (5080, 5433), e os
      limites de recurso (`cpu_shares: 512`, `memory: 192m`) vêm de medição na VPS —
      ver `LEIA-ME-KIT.md`, "Armadilhas de infra", item 9. O comentário no
      `docker-compose.prod.yml` ainda descreve os limites como "ponto de partida, não
      número medido"; isso está desatualizado à luz do item 9 e vale revisar o texto
      do comentário, não o número.

## 3. Fora deste repo

- [x] Secrets no GitHub: `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY` e os do servico.
      Cadastre **antes** do primeiro merge — o deploy falha cedo e com mensagem clara
      se faltarem, mas falha.
- [x] No repo do `tesouro-direto`, para metrica (que e *pull* e mora la):
      alvo do scrape em `infra/alloy/config.alloy` com `job=operacoes`;
      dashboard em `infra/grafana/dashboards/`;
      **o nome do dashboard na lista fixa do `apply-cloud.sh`** (copiar o JSON nao basta);
      regras como `rules-operacoes.yaml`, **nunca** `rules.yaml`.
      **Estado parcial, nao check:** os quatro arquivos ja existem em disco em
      `../tesouro-direto-api` (`infra/alloy/config.alloy`, `infra/grafana/dashboards/
      operacoes.json`, `infra/grafana/cloud/rules-operacoes.yaml`, e a entrada no
      `apply-cloud.sh`), mas `git status` naquele repo mostra **`??` (nao rastreado)**
      para os dois `.json`/`.yaml` novos — e tambem para `rules-hub.yaml` e
      `hub-precos.json`, do proprio hub, esquecidos la de uma etapa anterior (ver
      `LEIA-ME-KIT.md`, "Escrever no repo certo e esquecer de rastrear la"). Sem commit
      la, o item nao esta pronto: some no primeiro clone limpo.
- [x] Rodar o `apply-cloud.sh` com `GC_GRAFANA_URL`, `GC_GRAFANA_TOKEN` e
      `TELEGRAM_BOT_TOKEN` **exportados na invocacao** — o script nao le o `.env`, e a
      guarda \`\${VAR:?}\` so testa vazio: um placeholder passa por ela e cala o Telegram
      de todos os servicos, com o script reportando sucesso.
- [x] **Orcamento de memoria da VPS.** Ela tem 2GB e UM nucleo, ja dividida entre TD,
      Hub e broker. Servico novo muda o teto dos VIZINHOS, nao so o seu (secao 10.14).
      Decida o teto deste antes do primeiro deploy, e revise os outros.
- [x] Semear a memoria do projeto: ela e **por caminho**, entao este repo nasce com a
      dele vazia. Peca: *"leia o LEIA-ME-KIT.md e grave na memoria do projeto: o
      criterio de pronto do F1, onde ficam as licoes aprendidas, e os limites de
      recurso da VPS"*.

## 4. Criterio de pronto do F1 (cinco provas)

1. **Um merge na `main` deploya sozinho** — deploy na unha por SSH nao conta.
2. `curl` no `/health/ready` **pela VPS**.
3. A serie do `job=operacoes` visivel no Grafana Cloud.
4. O dashboard deste servico, com dados.
5. **Um alerta seu disparando de proposito e chegando no Telegram** — a unica que
   prova a corrente inteira.

E depois de todo merge, confira o run de `push`: **verde no CI nao e deployado**.
No hub um PR ficou 12 dias fora do ar porque um teste instavel pulou o job de deploy.

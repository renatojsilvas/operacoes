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

- [ ] `git init` e primeiro commit; criar o repo no GitHub.
- [ ] Clonar o molde como irmao: `git clone <tesouro-direto> ../tesouro-direto-api`
      (o nome do diretorio precisa ser exatamente esse) e `/add-dir` na sessao.
- [ ] **Portar o CODIGO do molde** (nao deste repo): os 4 projetos, `Result`/`Error`,
      `ResultExtensions`, `MapReadGet`, `ApiKeyMiddleware`, CorrelationId, Serilog.
      Regra de ouro do `CLAUDE.md`: localize o equivalente no molde e siga.
- [ ] Copiar `tests/*.Architecture.Tests/` do `hub-precos` — a versao de la tem o
      **controle positivo** da secao 10.8, que o molde nao tem.
- [ ] `Operacoes.sln` e os csproj.
- [ ] Reescrever o `README.md`.
- [ ] Revisar `docs/ROADMAP.md`: veio como template, com a fila do hub.
- [ ] Conferir `.env.example` e os composes: os nomes foram substituidos, mas os
      **valores** (portas, limites de recurso) sao do hub e precisam de decisao.

## 3. Fora deste repo

- [ ] Secrets no GitHub: `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY` e os do servico.
      Cadastre **antes** do primeiro merge — o deploy falha cedo e com mensagem clara
      se faltarem, mas falha.
- [ ] No repo do `tesouro-direto`, para metrica (que e *pull* e mora la):
      alvo do scrape em `infra/alloy/config.alloy` com `job=operacoes`;
      dashboard em `infra/grafana/dashboards/`;
      **o nome do dashboard na lista fixa do `apply-cloud.sh`** (copiar o JSON nao basta);
      regras como `rules-operacoes.yaml`, **nunca** `rules.yaml`.
- [ ] Rodar o `apply-cloud.sh` com `GC_GRAFANA_URL`, `GC_GRAFANA_TOKEN` e
      `TELEGRAM_BOT_TOKEN` **exportados na invocacao** — o script nao le o `.env`, e a
      guarda \`\${VAR:?}\` so testa vazio: um placeholder passa por ela e cala o Telegram
      de todos os servicos, com o script reportando sucesso.
- [ ] **Orcamento de memoria da VPS.** Ela tem 2GB e UM nucleo, ja dividida entre TD,
      Hub e broker. Servico novo muda o teto dos VIZINHOS, nao so o seu (secao 10.14).
      Decida o teto deste antes do primeiro deploy, e revise os outros.
- [ ] Semear a memoria do projeto: ela e **por caminho**, entao este repo nasce com a
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

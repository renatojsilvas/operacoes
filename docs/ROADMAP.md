# ROADMAP do operacoes

Fila de tarefas, uma por vez.

**Como usar:** abra este arquivo, copie o bloco **Prompt** do proximo `F` nao marcado
e cole na sessao. O texto acima do prompt e briefing para voce, nao para colar — o
prompt ja carrega o que o orquestrador precisa. Ao aceitar a entrega, rode o criterio
de **Pronto**, marque o checkbox e commite. O arquivo e a fonte, nao o que estiver no
contexto de alguma sessao.

Arquitetura: `../plataforma-docs/ARQUITETURA.md`. Molde: `../hub-precos`
(ver `PADROES.md`, `LEIA-ME-KIT.md` e `CLAUDE.md`).

---

## Fila

- [ ] **F1** — esqueleto da solucao (Operacoes.API, Operacoes.Application, Operacoes.Domain,
  Operacoes.Infrastructure) seguindo o molde: Directory.Build.props, Dockerfile
  multi-stage, Serilog+CorrelationId, health/metrics, migrations no boot conectando
  como role `operacoes`. **Sem endpoints de negocio.**

  **O F1 nao termina quando compila.** Criterio de pronto em cinco provas — ver
  `LEIA-ME-KIT.md`, secao "O que o F1 tem que alcancar":

  1. um merge na `main` deploya sozinho (deploy na unha por SSH nao conta);
  2. `curl` no `/health/ready` **pela VPS**;
  3. a serie do `job=operacoes` visivel no Grafana Cloud;
  4. o dashboard deste servico, com dados;
  5. um alerta seu disparando de proposito e chegando no Telegram.

  Depois de todo merge, confira o run de `push`: **verde no CI nao e deployado**
  (PADROES 10.17).


  **Prompt:**
  ```
  Crie o esqueleto da solucao seguindo o molde em ../hub-precos: Operacoes.API,
  Operacoes.Application, Operacoes.Domain, Operacoes.Infrastructure, mais
  Directory.Build.props, Dockerfile multi-stage, Serilog com CorrelationId,
  health/metrics e migrations no boot conectando como role `operacoes`. SEM endpoints
  de negocio.

  Leia antes de despachar: PADROES.md secao 10 (17 itens, cada um de um incidente
  real) e LEIA-ME-KIT.md, secoes "O que o F1 tem que alcancar" e "Erros de
  orquestracao".

  O F1 NAO termina quando compila. Termina com CI/CD funcionando, deployado na VPS e
  com metrica no Grafana. Ao final rode ./scripts/verificar-f1.sh e me mostre a saida
  — nao marque nada como pronto sem ela.
  ```

- [ ] **F2** — schema do Operações como migrations EF, snake_case, índices nomeados.

  **O `ARQUITETURA.md` NÃO especifica estas tabelas** — define as 4 do Hub na §4.1 e as
  4 da Custódia na §7.1, e nenhuma daqui. O modelo abaixo foi derivado do contrato de
  `trades.registered` (§5.1) e da §6; confira contra elas antes de implementar.

  **Tabela `operacoes`** — o fato cru, o que o usuário declarou ter feito:

  | coluna | tipo | nota |
  |---|---|---|
  | `id` | text PK | o `tradeId` do evento (`op-...`), chave de dedupe do consumidor |
  | `cliente_id` | text NOT NULL | |
  | `instrumento_id` | text NOT NULL | id do Hub, **sem FK e sem tabela de-para** |
  | `operacao` | text NOT NULL | `aplicacao` \| `resgate` \| `aporte` \| `estorno` |
  | `quantidade` | numeric(18,8) NOT NULL | TD vende frações de 0,01 (§11) |
  | `valor_financeiro` | numeric(18,2) NOT NULL | bruto; arredondamento **sempre aqui** |
  | `data_evento` | date NOT NULL | pode estar no passado (retroativa) |
  | `registrado_em` | timestamptz NOT NULL DEFAULT now() | |
  | `estorna_operacao_id` | text NULL → `operacoes(id)` | correção é por estorno (§6) |

  Índices: `ix_operacoes_cliente (cliente_id, data_evento DESC)` para o padrão de
  leitura, e `UNIQUE (estorna_operacao_id) WHERE estorna_operacao_id IS NOT NULL` —
  uma operação não deve ser estornada duas vezes.

  **Sem FK para `instrumentos` e sem de-para**: a ADR-12 proíbe ler o banco de outro
  serviço, e a §7.2 registra o princípio para a Custódia — "grava o id do Hub
  diretamente; não existe id interno de instrumento (terceira identidade = tabela
  de-para a mais para divergir)". Vale igual aqui. A validação do `instrumento_id`
  acontece contra o REST do Hub, no F3.

  **Tabela `outbox`** — idêntica à do Hub de propósito, porque o relay do F4 é porte
  direto: `id bigserial`, `tipo`, `routing_key`, `payload jsonb`, `criado_em`,
  `publicado_em`, mais o índice parcial `ix_outbox_pendentes (id) WHERE publicado_em
  IS NULL`. Detalhe que morde: no Hub a coluna `criado_em` **não** tem `DEFAULT now()`
  no schema gerado pelo EF, embora a DDL da §4.1 documente — inócuo pela aplicação,
  mas `INSERT` manual falha por NOT NULL. Decida se replica ou corrige.

  **Três decisões em aberto — tome e registre na memória, com o motivo:**

  1. **Idempotência do `POST /operacoes`.** O consumidor deduplica por `tradeId`, mas
     se o cliente reenviar o POST (timeout, retry), nasce um `tradeId` novo e a
     operação duplica. Chave natural não serve: dois aportes idênticos no mesmo dia
     são legítimos. Provavelmente pede header de idempotência — mas a decisão é do F3,
     e o F2 só precisa não fechar a porta (nada de UNIQUE sobre os campos de negócio).
  2. **`cliente_id` sem tabela aqui.** A §7 diz que cliente é conceito que só existe na
     Custódia. Operações grava o id sem validar contra tabela própria; quem garante que
     existe é a borda autenticada. Registre isso como decisão explícita — implícito, o
     próximo vai querer criar uma tabela de clientes aqui e duplicar o conceito.
  3. **Imutabilidade.** A §9 proíbe edição destrutiva de fato: nenhuma linha sofre
     `UPDATE`, correção é `INSERT` de estorno. Decida se fica na disciplina (revisão +
     teste) ou ganha guarda no banco.



  **Prompt:**
  ```
  Implemente o schema do Operacoes como migrations EF, snake_case, indices nomeados,
  seguindo o molde de ../hub-precos/src/Hub.Infrastructure/Persistence/.

  As tabelas estao especificadas no F2 do docs/ROADMAP.md (o ARQUITETURA.md nao as
  define — foram derivadas do contrato de trades.registered na secao 5.1). Confira o
  modelo contra essas fontes antes de implementar; se discordar de alguma coluna,
  levante ANTES de escrever.

  Sem FK para instrumentos e sem tabela de-para: a ADR-12 proibe ler banco de outro
  servico, e a secao 7.2 registra o principio. A validacao do instrumento_id acontece
  contra o REST do Hub, no F3.

  As tres decisoes em aberto do F2 (idempotencia do POST, cliente_id sem tabela aqui,
  imutabilidade) — passe pelo advisor e grave o resultado na memoria com o motivo e as
  alternativas rejeitadas.
  ```

  <br>**Pronto:** migrations aplicando no boot; tabelas e índices conferidos no banco
  com `\d`; as três decisões acima registradas na memória do projeto.

- [ ] **F3** — `POST /operacoes` com a **camada 2** da validação (§6.1, ADR-11): rejeição
  síncrona 400/422 para instrumento inexistente, quantidade não positiva,
  `data_evento > hoje` e campos malformados. Grava o fato **e o evento na MESMA
  transação** (ADR-3) — nada é gravado nem publicado em caso de rejeição.
  Operações **não tem posição e não deve ter**: resgate maior que a posição e venda sem
  compra são camada 3, sinalizadas pela Custódia depois. Não implemente essa checagem
  aqui — duplicaria a projeção da Custódia, que é o acoplamento que a ADR-10 evitou.

  **Prompt:**
  ```
  Implemente POST /operacoes com a camada 2 da validacao (secao 6.1 do ARQUITETURA,
  ADR-11): rejeicao sincrona 400/422 para instrumento inexistente, quantidade nao
  positiva, data_evento > hoje e campos malformados. O fato e o evento vao na MESMA
  transacao (ADR-3); nada e gravado nem publicado se a validacao rejeitar.

  NAO implemente checagem de posicao (resgate maior que a posicao, venda sem compra):
  isso e camada 3, da Custodia. Duplicar aqui seria o acoplamento que a ADR-10 evitou.

  Molde: ../hub-precos para o padrao de endpoint, Result, ErrorType e problem+json.
  Lembre que falha de infraestrutura e 500 pelo handler global, nao Result de 400 —
  ver LEIA-ME-KIT.md, "Mandar o executor violar a camada".

  Ao final, revisor e guardiao-padroes em paralelo, e me mostre prova por mutacao dos
  testes de validacao.
  ```

  <br>**Pronto:** operação válida gravada com linha na outbox na mesma transação;
  inválida devolvendo problem+json com `code`, sem gravar nada.

- [ ] **F4** — relay outbox → RabbitMQ publicando `trades.registered` no exchange
  `prices` (§5 — sim, o exchange se chama `prices` e carrega trades também). É **porte
  do hub**: `../hub-precos/src/Hub.Application/Outbox/` e `Hub.Infrastructure/Messaging/`.
  Leve junto o que doeu lá: marcar `publicado_em` só no **maior prefixo contíguo
  confirmado**, o TTL do cache de ETag não se aplica aqui, e o relay fica **fora** do
  `/health/ready` — broker fora do ar não pode derrubar a API de escrita.

  **Prompt:**
  ```
  Implemente o relay outbox -> RabbitMQ publicando trades.registered no exchange
  `prices` (secao 5 do ARQUITETURA — o exchange se chama prices e carrega trades
  tambem). E porte de ../hub-precos/src/Hub.Application/Outbox/ e
  Hub.Infrastructure/Messaging/ — leia esses arquivos antes de escrever.

  Leve junto o que doeu la, esta tudo no PADROES secao 10 e no LEIA-ME-KIT:
  - marcar publicado_em so no MAIOR PREFIXO CONTIGUO CONFIRMADO do lote;
  - relay FORA do /health/ready (broker fora do ar nao pode derrubar a API de escrita);
  - o servico do broker NAO entra no compose deste repo, ele e da plataforma;
  - se mexer em depends_on ou em espera, procure quem depende dela (10.15).

  Ao final, revisor e guardiao-padroes em paralelo.
  ```

  <br>**Pronto:** `POST /operacoes` seguido de mensagem chegando numa fila de teste
  bindada em `trades.registered`, e a linha da outbox com `publicado_em`.

- [ ] **F5** — `GET /operacoes/instrumentos?query=...` (§6): proxy do catálogo do Hub
  com cache curto, no padrão `MapReadGet` do molde. Conveniências que são **de
  Operações, não do Hub**: priorizar instrumentos que o cliente já possui, e ocultar
  vencidos por default **mas permitir encontrá-los** — lançamento retroativo de título
  vencido é caso legítimo, e o filtro do autocomplete nunca pode bloquear o registro.
  A UI só fala com Operações; o Hub é infraestrutura interna e nunca é exposto ao front.

  **Prompt:**
  ```
  Implemente GET /operacoes/instrumentos?query=... (secao 6 do ARQUITETURA): proxy do
  catalogo do Hub com cache curto, no padrao MapReadGet do molde ../hub-precos.

  Conveniencias que sao de Operacoes e nao do Hub: priorizar instrumentos que o cliente
  ja possui, e ocultar vencidos por default MAS permitir encontra-los — lancamento
  retroativo de titulo vencido e caso legitimo, e o filtro do autocomplete nunca pode
  bloquear o registro.

  Invariante a preservar: mesma origem para lista e validacao. O instrumento que este
  endpoint oferece tem que ser aceito pelo POST /operacoes, por construcao.

  Ao final, revisor e guardiao-padroes em paralelo.
  ```

  <br>**Pronto:** autocomplete respondendo, e o mesmo instrumento que ele oferece sendo
  aceito pelo `POST /operacoes` — mesma origem para lista e validação, por construção.

Com o F5, fecha a **metade "Operações" do item 3** da ordem de implementação (§9). A
outra metade é a Custódia, em repo próprio: o critério de pronto do item ("aplicação
registrada via Operações aparecendo no livro por evento") só é verificável com as duas.

---

## Ao fechar cada F

Marque o checkbox, referencie o PR, e leve o que doeu para `PADROES.md` §10 (regra
técnica) ou `LEIA-ME-KIT.md` (armadilha de infra, erro de condução). Commit e PR
registram QUANDO; aqueles dois registram O QUE NÃO REPETIR — e são os únicos que o
próximo repo lê.

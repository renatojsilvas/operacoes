# PADROES.md — Constituição técnica (herdada do repo tesouro-direto-api)

Este arquivo é a referência normativa dos projetos novos (hub, operacoes, custodia).
Todo padrão aqui listado tem implementação de referência no repo `tesouro-direto-api`
(caminho em cada item). **Na dúvida entre este resumo e o código de referência, o
código de referência vence** — leia-o antes de decidir diferente, e desvios exigem
justificativa registrada na memória.

## 1. Arquitetura e camadas

- Solução em 4 projetos: `*.API` (endpoints finos), `*.Application` (commands/queries
  + handlers via MediatR/ISender), `*.Domain` (entidades, VOs, erros), `*.Infrastructure`
  (EF Core p/ escrita, Dapper p/ leitura, clients externos, jobs).
  Ref: `src/TesouroDireto.*/`
- Endpoint NUNCA contém lógica: recebe request → `ISender.Send` → `Result` →
  `ToHttpResult`. Ref: `src/TesouroDireto.API/Endpoints/*.cs`
- Erros de domínio como `Error` tipado com `ErrorType` (Validation|NotFound|Conflict),
  mapeados centralmente para HTTP (400/404/409) — nunca por string, nunca try/catch
  de fluxo. Ref: `Domain/Common/DomainErrors.cs`, `API/Extensions/ResultExtensions.cs`

## 2. Contratos HTTP

- Rotas de negócio versionadas sob grupo `/v1`; fora dele só health/metrics/swagger.
- Corpo de erro: `application/problem+json` com `detail`, extensão `code`
  (`recurso.motivo`), `correlationId`, `traceId`.
- Leituras via helper padrão (`MapReadGet`): GET+HEAD+OPTIONS, 405 com `Allow`,
  ETag/304 (`ConditionalGetFilter` sobre token global de versão), supressão de corpo
  em HEAD, `Cache-Control: public, max-age=300` só em 2xx.
  Ref: `API/Http/ReadEndpointExtensions.cs`
- Escritas: sem ETag; respostas sensíveis a tempo usam `no-store`.
- Paginação: `page/pageSize` com clamp (default 100, máx 500), `X-Total-Count` sempre,
  header `Link` (next/prev) só quando `page` informado. Coleções limitadas por
  construção podem dispensar paginação — decisão registrada.
- Identificadores públicos: **slugs determinísticos**, nunca uuid em contrato.
  Ref: racional do `codigo` de títulos.
- POST cria → 201 + `Location` via rota nomeada (`CreatedAtRoute`); PUT idempotente → 204.

## 3. Persistência

- CQRS leve: EF Core só na escrita; leituras em Dapper em ReadRepositories com SQL
  explícito. Ref: `Infrastructure/Persistence/Repositories/*ReadRepository.cs`
- **Toda porta de repositório devolve `Result<T>` — leitura inclusive.** No repo de
  referência isso é uniforme: `ITituloReadRepository` e `IPrecoTaxaReadRepository` têm
  seis métodos entre os dois, todos `Task<Result<...>>`. Porta de leitura com tipo cru
  não tem como reportar falha sem exceção, e força o chamador a escolher entre engolir
  o erro ou usar try/catch de fluxo — os dois proibidos pela §9. A implementação vive
  na Infrastructure, e é lá que exceção de infraestrutura vira `Result`; a Application
  nunca captura. Ref: `src/TesouroDireto.Application/{Titulos,PrecosTaxas}/I*ReadRepository.cs`
- Parâmetro de porta usa o **value object** onde ele existe (identidade e classificação),
  e primitivo só onde não há VO — data usada como filtro ou relógio continua `DateOnly`,
  termo de busca livre continua `string`. Ref: `ITituloWriteRepository.ExistsAsync(TipoTitulo,
  DataVencimento, ct)` vs. `IPrecoTaxaReadRepository.GetByTituloIdAsync(Guid, DateOnly?, DateOnly?, ct)`
- Migrations EF aplicadas no boot + seed idempotente.
- Snake_case no banco; índices nomeados (`ix_tabela_colunas`); todo padrão de acesso
  novo exige índice correspondente na mesma migration.
- Upserts idempotentes por chave natural (`ON CONFLICT`); jobs re-executáveis sem
  efeito duplicado.
- **Banco privado por serviço**: role própria não-superuser, schema próprio com
  `REVOKE ... FROM PUBLIC`, UMA connection string por serviço, integração entre
  serviços SOMENTE por contrato (HTTP/eventos), jamais lendo banco alheio.
- **Identidade de outro contexto grava-se CRUA**: sem FK, sem de-para, sem réplica local
  da entidade. Se o dono do conceito é outro serviço, guarde só o id que ele emitiu e
  valide contra o contrato dele (REST/evento), nunca contra tabela sua. Uma cópia local
  é uma terceira identidade a divergir, e validar contra ela é validar contra dado de
  que você não é dono. Vale para `instrumento_id` (id do Hub) e para `cliente_id`
  (conceito que só existe na Custódia — quem garante que existe é a borda autenticada):
  **`Operações` não tem, e não terá, tabela de clientes nem de instrumentos**. Ref:
  ADR-12 e §7.2 do `ARQUITETURA.md`; `Infrastructure/Persistence/Configurations/OperacaoConfiguration.cs`.

## 4. Integrações externas e jobs

- Cliente HTTP externo = typed client + Polly (retry exponencial + circuit breaker).
- Agendamento via Quartz; um job por responsabilidade; horários em config.
- Cache de dado externo com fallback explícito e não-silencioso
  (fresh + last-known-good com origem exposta no contrato).
  Ref: `CachedProjecaoMercadoService` (padrão Bcb|CacheFallback).
- Consumo de API própria/externa com GET condicional (If-None-Match) quando o
  provedor expõe ETag — sondar barato antes de coletar caro.

## 5. Mensageria (padrão novo, consolidado nas ADRs do plano)

- Transactional outbox obrigatória: efeito + evento na MESMA transação; relay
  publica at-least-once; consumidor deduplica por chave natural (`ref_externa` UNIQUE).
- Contratos de evento JSON com envelope `{"v": n, "tipo": ...}`; valores monetários
  como string decimal; campos novos sempre opcionais.
- Fila durável por serviço consumidor; manual ack pós-persistência.

## 6. Segurança e limites

- Auth por API key em header, middleware global, paths isentos explícitos.
- Rate limiting por config (`appsettings`), nunca hardcoded.
- Segredos por variável de ambiente/secrets; nunca em código ou compose commitado.
- Guarda de boot por credencial obrigatória: fora dos ambientes isentos, segredo
  ausente **derruba o boot** — nunca significa "sem verificação". Guardas irmãs podem
  isentar ambientes diferentes, e a resposta depende do que falta sem a credencial:
  sem connection string a aplicação não sobe de jeito nenhum (isenta só `Testing`);
  sem API key ela sobe e o middleware fica fail-closed nas rotas de negócio, o que é
  útil localmente (isenta `Testing` e `Development`). Diferença deliberada — se
  divergir, registre o porquê. A guarda de API key também rejeita chave abaixo de um
  comprimento mínimo (32 caracteres), não só vazia — uma chave curta sobe sem
  reclamação e não é menos perigosa que uma vazia — e rejeita placeholders conhecidos
  (`CHANGE-ME-IN-PRODUCTION`, `dev-local-key`, e outros que apareçam na própria
  documentação do repo), mesmo quando o valor configurado atinge o comprimento
  mínimo: comprimento e conteúdo são camadas independentes, uma não substitui a
  outra. **Toda guarda de API key segue este par de camadas — não só a que o
  middleware do próprio serviço exige (`ApiKey:Key`), também a que o serviço envia a
  outro (`Hub:ApiKey`)**: guardas irmãs isentando ambientes diferentes está previsto
  acima, mas comprimento/placeholder mais fraco numa delas não está — foi achado de
  auditoria no F3, quando `Hub:ApiKey` só checava vazio. As duas guardas compartilham
  o par de checagens por um helper único (`KeyStrengthGuard`), para a lista de
  placeholders não divergir com o tempo. Ref: `API/Extensions/
  {ConnectionStringGuard,ApiKeyGuard,HubConfigGuard,KeyStrengthGuard}.cs`

## 7. Observabilidade e operação

- Serilog estruturado + Correlation ID em toda requisição; logs → Loki/Grafana;
  métricas Prometheus em `/metrics`; health em `/health`, `/health/ready`, `/health/live`.
- Docker multi-stage; docker-compose com app + Postgres + observabilidade;
  CI GitHub Actions: testes → Sonar → deploy SSH. Ref: `Dockerfile`, `docker-compose.yml`,
  `.github/workflows/deploy.yml`
- Toda anomalia rara e significativa (ex.: revisão de dado retroativo) gera log
  destacado — raridade merece visibilidade.

## 8. Qualidade

- Testes: unidade (domínio/handlers), integração (repositórios/HTTP com banco real),
  e2e via compose (`run-e2e.sh` como modelo). Comportamento HTTP completo é testado:
  status, headers (X-Total-Count, Allow, ETag/304), corpo problem+json com codes.
- SonarQube local e no CI.
- Documentação de decisão: ADR curto por decisão estrutural (decisão, motivo,
  alternativas rejeitadas) — e gravado na memória MCP.
- **Código sem comentários.** Nenhum `//`, `/* */` ou `///` nos `.cs`. Decisão do dono
  do repo, 2026-09-06. O que um comentário diria vai para nome de método, nome de teste
  ou estrutura; o que não couber aí vai para `PADROES.md`, `LEIA-ME-KIT.md` ou `docs/`.
  Três itens deste catálogo exigiam o contrário e foram revogados na mesma decisão (a
  regra de identidade repetida na `OperacaoConfiguration`, a exceção da §10.21 e o
  inventário do "não cobre" da §10.22) — se você encontrar num texto antigo a instrução
  de "comentar no código", ela caiu.
  **O custo, que fica registrado por honestidade:** as três eram guardas posicionadas
  onde alguém está prestes a errar, e passam a viver longe de quem vai violá-las. Se um
  dia reaparecer tabela de clientes, alguém "consertar" a cadeia de estorno, ou o
  readiness for tratado como prova de mais do que cobre, a causa está aqui.
  **Arquivos gerados não obedecem sozinhos:** `dotnet ef migrations add` reescreve
  `// <auto-generated />` e `/// <inheritdoc />` a cada migration nova. Passe a limpeza
  neles depois de gerar. Não é só estética — o marcador `<auto-generated />` é o que faz
  analisador e StyleCop pularem o arquivo; aqui isso não morde porque o
  `sonar-project.properties` já exclui `**/Migrations/**`, mas quem copiar esta regra para
  um repo sem essa exclusão vai ligar análise sobre código gerado sem perceber.

## 9. Antipadrões proibidos (com a razão)

- Integração via banco de outro serviço (contorna contrato; migration alheia vira
  breaking change silencioso).
- Endpoint com lógica de negócio; erro mapeado por string; exception como fluxo.
- Resposta de coleção sem limite superior em contrato novo.
- Estado de controle duplicando dados (flags de bootstrap) — derive dos dados
  (padrão watermark/reconciliação).
- Publicar evento fora da transação do efeito (dois commits que divergem).
- Forward-fill materializado como observação; edição destrutiva de fato — corrija
  por revisão/estorno preservando a versão anterior.

## 10. Lacunas do molde (aprendidas por incidente, não por leitura)

Os itens de 1 a 9 vieram do `tesouro-direto`, o repo que serviu de molde ao `hub`.
**Os desta seção não** — cada um nasceu de algo que quebrou de verdade ao construir o
`hub`, porque aquele repo não tinha o caso ou porque o porte perdeu uma peça.

Com o `hub` promovido a molde da plataforma, esta seção deixa de ser "o que falta no
molde" e passa a ser **o que o molde sabe porque doeu**. Ela não é opcional: os itens
1 a 9 você reconhece lendo código, os desta seção não aparecem em código nenhum — só
no incidente.

Se você está criando `operacoes` ou `custodia`, leia isto **antes** de portar — foi
escrito para você não repetir.

### 10.1. Nome de serviço tem que ser único NA REDE, não só no arquivo

Ao entrar numa rede compartilhada (`external: true`), confira os aliases já
registrados antes de escolher o nome do serviço no compose.

**Por quê:** o Compose registra um alias DNS igual ao **nome do serviço**;
`container_name` acrescenta um nome, não substitui o alias. Um serviço `app` entrando
numa rede que já tem outro `app` faz o DNS do Docker guardar os dois IPs sob o mesmo
nome e devolver a lista alternada — sem erro, sem log. Metade das chamadas vai para o
vizinho. Aconteceu: o front do `tesouro-direto` passou a receber 404, e metade das
raspagens de métrica dele vinha do Hub, poluindo a série de onde saem 12 das 18
regras de alerta. Ocupados hoje na `tesouro-net`: `app`, `db`, `web`, `alloy`,
`tesouro-direto-*`.

**Guarda:** o job de deploy compara os nomes de serviço do compose com os aliases de
terceiros na rede e aborta antes do `up`. Cada repo precisa da sua — a do Hub só
protege o Hub. Ver `.github/workflows/ci.yml`.

**Corolário (2026-09-05, `operacoes`): `aliases:` não resolve a colisão, porque não
substitui o alias do nome do serviço.** A correção intuitiva ao encontrar um serviço
`app` — acrescentar `aliases: [nome-prefixado]` ao bloco de rede — não resolve, porque o
Compose registra o nome do **serviço** como alias sempre; `aliases:` acrescenta, não
substitui. Medido na VPS: o serviço `app` do `tesouro-direto` (`container_name:
tesouro-direto-app`) responde na rede `tesouro-net` por **dois** aliases ao mesmo
tempo — `tesouro-direto-app` e `app`; o mesmo padrão no hub (`hub-precos-app` e `hub`).
O `docker-compose.yml` local do `operacoes`, gerado por substituição textual do molde,
tinha o serviço chamado `app` entrando na rede compartilhada `plataforma` — mesmo com
o comentário do molde nomeando este repo explicitamente ("app fica de fora da
`plataforma` de propósito… quando custodia/operacoes chegarem, eles entram com nomes
prefixados pelo domínio"). A correção certa é **renomear o serviço**, não acrescentar
alias: depois de renomear para `operacoes`, `docker inspect` confirmou
`operacoes_default: operacoes-operacoes-1 operacoes` e `plataforma:
operacoes-operacoes-1 operacoes` — nenhum `app`.

**Guarda adicional:** confira o resultado com `docker inspect` dos aliases efetivos,
nunca pela leitura do YAML — o YAML não distingue "acrescenta" de "substitui".

### 10.2. Provisionar credencial sem verificá-la é afirmar sem evidência

Todo script que cria ou converge uma credencial tem que **testar** essa credencial
antes de reportar sucesso, e falhar se ela não autenticar.

**Por quê:** o molde provisiona a role e termina. Quando o valor divergiu, o
provisionamento passou verde e o defeito apareceu minutos depois, como crash-loop da
aplicação num container à parte — longe da causa e caro de diagnosticar. Com a
verificação, o deploy morre no passo certo, com a mensagem certa.

### 10.3. Verificação de credencial passa pela rede, nunca por loopback

Use um container cliente à parte na mesma rede docker. **Nunca**
`docker exec ... psql -h 127.0.0.1`.

**Por quê:** o `pg_hba.conf` da imagem oficial do Postgres tem
`host all all 127.0.0.1/32 trust`. Por loopback, **qualquer senha autentica** — a
verificação passaria sempre e seria teatro. Medido nos dois sentidos: pela rede
rejeita senha errada, por loopback aceita.

### 10.4. Segredo que alimenta dois caminhos tem que ser normalizado na origem

Se o mesmo segredo chega ao provisionamento por um caminho e à aplicação por outro,
normalize (`tr -d '\r\n'`) **uma vez, antes de qualquer uso**.

**Por quê:** `docker exec -e` preserva o valor byte a byte; o `.env` do compose é lido
**por linha** e descarta um `\n`/`\r` final. Um segredo colado com quebra de linha faz
a role nascer com um byte a mais do que a aplicação envia. O pior: qualquer comparação
feita a partir do `.env` mostra os dois lados idênticos, porque a leitura já
normalizou — o diagnóstico dá falso negativo.

### 10.5. Script de provisionamento tem que ser atômico fim a fim

`BEGIN`/`COMMIT` **dentro do arquivo**, não `--single-transaction` na invocação.

**Por quê:** o molde tem blocos `DO` separados, que são transações independentes. Uma
falha entre eles deixa a role criada e o `REVOKE CONNECT` sem aplicar. E o
`docker start` seguinte sobe **limpo e silencioso**, porque a imagem oficial só executa
os hooks de initdb com `PGDATA` vazio — ninguém é avisado. A garantia tem que estar no
arquivo porque ele roda por dois caminhos (initdb e `docker exec ... psql -f`), e
depender de quem digita o comando lembrar da flag reabre a janela.

### 10.6. Role é global ao cluster: guarde contra o database errado

O SQL de provisionamento deve abortar, antes de qualquer DDL, se
`current_database()` não for o esperado.

**Por quê:** `ALTER ROLE ... PASSWORD` vale para o cluster inteiro. Rodar o script com
o `-d` errado rotaciona a senha da role de produção em silêncio e ainda dá ownership a
um banco não relacionado. O molde não tem essa guarda.

### 10.7. Em cluster compartilhado, alguém tem que criar o database

O script que provisiona a role assume que o database existe — localmente quem o cria é
o `POSTGRES_DB` do compose, via entrypoint da imagem. **Num cluster que já está de pé
não há entrypoint nenhum.** Providencie o `CREATE DATABASE` num passo separado
(`\gexec`; ele não roda em transação nem em bloco `DO`).

### 10.8. Asserção negativa precisa de controle positivo

Teste que afirma "X não referencia Y" passa tanto quando a regra é respeitada quanto
quando o mecanismo de detecção quebra. Acompanhe-o de uma asserção sobre algo que
**existe de verdade**.

**Por quê:** se `GetReferencedAssemblies()` um dia devolver vazio — outro
TargetFramework, trimming, mudança de SDK —, todos os testes de camada ficam verdes
sem checar nada, e a regra de PADROES §1 fica desprotegida em silêncio. Mesma lógica
para teste que itera coleção: guarde contra a coleção vazia.

**Corolário: mutação que contém a palavra que o scanner procura não é mutação.** Ao
provar por mutação que o teste "Domain e Application não contêm `catch`" não é vácuo, a
primeira tentativa passou por engano: o comentário escrito junto da mutação continha a
palavra `catch`, e o regex `\bcatch\b` do próprio teste a capturava — o teste continuou
verde, mas por um motivo diferente do que o autor pensava. Só ficou vermelho depois de
reescrever o comentário. **Guarda:** ao mutar para provar não-vacuidade, confira que a
mutação mudou o sinal que o teste lê, não só o texto ao redor — mutação que não altera
esse sinal é mutação que não aconteceu, e o verde resultante é falsa confirmação de que
o teste presta.

### 10.9. Verifique com o comando literal da documentação

Ao validar, rode **exatamente** o comando que está escrito no README — não um
equivalente com variáveis de ambiente na frente.

**Por quê:** o porte perdeu o `Properties/launchSettings.json`, e sem ele `dotnet run`
sobe em `Production`, onde user-secrets não são carregados. Executores e duas revisões
adversariais passaram, porque todos subiam a aplicação com variáveis explícitas. O
comando do README falhava, e quem descobriu foi o dono, na primeira tentativa real.

### 10.10. Compare o molde por AUSÊNCIA, não só por fidelidade

Antes de dar um porte por concluído, liste o que o molde tem e você **não** portou, e
justifique cada ausência.

**Por quê:** fidelidade no que se copia é a metade fácil. Passaram despercebidos o
`launchSettings.json`, a linha `test-results/` do `.gitignore` (removida por parecer
inaplicável, e que voltou a ser aplicável), o CI inteiro, o `sonar-project.properties`
e o gate de cobertura. Todos existiam no molde; nenhum foi decidido, só esquecido.

### 10.11. Endpoint de outro serviço no seu log nunca é ruído

Requisição a um caminho que pertence ao contrato de outro serviço aparecendo no seu
log é sinal de roteamento errado. Investigue antes de explicar como teste manual.

**Por quê:** 16 requisições `GET /v1/titulos` com 404 apareceram no log do Hub e foram
lidas como ruído. Eram o front do `tesouro-direto` caindo aqui por causa da colisão de
alias (10.1) — a prova do incidente estava disponível antes de alguém notar o
incidente.

### 10.12. Runtime dentro de container não enxerga o limite do cgroup

Todo runtime que dimensiona algo "em função da memória disponível" — heap, cache,
watermark, número de threads — lê o **host**, não o seu `deploy.resources.limits`.
Fixe o valor em absoluto, sempre.

**Por quê:** o `vm_memory_high_watermark` do RabbitMQ é relativo por padrão (0,4).
Numa VPS de 2GB isso reserva ~800MB **dentro** de um cgroup de 384MB: o kernel mata o
processo por OOM antes de o watermark ter chance de aplicar backpressure — ou seja, o
mecanismo que existe para evitar o OOM nunca chega a agir. O mesmo vale para o GC do
.NET, que sem limite de cgroup vê os 2GB do host e deixa o heap crescer bem além do
que cresceria vendo 256MB. Corolário desagradável: a env var clássica
`RABBITMQ_VM_MEMORY_HIGH_WATERMARK` está **deprecated** na imagem `rabbitmq:4` e o
entrypoint **recusa subir** com ela setada — o valor absoluto tem que entrar por
arquivo de config.

**Guarda:** depois de subir, confira o valor efetivo lá dentro (`rabbitmqctl status`,
`GC.GetGCMemoryInfo`, o que o runtime expuser). Se ele bate com o host e não com o
container, está errado.

### 10.13. Em host de um núcleo, teto de CPU não contém nada — só corta rajada

Use **peso** (`cpu_shares`) para proteger vizinho sob contenção. Um teto rígido
(`cpus`) abaixo de 1.0 num host de 1 vCPU não impede runaway nenhum, porque o processo
já não pode passar de um núcleo; ele só transforma trabalho curto em trabalho throttled.

**Por quê:** o broker recebeu `cpus: "0.60"` descrito como "folgado, só para conter
loop desgovernado". Não era folgado: a coleta de estatísticas do plugin de management
(a cada 5s por padrão) precisa de ~0,6s de CPU, e o corte a 60ms por período de 100ms
a esticava por ~10 períodos. Resultado: ~20% dos períodos throttled, exatamente o
limiar do alerta, com o broker **99,8% ocioso** no resto do tempo.

**Como reconhecer:** throttling com CPU média baixa é normal e enganoso — o CFS não
limita média, limita fatia por período. Num laço de `docker stats`, rajada que para
**exatamente** no valor da cota é trabalho cortado, não trabalho excessivo.

### 10.14. Serviço novo muda o orçamento dos VIZINHOS, não só o seu

Ao acrescentar serviço ou volume de dados numa máquina pequena, revise os tetos de
quem já estava lá. O limite deles foi dimensionado para um mundo que você acabou de
mudar.

**Por quê:** o Postgres compartilhado tinha 128MB, dimensionados quando servia só o
`tesouro_direto` (44MB). O Hub passou a escrever no mesmo cluster e o database `hub`
chegou a 298MB — 288MB só da tabela `precos`. Servir ~342MB dentro de um cgroup de
128MB faz o kernel reciclar página continuamente, e o alerta de reclaim sustentado
começou a disparar num container que ninguém tinha tocado. A ADR-12 separa **bancos**
por serviço; ela não separa **orçamento de memória**, porque a instância é uma só.

### 10.15. `up -d` retorna quando o container arranca, não quando fica pronto

Qualquer passo de deploy que fale com um serviço recém-subido precisa esperar pela
**condição real** que ele usa — não pelo healthcheck, e nunca por nada.

**Por quê:** a verificação de credencial do broker rodava logo após o `up -d` e falhou
com `curl: (7) failed to connect` 6 segundos depois do "Started", reportando
credencial divergente quando o problema era só tempo. A espera implícita existia antes
por acidente (`depends_on: service_healthy`) e sumiu quando essa condição foi trocada
por `service_started` — por um bom motivo, mas sem que ninguém procurasse quem
dependia dela. Esperar pelo healthcheck também não resolveria:
`rabbitmq-diagnostics check_running` fica verde **antes** de a porta de management
aceitar conexão, então ele é um proxy da condição, não a condição.

**Guarda:** o laço espera pela própria verificação que você vai fazer, e distingue
"não conectou" (repete) de "conectou e recusou" (falha rápido) — são diagnósticos
diferentes, e tratá-los igual manda o operador para o lado errado.

### 10.16. Cache sem prazo é uma afirmação que nunca é confrontada

Toda entrada de cache que descreve estado externo precisa de prazo de validade. Sem
ele, o cache afirma para sempre algo que ele não observa.

**Por quê:** o ETag da TD API ficava num dicionário em memória, sem expiração.
Enquanto o banco só crescia, a afirmação era verdadeira por acidente. No instante em
que o banco foi zerado por fora, ela virou mentira permanente: a sonda mandava
`If-None-Match`, a TD API respondia 304 corretamente ("você já tem esse corpo" — e o
Hub não tinha), e o ciclo encerrava sem ingerir nada, indefinidamente, até alguém
reiniciar o container. É o antipadrão do §9 — estado de controle divergindo dos dados.

**Cuidado com o remédio errado:** persistir o ETag junto dos dados parece "derivar dos
dados" e é pior. A volatilidade foi justamente o que permitiu curar o incidente com um
restart; persistindo, o incidente vira permanente, e continua quebrando em truncate
parcial, restore de dump antigo e delete de uma tabela só.

### 10.17. Commit mergeado não é commit em produção

Se o job de deploy depende do job de teste, um teste instável **pula o deploy em
silêncio**. O painel do repositório fica verde-ish, a `main` tem o código, e produção
não tem.

**Por quê:** um PR ficou 12 dias mergeado e fora de produção. O teste falhou por
instabilidade de infraestrutura, o deploy foi pulado, e ninguém notou — porque nada
distingue "nunca deployado" de "deployado" na leitura casual do histórico.

**Guarda:** o pipeline deve avisar quando a `main` tem commit sem deploy
correspondente. Enquanto isso não existir, confira o último run de `push` depois de
todo merge, não só o do PR.

### 10.18. `/health/ready` prova conectividade, não prova schema

`AddDbContextCheck<T>` chama `CanConnectAsync()` — ele não confere se as migrations
foram aplicadas. Trate o resultado do readiness como "o banco responde", não como "o
schema está certo".

**Por quê:** provado por mutação no `operacoes`: com a aplicação de migrations
desligada e o schema **completamente vazio** (`__EFMigrationsHistory` dropada, zero
tabelas), `/health` e `/health/ready` responderam `200 Healthy`. A distinção que muda a
gravidade: migration que **falha** não passa despercebida — o boot lança exceção não
tratada antes de escutar a porta (provado: revogar a posse do schema `public` produziu
`Npgsql.PostgresException 42501: permission denied for schema public`, e o processo
morreu antes do `app.Run()`), então o container nunca fica `healthy`, o healthcheck do
Docker nunca vira verde, e o laço `until healthy` do job de deploy falha alto. O risco
real é mais estreito e mais silencioso: migration **pulada** (feature desligada, chamada
removida por regressão) ou **drift manual** (tabela dropada por fora com a linha do
histórico intacta). Nesses dois casos o `/health/ready` mente — responde saudável com o
dado que ele deveria servir ausente.

No `operacoes` isso era inofensivo enquanto a migration da primeira fase era vazia —
não havia tabela para faltar. **No `hub` a lacuna já está aberta hoje**: mesmo
`AddDbContextCheck<AppDbContext>()`, quatro migrations reais, e um `/health/ready` que
responderia `200 Healthy` com qualquer uma delas ausente por drift. A regra geral: a
partir do momento em que um serviço tem schema, o readiness precisa confirmar algo além
de `CanConnectAsync()` para continuar valendo como prova de prontidão.

### 10.19. Testar o header não testa o log

Todo dado que existe em mais de um canal de saída precisa de teste **por canal**. Um
teste verde num canal não é evidência sobre o outro.

**Por quê:** o CorrelationId trafega por dois caminhos independentes — o header HTTP de
resposta e o corpo do problem+json saem de `context.Items`; o campo no log estruturado
sai do `LogContext.PushProperty` do Serilog. Provado por mutação numa revisão
adversarial: removido o `LogContext.PushProperty` mantendo o header intacto, os 16
testes de CorrelationId **continuaram passando**. O comportamento estava correto
(confirmado em log real), mas sem rede de proteção nenhuma — uma regressão futura
passaria pela CI sem ninguém notar. O `PADROES.md` §7 exige correlation id em toda
requisição justamente porque é o que torna incidente rastreável; perdê-lo em silêncio no
log custa caro exatamente quando mais se precisa dele.

**Guarda:** para todo dado que existe em mais de um canal de saída, escreva um teste por
canal. Não infira o log a partir do header.

### 10.20. Porte herda também as afirmações que só valiam para o molde

Inverso da §10.10: lá o risco é o que faltou copiar; aqui é o que **sobrou** copiado —
uma asserção que era verdadeira no molde e é falsa nesta fase.

**Por quê:** o gerador de repo novo faz substituição textual, então o CI veio inteiro e
funcionando — e afirmando funcionalidade que o `operacoes` do F1 não tem: smoke test em
`GET /v1/instruments` (endpoint do molde, que só nasce no F5 daqui); a cadeia inteira do
`TD_API_KEY` (este serviço não chama a TD API); consulta de backlog em `SELECT
COUNT(*) FROM outbox` (tabela que só nasce no F2); verificação de credencial do broker e
smoke test do relay em `hub_relay_ciclos_total` (F4); e exclusão do container
`operacoes-rabbitmq` na guarda de colisão — container que o compose deste repo não sobe.
Rodado assim, o deploy da primeira fase falharia em smoke test de coisa que não existe.

**Guarda:** ao portar, compare por ausência **e** por excesso — para cada asserção
herdada, pergunte "isto existe nesta fase?". E ao remover, **substitua por uma
asserção equivalente sobre o que existe** em vez de só apagar: aqui o smoke test de
`/v1/instruments` virou "sem chave → 401 **e** com a chave → 404", que juntas provam que
o middleware está ativo e que a chave confere — enquanto só o 401 passaria também com a
chave errada.

### 10.21. Em tabela append-only, o lado estrito é o lado reversível

Numa tabela imutável (UPDATE e DELETE bloqueados), a assimetria de custo entre restringir
demais e restringir de menos **se inverte** em relação ao normal. Na dúvida entre pôr a
constraint agora ou adiar para a próxima fase, **ponha agora**.

**Por quê:** aprendido no F2 do `operacoes`, em revisão adversarial. A tabela `operacoes`
tinha três decisões que, isoladas, estavam certas: trigger de imutabilidade (correção é
por estorno, nunca por UPDATE), índice `UNIQUE (estorna_operacao_id) WHERE ... IS NOT
NULL` (uma operação não se estorna duas vezes), e validação de negócio adiada para a fase
seguinte, "porque o F2 é schema". Juntas, abriram um caminho de dado **irreparável**:
`INSERT` com `estorna_operacao_id = id` passa — a FK auto-referente se satisfaz sozinha —,
consome o slot único de estorno daquela linha, e como UPDATE e DELETE estão bloqueados,
o estorno legítimo daquela operação fica impossível **para sempre**. Vale igual para uma
operação de outro tipo carregando referência, e para um estorno apontando para operação
de outro cliente (que ainda publicaria evento no livro errado).

A conta que decide: sair de uma constraint estrita demais é `DROP CONSTRAINT` — migration
de uma linha, sem rewrite, e drop de constraint **nunca invalida linha existente**. Sair
de uma constraint que faltou exige `DISABLE TRIGGER` mais perícia manual, e os eventos já
publicados rio abaixo não voltam.

**Guarda:** ao fechar o schema de uma tabela append-only, não pergunte "esta regra é desta
fase?" e sim "**se entrar dado errado aqui, dá para consertar depois?**". O que não dá,
entra agora. Há uma exceção real: a guarda que fecharia a última porta de correção — aqui,
bloquear estorno **de** estorno teria sido isso, porque cadeia de estorno é a única saída
quando um estorno entra errado.

### 10.22. `GetPendingMigrationsAsync` não prova schema — prova histórico

Continuação direta da §10.18. Endurecer o `/health/ready` com `GetPendingMigrationsAsync()`
fecha **metade** da lacuna e dá a sensação de ter fechado inteira.

**Por quê:** provado por mutação no F2 do `operacoes`. `GetPendingMigrationsAsync()` compara
a lista gravada em `__EFMigrationsHistory` com as migrations do assembly — nunca toca o
catálogo do Postgres. Com `DROP TABLE operacoes CASCADE` por fora e o histórico intacto:
`0 pendências`, `/health/ready` respondeu **200**. Dos dois riscos que a §10.18 nomeia,
ele cobre migration **pulada** e não cobre **drift manual** — que é justamente o mais
silencioso dos dois.

Agravante do mesmo incidente: o comentário no código **afirmava** cobrir "drift manual,
tabela dropada por fora". Afirmação falsa escrita no código é pior que a lacuna, porque
desliga a desconfiança de quem lê depois.

**Guarda:** some uma sonda de existência física, com a lista de tabelas derivada de
`db.Model.GetEntityTypes()` — **nunca escrita à mão**, que desatualiza em silêncio quando
entrar tabela nova. Uma consulta só, `unnest` + `to_regclass`, resolve todas. O que este check **não** cobre —
coluna, índice ou CHECK alterados mantendo a tabela — fica registrado aqui, no catálogo, e
não no código.

**E o inventário do "não cobre" também é uma afirmação — tem que estar completo.** Na
revisão seguinte, dropar a **trigger de imutabilidade** por fora deixou o `/health/ready`
em 200 com o `UPDATE` voltando a passar em silêncio: a única guarda que impede corrupção
irreversível em `operacoes` sumia sem ninguém notar, e a trigger não estava na lista do que
o check não cobria. Regra derivada: **objeto de schema cuja ausência permite corrupção
silenciosa não vai para a lista do "não cobre" — vai para a sonda.** Consulte `pg_trigger`
com `tgisinternal = false`, para não casar as triggers internas de constraint.

Declare a trigger no modelo com `ToTable(t => t.HasTrigger("nome"))` e derive a lista de
`GetDeclaredTriggers()`, do mesmo jeito que a lista de tabelas — **não** deixe o nome
literal na consulta. Verificado neste repo (EF Core 8.0.11 + Npgsql) antes de adotar: a
anotação é **puro metadado**, o `migrations script` sai idêntico e uma migration gerada com
ela vem com `Up`/`Down` vazios. A checagem que importa fazer antes de copiar isto para outro
provider: no SQL Server, declarar trigger faz o provider **abandonar a cláusula `OUTPUT`** e
mudar a estratégia de escrita (`SqlServerOutputClauseConvention`) — esse convention existe
só no assembly do SQL Server; o do Npgsql não tem convenção alguma que reaja ao metadado de
trigger. Verificado ainda por fora do assembly, que é a prova que vale: `migrations
has-pending-model-changes` sem mudanças, nenhuma DDL gerada pela anotação, e o SQL de INSERT
idêntico com e sem ela.
E note por que a sonda continua necessária mesmo com o metadado declarado: **`HasTrigger`
registra intenção, não confere existência** — é exatamente a distinção da §10.18.

**Erro que este item quase carregou:** a primeira versão deste texto afirmava que "o EF não
tem metadado de trigger, então não dá para derivar de `db.Model`". Falso — `HasTrigger`
existe desde o EF Core 7, e a versão em uso aqui é a 8.0.11. Ou seja, a regra escrita para
combater afirmação-que-o-código-não-sustenta nasceu com uma. Antes de escrever "a ferramenta
não permite X", procure X na documentação da versão que você está usando.

**Duas armadilhas de `SqlQueryRaw` achadas escrevendo esta sonda**, ambas provadas contra
Postgres real e ambas silenciosas em compilação: (1) passar um `string[]` direto para
`SqlQueryRaw(string, params object[])` sofre **covariância de array** e vira um parâmetro
por elemento em vez de um `text[]` — embrulhe em `new object[] { array }`; (2) `SqlQueryRaw<T>`
escalar exige a coluna com alias `AS "Value"` assim que qualquer operador LINQ compõe a
consulta (`SingleAsync`, por exemplo, a envelopa em `SELECT t."Value" FROM (...) AS t`) —
sem o alias, falha em runtime. Numa delas o `catch` do health check engoliu o erro e deixou
o readiness permanentemente `Unhealthy`: é a direção segura de falhar, mas o motivo real só
aparece em log.

### 10.23. FK composta faz o EF gerar um índice que ninguém nomeou

Ao criar FK cujas colunas não são prefixo de nenhum índice existente, o EF Core auto-gera
o índice de cobertura com o nome default em PascalCase — `IX_tabela_col1_col2_col3` —
violando a §3 sem que ninguém tenha escrito uma linha errada.

**Por quê:** no F2 do `operacoes`, trocar a FK de estorno por composta produziu
`IX_operacoes_estorna_operacao_id_cliente_id_instrumento_id`. Passou pelo executor, por
uma auditoria de conformidade inteira, pela revisão adversarial e pelo orquestrador. O
índice não aparece na configuration (ninguém o escreveu), só na migration gerada e no
snapshot; e o teste que varria índices únicos não pegava, porque este não é único.

O molde não protege contra isto: no `hub-precos` as FKs são sempre a coluna líder da
própria PK composta, então o EF nunca precisa gerar índice de suporte. É lacuna que
comparar com o molde por fidelidade **não** revela, porque o molde não tem o caso — o
complemento exato da §10.10.

**Guarda:** declare o índice de cobertura com `HasDatabaseName` sempre que a FK não
coincidir com prefixo de índice existente. E, melhor que lembrar disso, tenha um teste
que varra `pg_indexes` e reprove **qualquer** índice fora da convenção: nomear um índice
hoje não impede o EF de gerar outro amanhã.

### 10.24. Campo opcional que usa `null` para "ausente" precisa de guarda contra vazio

Se `null` significa "sem valor" numa coluna nullable, então string vazia ou só espaços é
**entrada malformada**, não sinônimo de `null`. Rejeite explicitamente, com erro próprio —
e não normalize para `null` em silêncio, que esconde bug do chamador.

**Por quê:** no F2 do `operacoes`, `Operacao.Create(..., tipo: Aporte, estornaOperacaoId:
"   ")` devolvia **sucesso**, guardando os espaços. O banco discorda: `'   '` é `IS NOT
NULL`, então `ck_operacoes_estorno_coerente` exige `operacao = 'estorno'` e o INSERT falha.
Domínio e banco davam vereditos opostos para a mesma entrada — exatamente o 500-em-vez-de-4xx
que aquela validação de Domínio existia para impedir. Nenhum teste cobria "string não-nula
porém vazia", que é o buraco clássico entre `!= null` e `IsNullOrWhiteSpace`.

**E normalize os identificadores todos, não um.** A correção inicial trimou só o campo que
tinha mordido; `id`, `cliente_id` e `instrumento_id` ficaram sem trim no mesmo método, numa
tabela append-only onde `"op-1"` e `"op-1 "` viram duas linhas distintas **para sempre**. É a
seção "Normalizar de um lado só" do `LEIA-ME-KIT.md` acontecendo dentro do código que a
combatia. Cuidado com o efeito colateral: a comparação de auto-referência tem que usar o
valor **já normalizado**, senão `" op-1 "` como id e `"op-1"` como referência escapam da
guarda.

**Guarda:** para todo campo `string?` opcional, um teste por valor de fronteira (`null`,
`""`, `"   "`, com espaço em volta, e o caso válido) **cruzando as duas camadas** — provando
que Domínio e banco dão o mesmo veredito, não que cada um funciona sozinho. Ref:
`SchemaTests.DominioEBanco_ConcordamSobreEstornaOperacaoId`.

**Onde fica a fronteira com o "grava-se crua" da §3** (ler as duas sem isto permite concluir
o oposto): normalizar identidade de outro contexto é **só `Trim()`** — remover ruído de
transporte, que nenhum dono de identidade trata como parte do valor. **Nunca transformar o
valor**: baixar caixa, reordenar, reescrever separador. Canonização é política do dono do
conceito, e reimplementá-la aqui é presumir regra alheia que pode mudar sem aviso. O molde
`Hub.Domain.Instrumentos.InstrumentoId` faz `Trim().ToLowerInvariant()` porque o slug é
**dele**; quem só guarda o id do Hub para de propósito no trim.

### 10.25. `numeric(p,s)` rejeita o que o Domínio aceita — e o caso de ESCALA é pior que o de magnitude

Coluna `numeric(p,s)` impõe duas restrições, não uma, e o Domínio precisa validar as duas.
A de magnitude falha alto; a de escala **não falha**.

**Por quê:** achado em revisão adversarial no F3 do `operacoes`. `quantidade: 12345678901.1`
contra `numeric(18,8)` (que aceita 10 dígitos antes do ponto) produz `22003`
(`numeric_field_overflow`), que o `AppDbContext` não traduzia — 500 com `code: Erro.Interno`,
onde o contrato exige 422. Esse é o caso barulhento.

O silencioso é o outro: `quantidade: 1.123456789` contra escala 8 **não dá erro nenhum**. O
Postgres arredonda e grava `1.12345679`. O cliente recebe 201, o `TradeRegistered` sai com o
valor arredondado, e numa tabela append-only não há UPDATE para corrigir. Ninguém é avisado —
é o antipadrão da §10.22 (afirmar cobertura que não existe) em forma de dado.

**Guarda:** valide magnitude **e** escala no Domínio, para **todas** as colunas `numeric`, e
some a tradução de `NumericValueOutOfRange` no `SaveChangesAsync` como rede — o mesmo par
"Domínio para 4xx, banco para todo escritor" da §10.21. Detecte escala com
`decimal.Round(valor, escala) != valor`: se o arredondamento não muda o valor, não houve perda,
qualquer que seja o `MidpointRounding` — verificado por varredura, o modo de desempate só
importa quando o round já altera o valor, e aí a guarda rejeita.

**E confronte as constantes com o banco.** Os limites vivem no Domínio (que não pode depender
da Infrastructure), então `p` e `s` ficam escritos em dois lugares. Um teste tem que ler
`numeric_precision`/`numeric_scale` do `information_schema` e comparar com as constantes —
sem isso as duas cópias divergem em silêncio na primeira migration que mudar a coluna, que é
a §10.23 outra vez.

**Corolário sobre onde o defeito nasceu:** o `hub-precos` não tem endpoint de escrita, então
nenhum valor decimal do molde jamais atravessou um contrato HTTP vindo de fora. Comparar com
o molde por fidelidade **não** revelaria isto — complemento exato da §10.10.

### 10.26. Publisher confirms de um lote se aguardam UM A UM, não em bloco

Ao publicar um lote com publisher confirms ligados, aguarde a confirmação de cada mensagem
**antes de publicar a próxima**. O padrão "dispara todas, empilha as tasks, aguarda depois
contando" publica fisicamente o que você não vai marcar.

**Por quê:** achado em revisão adversarial no F4 do `operacoes`, provado contra broker real.
O relay lê `WHERE publicado_em IS NULL ORDER BY id LIMIT n` e marca só o **maior prefixo
contíguo confirmado** — regra correta, e ela não era o problema. O problema é que as
mensagens do lote saíam todas juntas antes do primeiro `await`. Quando a mensagem de menor
`id` falha de forma **persistente** (fila destino com `x-max-length` + `x-overflow=reject-publish`
cheia, por consumidor parado), o resultado é:

1. o publisher estoura no `await` do índice 0, com `confirmados == 0`, e devolve falha;
2. o handler não marca nada — corretamente, do ponto de vista dele;
3. mas as mensagens 2..N **já foram aceitas pelo broker**, porque foram disparadas antes;
4. no tick seguinte o mesmo lote é relido (nada foi marcado) e 2..N são republicadas.

Indefinidamente. O backlog atrás da mensagem-veneno nunca avança e as mensagens boas são
amplificadas sem limite — medido: 3 ciclos sobre `[restrita, livre, livre]` deixaram **6**
mensagens na fila livre. Isso **não** é o at-least-once que a ADR-3 aceita: at-least-once é
duplicata limitada por uma janela de crash, não um laço infinito de reenvio.

**Guarda:** `await` de cada publish dentro do laço. Aí `confirmados` passa a ser exatamente o
prefixo contíguo **realmente entregue**, que é o que o handler já assumia. O custo é o
pipelining dos confirms — irrelevante quando o ciclo é de segundos e o lote de centenas: a
ordem de grandeza de 100 confirms sequenciais em rede local é de centenas de milissegundos
contra um ciclo de 5 s. **Estimativa, não medição** — o maior lote coberto por teste aqui tem
5 mensagens; se algum dia o lote crescer, meça antes de confiar nesta frase.

**Não tente pular o veneno.** Marcar 2..N e deixar a 1 pendente parece resolver e quebra o
contrato: dedupe por chave natural dá **idempotência, não comutatividade**. Um `estorno` que
chega antes do trade que ele estorna faz o lookup de `ref_externa` não encontrar nada, e a
Custódia grava `ref_estorno = NULL` — um ajuste apontando para o nada, que a reentrega nunca
conserta, porque `UNIQUE (cliente_id, ref_externa)` torna o retry um no-op.

**Política de mensagem-veneno** (contador de tentativas, parking, DLX) é peça própria, que a
ARQUITETURA não prevê. Enquanto ela não existe, é a serialização que torna a ausência dela
sobrevivível: o pior caso vira **estacionar visivelmente** — o backlog envelhece e o alerta de
idade dispara — em vez de amplificar em silêncio. Em at-least-once, parar é o modo de falha
certo.

**Corolário sobre o rótulo:** `confirmados == 0` não significa "broker fora do ar". Separe
`PublicacaoRejeitada` (nack: broker de pé, fila destino recusando) de `BrokerIndisponivel`
(não conectou) — os dois mandam o operador para lados opostos, e o alerta que não distingue
manda para o errado. Descubra o tipo da exceção **empiricamente** contra o broker real, nunca
por suposição: aqui é `PublishException`, e ela também cobriria `basic.return` se o publish
usasse `mandatory: true`.

**Corolário sobre o molde:** este código é porte fiel do `hub-precos`, e o defeito está lá
também — verificado lendo `../hub-precos/src/Hub.Infrastructure/Messaging/RabbitMqEventPublisher.cs`,
que tem o mesmo "dispara todas, aguarda depois". E lá ele é mais exposto por natureza, não por
volume medido: o Hub ingere preços continuamente, enquanto esta outbox só recebe evento por
ação manual de um usuário. Fidelidade ao molde não é motivo para replicar
defeito provado — é desvio **por correção**, e ele pede tarefa no repo de origem. Ver §10.10
e §10.20: comparar com o molde acha o que faltou e o que sobrou copiado, mas **não acha o que
está errado nos dois**.

### 10.27. Cache-Control de leitura variável por cliente não pode herdar o `public` do molde

Quando a resposta de um endpoint de leitura varia por um parâmetro controlado pelo próprio
cliente (ex.: `clienteId` na query), o `Cache-Control` do `ConditionalGetFilter` não pode ser
`public`, e não pode ser um valor cravado em constante — tem que ser parametrizável, com
default `private, max-age=<TTL do dado que ele descreve>`.

**Por quê:** o `Hub.API.Http.ConditionalGetFilter` crava `"public, max-age=300"` porque no Hub
nada no corpo da resposta depende de quem pergunta — qualquer cliente pode receber a mesma
resposta cacheada por um proxy compartilhado. No `operacoes` o primeiro consumidor do filtro
(F5, catálogo de instrumentos filtrado por `clienteId`) tem exatamente essa dependência:
`public` autorizaria um cache compartilhado (proxy, CDN) a servir a lista de um cliente para
outro — vazamento de dado entre clientes por um header herdado do molde sem checar a premissa
dele. E `max-age=300` (5 min) afirma frescor sobre um dado que só sustentamos por 60 s (o TTL do
cache do catálogo do Hub, `Caching:CatalogoInstrumentos`) — é a §10.16 ("cache sem prazo é
afirmação que nunca é confrontada") na forma de um prazo maior do que o dado de fato tem.

**Guarda:** `ConditionalGetFilter` injeta a diretiva via `IConfiguration` (`Http:CacheControl`,
default `private, max-age=60`), nunca uma constante. Teste que trava o default: a resposta
contém `private` e não contém `public`. Ref: `API/Http/ConditionalGetFilter.cs`,
`API.Tests/Http/ReadEndpointPipelineTests.cs`.

### 10.28. Token de versão para conteúdo de outro serviço precisa de componente temporal, não só do banco local

Quando o dado que um endpoint serve não é só o que está no banco local — vem também de um
catálogo remoto cacheado —, o `IContentVersionProvider` não pode derivar a versão só das
tabelas locais. Ele tem que somar um "balde" de tempo (`agora.Ticks / ttl.Ticks`), com o MESMO
TTL do cache do dado remoto.

**Por quê:** o `Hub.Infrastructure.Http.ContentVersionProvider` deriva a versão só do banco dele
(`max(observado_em)`, `count(*)`, `max(criado_em)`) porque lá tudo que o endpoint serve está no
banco do Hub. Copiar essa forma para o `operacoes` seria um defeito silencioso e grave: o F5
serve o catálogo de instrumentos do Hub (via `IHubCatalogoClient`, cacheado por
`Caching:CatalogoInstrumentos`), e uma versão que só olha para a tabela `operacoes` nunca muda
quando o catálogo do Hub muda — um cliente que manda `If-None-Match` ficaria preso a `304` para
sempre sobre um conteúdo externo que a versão não observa. É o mesmo incidente da §10.16 (ETag
persistido que nunca expira), na forma inversa: aqui o problema não é o cache não ter prazo, é o
token de versão não enxergar uma fonte de dado que ele deveria representar.

**Guarda:** a versão soma duas partes — a local (Dapper, `max(registrado_em)`/`count(*)` de
`operacoes`) e o balde temporal com o TTL do catálogo (`Caching:CatalogoInstrumentos`, default
60 s — a MESMA chave que o cache do catálogo usa, para as duas nunca divergirem). Custo aceito:
até um `200` espúrio por TTL quando nada mudou de fato — honesto, porque o dado é externo e o
balde é o que torna a afirmação confrontável. Ref: `Infrastructure/Http/ContentVersionProvider.cs`,
`API.Tests/Integration/ContentVersionProviderIntegrationTests.cs`.

**Correção do próprio limite, achada em revisão adversarial depois que este item já estava
escrito:** o pior caso de obsolescência NÃO é o TTL do catálogo, é a **soma de dois TTLs**. O
`CachedContentVersionProvider` cacheia a string de versão por mais 10 s
(`Caching:ContentVersion`), e essa string já carrega o balde dentro dela — então, quando o balde
vira, a versão velha ainda pode ser servida por até o TTL dela. O limite real é
`Caching:CatalogoInstrumentos + Caching:ContentVersion`, hoje 60 s + 10 s = **70 s**. Continua
limitado, que é o que importa em relação à §10.16, mas quem calcular a janela pelo número que
estava escrito aqui erra por 10 s.

**A lição, que é maior que os 10 s:** dois caches em série multiplicam-se em janela de
obsolescência, e o de fora esconde o de dentro. Ao empilhar cache sobre cache, o prazo que vale
para quem lê a resposta é a SOMA dos prazos, nunca o menor deles nem o do mais próximo do dado —
e é a soma que tem que ser escrita, porque é ela que alguém vai usar para dimensionar `max-age`,
alerta ou janela de reconciliação. Aqui o `max-age=60` da §10.27 é, portanto, ligeiramente
otimista em relação ao pior caso do servidor; é conservador na direção segura (o cliente
revalida antes do servidor mudar de ideia), mas foi coincidência, não projeto.

### 10.29. Cache compartilhado com um caminho de escrita não pode ter last-known-good

Quando o mesmo cliente cacheado serve uma LEITURA de conveniência e a VALIDAÇÃO de uma escrita, a
política de cache passa a ser decidida pelo caminho mais estrito dos dois. Fallback
last-known-good, que é correto para a leitura, vira aceitação de escrita não validada.

**Por quê:** a §4 deste catálogo pede, para dado externo, "fallback explícito e não-silencioso
(fresh + last-known-good)" — e o `CachedProjecaoMercadoService` do `tesouro-direto-api` é o molde
citado. No F5 do `operacoes` a auditoria de conformidade cobrou justamente essa ausência no
`CachedHubCatalogoClient`, com razão à luz da §4 isolada. Só que no F5 a decisão de projeto foi
unificar lista e validação num único método de `IHubCatalogoClient`, para que o invariante "mesma
origem para lista e validação" (ARQUITETURA §6) valesse **por construção** e não por disciplina.
Feita essa unificação, servir catálogo velho quando o Hub está fora faria o `POST /operacoes`
**aceitar um instrumento que o Hub não confirmou** — exatamente o que a ADR-11 proíbe ("nunca
aceitar sem validar, nunca rejeitar como inexistente por falha de infraestrutura"). O 503 honesto
é o comportamento certo, e a §4 cede para a ADR quando as duas colidem.

**A lição geral, que é maior que este caso:** unificar dois caminhos por baixo faz cada um herdar
as restrições do outro. O ganho — o invariante virar construção — é real, e o custo também: a
partir da unificação, toda política aplicada no ponto comum (cache, fallback, retry, timeout,
normalização) tem que ser avaliada contra o caminho MAIS estrito, não contra aquele que motivou a
mudança. Ao unificar, liste os dois consumidores e releia as restrições de cada um.

**Guarda:** o cache do catálogo tem TTL e nada mais — sem par fresh/LKG, sem campo de origem.
Ausência decidida, não esquecida. Se um dia a leitura precisar de degradação graciosa, ela não
pode voltar por baixo, no cliente compartilhado: teria que ser uma camada acima, exclusiva do
caminho de leitura, e aí o invariante da §6 precisa ser re-provado. Ref:
`Infrastructure/Caching/CachedHubCatalogoClient.cs`, ADR-11.

### 10.30. GET condicional serve sonda, não busca por termo

`If-None-Match` contra um provedor que expõe ETag (§4) vale quando se bate na MESMA URL
repetidamente. Para busca parametrizada por texto que o usuário digita, o store de ETag por termo
tem acerto baixo e traz de volta uma peça com histórico de incidente.

**Por quê:** o molde do consumo condicional é o `TdApiClient` do `hub-precos`, e ele é um JOB de
ingestão: sonda a mesma URL a cada ciclo, então o `If-None-Match` quase sempre acerta e evita
baixar um corpo caro. O `HubCatalogoClient` do `operacoes` é o oposto — a URL carrega o termo
digitado, cada termo novo é um miss garantido, e os corpos são pequenos (matches de autocomplete).
O que colapsa a rajada de quem está digitando é o cache de 60 s (§10.29), não o ETag. E o
`ConditionalGetStore` é justamente a peça que causou o incidente da §10.16 quando ficou sem prazo.

**Guarda:** ausência decidida e registrada. Se algum dia o `operacoes` passar a sondar uma URL fixa
do Hub em ciclo (um refresh periódico do catálogo inteiro, por exemplo), aí o molde do
`TdApiClient` volta a valer e o store precisa nascer com prazo, cap e evicção — as três coisas que
a §10.16 cobra.

### 10.31. Coleta paginada tem que distinguir "parei porque acabou" de "parei porque bati num limite"

Todo laço que coleta páginas de um serviço externo tem **mais de uma** condição de parada. Cada
uma precisa ser classificada como **completude** (coletei tudo que existe) ou **limite** (parei
antes do fim), e as duas têm que produzir resultados **diferentes**: completude devolve sucesso;
limite devolve falha. Parada por limite devolvida como `Result.Success` é um conjunto parcial
apresentado como completo.

**Por quê:** no F5 do `operacoes` este mesmo defeito foi encontrado **três vezes, por três portas
diferentes**, cada uma numa rodada de revisão distinta, sempre com a suíte verde:

1. **Teto de páginas.** `while (page <= MaxPaginas)` parava em 100 páginas e devolvia
   `Success` com o que tinha. E o teste existente **documentava e aprovava** o truncamento,
   afirmando só `IsSuccess == true`.
2. **Descarte contaminando a contagem.** Ao acrescentar o descarte de item com `id` nulo, a soma
   do total coletado podia passar a contar só os itens mantidos — fazendo um descarte legítimo
   parecer truncamento (503 indevido), ou o inverso. Só a ordem "somar o bruto **antes** de
   filtrar" separa as duas coisas, e ela não tinha teste que a travasse.
3. **Header de total não confiável.** `X-Total-Count` era aceito por qualquer valor que passasse
   no `int.TryParse`. Com `0`, negativo, ou qualquer número **menor que o já coletado**, a
   condição de corte antecipado (`totalBruto >= totalAnunciado`) ficava verdadeira ao fim da
   primeira página cheia: o laço encerrava, a página seguinte **nunca era pedida**, e o retorno
   era `Success` com o catálogo truncado. Sem log nenhum — o ramo de erro compara
   `totalBruto < totalAnunciado`, que aqui é falso por construção.

O caso 3 é o mais instrutivo porque o dado que mentia vinha de **fora**: a guarda existia, mas
guardava contra o valor ausente e não contra o valor errado — a §10.16 e a lição da guarda de
variável vazia, outra vez, agora num header HTTP.

**A gravidade não é "faltam itens na lista".** Quando o mesmo cliente serve a leitura e a
validação de escrita (§10.29), o item silenciado deixa de existir para os dois: some do
autocomplete **e** é rejeitado como inexistente pelo `POST`. Isso é o 422 mentiroso que a ADR-11
proíbe explicitamente — "nunca rejeitar como inexistente por falha de infraestrutura". Truncar em
silêncio não degrada a leitura; corrompe a escrita.

**Guarda:**
- Enumere as condições de parada do laço e classifique **cada uma**. No `HubCatalogoClient` são
  quatro: página parcial (completude), total anunciado atingido na igualdade exata (completude),
  teto de páginas (limite → falha), total anunciado maior que o coletado ao fim (limite → falha).
- Metadado vindo do outro serviço só vale enquanto for **consistente com o que você já viu**:
  aceite `X-Total-Count` apenas se `> 0`, e **descarte-o com `LogWarning`** se em algum momento o
  coletado ultrapassá-lo. Descartar faz coletar **mais**, nunca menos — é a direção segura.
- Separe os rótulos de erro, como manda o corolário da §10.26: `Hub.Indisponivel` (transitório,
  "tente novamente" é honesto) × `Hub.ColetaIncompleta` (estrutural e determinístico, onde repetir
  dá o mesmo resultado). Os dois são 503; o que muda é o `code`, e é ele que manda o operador para
  o lado certo.
- Teste o **boundary**, não só o caso fácil: total anunciado múltiplo exato do `pageSize` com a
  última página **cheia** é o único cenário que exercita o corte por igualdade — todos os casos
  de "o total bate" com página parcial encerram pelo outro ramo e deixam esse código sem
  cobertura. Ref: `Infrastructure/Catalogo/HubCatalogoClient.cs`,
  `Infrastructure.Tests/Catalogo/HubCatalogoClientTests.cs`.

**Nota sobre mutante equivalente, para quem for medir a suíte:** depois da guarda de descarte
existir, trocar `totalBruto == totalConhecido` por `>=` **não** quebra nenhum teste, e isso está
certo — o descarte garante que, naquele ponto, `totalBruto` nunca excede o total conhecido, então
as duas formas são semanticamente idênticas. Mutação que sobrevive nem sempre é buraco de
cobertura; às vezes é redundância. A guarda que sustenta a correção é o descarte, e essa tem
teste que a trava.

### 10.32. Quem sabe a diferença é quem deve marcá-la, não quem consome

Se um produtor já distingue dois casos estruturalmente, ele tem que **marcar** essa distinção no
dado. O consumidor não pode re-derivá-la por heurística sobre o conteúdo — código de erro,
mensagem, tipo, prefixo de id. Toda re-derivação apodrece no primeiro caso novo, e apodrece em
silêncio.

**Por quê:** achado no `hub-precos` ao portar para lá a §10.31, e a lição é geral. O
`IngerirPrecosTdCommandHandler` decidia se um instrumento tinha sido truncado comparando o
`Error.Code` contra uma **allowlist de dois códigos**. Ela já nascera incompleta — um terceiro erro
que existia desde sempre nunca esteve na lista. Inverter para "tudo que não for o erro de linha"
pareceu resolver e **gerou regressão na mesma branch**: não cobria um segundo erro de linha, que
passou a inflar a métrica de falha. Seria a terceira encarnação da mesma lista.

O teste escrito para proteger a lista varria por reflexão **um** catálogo de erros, e o erro que
causou a regressão vinha de **outro** — o teste era cego exatamente para a direção do defeito.
Guarda que só enxerga um catálogo não guarda contra o outro.

E o produtor **já sabia a resposta**: no adapter, erro de stream é seguido de `break` e erro de
linha de `continue`. A informação existia, estruturada, e estava sendo jogada fora para ser
adivinhada rio abaixo.

**Guarda:** faça a distinção viajar com o dado, num campo **obrigatório** (sem default — default
faz um produtor futuro esquecer de marcar, em silêncio, que é o mesmo default inseguro de origem),
preenchido no ponto onde ela é conhecida. O consumidor decide só por ele.

**E teste contra a re-derivação, não contra o caso:** o teste tem que usar valores que uma
decisão-por-conteúdo classificaria **ao contrário** da marcação, **nos dois sentidos**. Marcar
"trunca" num item cujo código a lista antiga consideraria de linha, e "não trunca" num cujo código
ela consideraria de stream. Assim qualquer reintrodução reprova nos dois casos. Um teste com valor
neutro — um código inventado que nenhuma lista conhece — reprova por coincidência em só um dos
lados, e some no dia em que alguém apagar aquele lado.

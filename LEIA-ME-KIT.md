# Kit de padrões — como instalar em cada repo novo (hub, operacoes, custodia)

1. Copie para a raiz do repo novo: `CLAUDE.md`, `PADROES.md`, **`LEIA-ME-KIT.md`
   (este arquivo)** e `.claude/agents/` (5 agents: executor, tarefas-leves, advisor,
   revisor, guardiao-padroes). Este arquivo vai junto de propósito: o `PADROES.md`
   carrega a regra, mas é aqui que estão o objetivo do F1, as armadilhas de infra e
   os erros de quem conduz — sem ele o repo novo herda o "o quê" e perde o "como não
   repetir".
2. Clone o **molde** como irmão: `git clone <hub-precos> ../hub-precos` (somente
   leitura — jamais editar por aqui). O nome do diretório precisa ser exatamente
   `hub-precos`, conforme declarado em `CLAUDE.md` — divergência aí impede os agents de
   localizar o molde. Clone também o `tesouro-direto` como `../tesouro-direto-api`: ele
   é **referência secundária**, para os padrões que o hub não tem (projeto `*.Web`,
   testes E2E, testes de carga).
3. Na sessão do Claude Code: `/add-dir ../hub-precos` e `/add-dir ../tesouro-direto-api`
   (dá aos agents acesso de leitura aos dois). Confirme os agents com `/agents`.
4. Memória: `claude mcp add memoria -- npx -y @modelcontextprotocol/server-memory`
   e semeie com as ADRs 1–12 do plano de arquitetura (cole a seção 10 do
   `plano-hub-custodia.md` e peça para gravar como entidades/relações).
   **A memória é por caminho de projeto** — o repo novo nasce com a dele vazia, e nada
   do `hub` atravessa sozinho. Além das ADRs, semeie as lições de processo pedindo:
   *"leia o `LEIA-ME-KIT.md` e grave na memória do projeto: o critério de pronto do F1,
   onde ficam as lições aprendidas, e os limites de recurso da VPS"*. Sem isso, a
   primeira sessão do repo novo não sabe nada do que custou caro aqui.
5. Primeira fase (F1): peça "ultracode: crie o esqueleto da solução seguindo o
   molde do repo de referência" e observe se o guardiao-padroes roda na entrega.
   **O F1 não termina quando compila — termina deployado na VPS, mandando métrica
   para o Grafana.** Ver "O que o F1 tem que alcançar", logo abaixo.
6. **Leia a seção 10 do PADROES.md ANTES de portar qualquer coisa.** As seções 1–9
   vêm do repo de referência; a 10 é o que ele NÃO tem — cada item nasceu de algo
   que quebrou de verdade ao construir o `hub` (colisão de alias DNS derrubando o
   vizinho em produção, credencial provisionada sem verificação, arquivo do molde
   perdido no porte). Ela foi escrita para o próximo repo não repetir. Os itens
   10.12–10.17 vieram da fase de mensageria e são de outra natureza — não são sobre
   porte, são sobre **operar numa máquina pequena e compartilhada**: runtime que não
   enxerga o cgroup, teto de CPU que só corta rajada, serviço novo estourando o
   orçamento do vizinho, cache sem prazo, e commit mergeado que nunca chegou a
   produção. Se o seu repo vai dividir VPS com os outros, esses seis são os que vão
   te pegar.
7. **Se você vai CONDUZIR os agents, leia também a última seção deste arquivo**
   ("Erros de orquestração"). Ela é sobre o que o condutor errou — não o executor —
   e cada item passou por suíte verde antes de alguém notar.

Manutenção: o `PADROES.md` é 1 arquivo, copiado igual entre os repos — quando um
padrão evoluir no molde, atualize-o **no `hub-precos`** e copie para os demais; o
guardião passa a cobrar o novo. Padrão que evoluir no `tesouro-direto` (referência
secundária) entra pelo mesmo caminho, mas passando pelo hub primeiro: é lá que se
decide se vira padrão da plataforma.

---

# O que o F1 tem que alcançar

**O objetivo da primeira fase não é "esqueleto que compila". É esqueleto que está
rodando na VPS e cuja métrica aparece no Grafana** — sem um endpoint de negócio
sequer. Andar antes de correr: o caminho inteiro de entrega funcionando, com nada
dentro.

O `hub` fez ao contrário — esqueleto local primeiro, deploy muito depois — e pagou
caro por isso. Quando o deploy finalmente aconteceu, já havia código de verdade em
cima, e cada problema de infraestrutura chegou junto: colisão de alias DNS derrubando
o vizinho **em produção**, credencial provisionada sem verificação, segredo com quebra
de linha invisível, o scrape do alloy que ninguém tinha configurado. Com um esqueleto
vazio, todos esses seriam de resolução trivial e sem consequência. **Não repita a
ordem.**

E há um ganho que só existe nessa ordem: quando a primeira funcionalidade real
aparecer, o painel já está de pé para mostrá-la. Observabilidade montada depois é
observabilidade que ninguém confere.

## No seu repo

- `Dockerfile` multi-stage e **dois** composes: o local (com o serviço `app`, não só o
  banco — ver armadilha 2) e o de produção.
- **O CI/CD inteiro, funcionando** — não é item de fase futura. Dois jobs: `test`
  (restore, build, testes, **gate de cobertura**, Sonar, e o `docker compose config -q`
  dos dois composes) e `deploy` (SSH, condicionado a `push` na `main` e ao `test`
  passar). Copie o do molde inteiro; o `hub` esqueceu o CI no porte e só notou muito
  depois (§10.10).
- No job de deploy, o que o `hub` aprendeu apanhando e que você deve levar de saída:
  preflight que aborta se a rede ou o container vizinho não existem; normalização dos
  segredos com `tr -d '\r\n'` **na origem** (§10.4); o `.env` escrito com **todas** as
  variáveis obrigatórias no mesmo `printf`, porque ele é reescrito por completo e
  esquecer uma derruba o serviço no `up` seguinte; guarda de colisão de alias por rede
  (§10.1); verificação da credencial **pela rede**, nunca por loopback (§10.2, §10.3);
  e smoke tests no fim que provem o que o healthcheck não prova.
- Nome de serviço **único na rede** que você vai usar, conferido contra os aliases já
  registrados lá — não só único no seu arquivo (§10.1).
- `health`/`metrics` expostos e `Loki__Uri=http://alloy:3100`. Log é *push*: quem
  inicia é a sua aplicação, então ela precisa do endereço do destino.
- **Limites de recurso desde o primeiro deploy.** A VPS é pequena e compartilhada;
  serviço sem teto é serviço que derruba vizinho. Leia 10.12–10.14 do `PADROES` antes
  de escolher os números, e saiba quantos núcleos a máquina tem (`nproc`).
- `.env.example` documentando cada segredo: papel, default e o que quebra sem ele.

## No GitHub

Os secrets do deploy (`VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`) e os do seu serviço.
Cadastre **antes** do primeiro merge: o deploy foi escrito para falhar cedo e com
mensagem clara se faltarem — mas falha.

## No repo do `tesouro-direto` (métrica é *pull*, mora lá)

Quem inicia a conexão precisa do endereço do outro — por isso log resolve no seu repo
e métrica não. São quatro edições, e a terceira é a que todo mundo esquece:

1. alvo do scrape em `infra/alloy/config.alloy`, com `job=<seu-servico>`;
2. dashboard em `infra/grafana/dashboards/`;
3. **o nome do dashboard na lista fixa do `apply-cloud.sh`** — copiar o JSON não basta;
4. regras de alerta como `rules-<seu-repo>.yaml`, **nunca** `rules.yaml`: esse nome já
   é das 21 regras do TD, e o PUT do publicador as sobrescreveria.

Depois rode o `apply-cloud.sh` com `GC_GRAFANA_URL`, `GC_GRAFANA_TOKEN` e
`TELEGRAM_BOT_TOKEN` **exportados na invocação** — o script não lê o `.env`, e a guarda
`${VAR:?}` só testa vazio: um placeholder como `not-configured-local-dev` passa por ela
e deixa o Telegram mudo, para todos os serviços, com o script reportando sucesso.

## Critério de pronto do F1

Cinco provas. A primeira é sobre o **mecanismo**; as outras quatro, sobre o
**resultado** — e nenhuma delas é "o CI ficou verde":

1. **Um merge na `main` deploya sozinho.** Deploy feito à mão não conta: o pipeline é
   entregável do F1, não atalho para ele. Se você subiu por SSH na unha para "ver
   funcionando", o F1 não fechou — só adiou a descoberta dos problemas de pipeline para
   quando já houver código em cima.
2. `curl` no `/health/ready` **pela VPS**, respondendo.
3. A série do seu `job=` visível no Grafana Cloud.
4. O dashboard do seu serviço aparecendo lá, com dados.
5. **Um alerta seu disparando de propósito e chegando no Telegram.** Esta é a única que
   prova a corrente inteira. No `hub` os alertas ficaram semanas sem existir na nuvem
   porque os arquivos nunca tinham sido copiados para o outro repo, e o publicador
   pulava o bloco em silêncio, avisando "ausente — pulando" no meio de uma saída longa.

E um cuidado que o `hub` só aprendeu tarde: **verde no CI não é deployado**. Se o job de
deploy depende do de teste, um teste instável pula o deploy sem alarde nenhum. No `hub`
um PR ficou **12 dias** mergeado e fora do ar por isso. Depois de todo merge, confira o
run de `push`, não só o do PR (§10.17).

Só depois disso escreva a primeira migration.

---

# Armadilhas que custaram tempo no `hub` — leia antes de subir qualquer coisa

Oito coisas que quebraram de verdade ao levar o primeiro repo deste kit até a VPS.
Nenhuma está no repo de referência: ou ele nunca enfrentou o caso, ou tem o mesmo
defeito e ninguém tinha esbarrado ainda. A pior derrubou um serviço em produção.

**1. Nome de serviço no compose vira alias DNS na rede.**
Ao entrar numa rede que já existe (`external: true`), um serviço chamado `app` colide
com o `app` que já está lá. O Docker não trata como erro: guarda os dois IPs sob o mesmo
nome e devolve a lista alternada. Metade das chamadas do vizinho cai em você, sem erro
nem log. Foi assim que o `tesouro-direto` passou a responder de forma intermitente, e
metade das raspagens de métrica dele vinha do Hub, poluindo a série de onde saem 12 das
18 regras de alerta. Já ocupados na `tesouro-net`: `app`, `db`, `web`, `alloy`,
`tesouro-direto-*`. O nome tem que ser único **na rede**, não só no seu arquivo.

**2. Ponha o serviço `app` no compose desde o começo.**
O molde tem. Se o seu `docker-compose.yml` só sobe o banco, a única forma documentada de
rodar vira `dotnet run` — justamente a que exige SDK instalado, user-secrets e
`launchSettings.json`. `docker compose up -d` e pronto é o que quem chega quer, e é o
mais parecido com produção.

**3. Sem `Properties/launchSettings.json`, `dotnet run` não roda.**
Sem ele a aplicação sobe em `Production`, user-secrets não são carregados, e o boot falha
por falta de credencial. O molde tem esse arquivo e é fácil não portar. Corolário: teste
com o comando **cru** do README, nunca com variável de ambiente na frente — foi assim que
essa falta passou por executores e por duas revisões adversariais, e só apareceu quando o
dono rodou o comando de verdade.

**4. Observabilidade se configura no repo do `tesouro-direto`, não no seu.**
São três edições lá: o alvo do scrape em `infra/alloy/config.alloy`, o dashboard em
`infra/grafana/dashboards/` **mais o nome dele na lista fixa do `apply-cloud.sh`** (copiar
o JSON não basta), e a regra de alerta em `infra/grafana/cloud/rules.yaml`.
Log é o oposto e resolve no seu repo: basta `Loki__Uri=http://alloy:3100`. A razão é de
protocolo — log é *push* (quem inicia é a aplicação, então ela precisa do endereço do
destino) e métrica é *pull* (quem inicia é o coletor, então ele precisa do endereço do
alvo). Quem inicia a conexão precisa do endereço do outro.

**5. Segredo que alimenta dois caminhos precisa ser normalizado na origem.**
`docker exec -e` preserva o valor byte a byte; o `.env` do compose é lido **por linha** e
descarta um `\n`/`\r` final. Um segredo colado com quebra de linha faz a role nascer com
uma senha e a aplicação enviar outra. O pior é o diagnóstico: comparar os dois lados a
partir do `.env` mostra que batem, porque a leitura já normalizou. Normalize com
`tr -d '\r\n'` uma vez, antes de qualquer uso.

**6. O SQL que cria a role não cria o database.**
Localmente quem cria é o `POSTGRES_DB` do compose, via entrypoint da imagem. Num cluster
que já está de pé não há entrypoint nenhum, e ninguém cria. Faça o `CREATE DATABASE` num
passo separado — ele não roda dentro de transação nem de bloco `DO`.

**7. Testar credencial por `127.0.0.1` sempre passa.**
O `pg_hba.conf` da imagem oficial do Postgres tem `host all all 127.0.0.1/32 trust`. Por
loopback, qualquer senha autentica — a verificação passaria sempre e seria teatro.
Verificação de credencial tem que sair por um container cliente na mesma rede docker. E
verifique: provisionar sem testar a credencial que você acabou de criar é afirmar sem
evidência, e o defeito reaparece minutos depois, longe da causa.

**8. A ferramenta de teste é ponto único de falha do CI inteiro.**
O Testcontainers puxa o **Ryuk** do Docker Hub uma vez por execução, para faxinar
containers órfãos ao final. Num runner efêmero ele não tem função — a VM é destruída
junto com o job — mas tem custo: quando o Docker Hub devolveu HTTP 500 servindo o
manifesto dele, **163 dos 208 testes quebraram juntos em 9,8s**, inclusive os que nada
tinham a ver com a mudança. Desligue com `TESTCONTAINERS_RYUK_DISABLED=true` no CI. E
saiba reconhecer o padrão: suíte inteira falhando de uma vez, rápido demais para ter
executado, é infraestrutura comum — não é o seu código.

**9. Orçamento do vizinho se confere ANTES do deploy, e é barato.**
Medido na VPS antes de qualquer deploy do `operacoes`, em dois comandos por SSH: `nproc`
= **1**; memória total 1967 MB com 1035 disponíveis; `tesouro-direto-alloy` em **169 MiB
de um teto de 192 MiB — 88%**, e ele é justamente quem recebe carga quando um serviço
novo ganha alvo de scrape. Também medido: `hub-precos-app` roda **sem limite nenhum**
(`HostConfig.Memory=0`), então o GC dele enxerga os 1,9 GB do host e não um cgroup — é a
§10.12 do `PADROES` acontecendo agora, não em retrospecto. E a soma dos tetos de CPU já
configurados chega a **2,7 núcleos num host de 1** — confirma a §10.13: teto rígido ali
não contém nada.
A lição de condução, não de infra: essas três medições custaram **dois comandos por
SSH** e foram feitas **antes** de escolher qualquer número para o serviço novo. É o
contraexemplo de "Dimensionar recurso sem medir, e chamar o número de folgado" — o teto
de 192 MB do serviço novo deixou de ser herdado do hub e passou a ter base própria: o
par equivalente, `hub-precos-app`, usa 95,87 MiB.

---

# Erros de orquestração no `hub` — o que o CONDUTOR errou, não o executor

A seção acima é sobre armadilhas do código. Esta é sobre erros de quem coordena os
agents. Todos aconteceram de verdade, ao construir o `hub`, e todos passaram pela
suíte verde — foram achados por revisão adversarial, por auditoria de conformidade, ou
pelo dono lendo o código. Se você vai orquestrar `operacoes` ou `custodia`, é aqui que
você vai errar.

## Especificar porta lendo a implementação, não a interface

O erro-mãe, e o que gerou três dos seguintes. Ao escrever a assinatura de uma porta no
prompt do executor, eu abria o `*ReadRepository.cs` do molde — a implementação — e
copiava a forma de lá. A implementação mostra o SQL; **só o arquivo `I*.cs` mostra o
contrato.**

Consequências concretas:

- **Portas de leitura sem `Result<T>`.** No molde é uniforme: seis métodos entre
  `ITituloReadRepository` e `IPrecoTaxaReadRepository`, todos `Task<Result<...>>`.
  Escrevi as do `hub` com tipo cru. Porta de leitura com tipo cru não reporta falha sem
  exceção, e força o chamador a engolir o erro ou usar try/catch de fluxo.
- **Primitivo onde existe value object.** As portas de escrita do próprio `hub` já
  usavam VO; as de leitura recebiam `string fonte, string classe`. Incoerência dentro
  do mesmo repo.
- **Parâmetros demais, e transponíveis.** `ObterPaginaDoCatalogoAsync(string? classe,
  string? busca, int skip, int take, ct)` — **dois pares adjacentes do mesmo tipo**. Um
  chamador passando `(busca, classe, take, skip)` compila e está errado, em silêncio.
  O molde tem no máximo 3 parâmetros antes do `CancellationToken`, em 28 métodos. Eu
  não conferi essa distribuição antes de escrever a minha.

**Regra:** antes de escrever qualquer assinatura num prompt, abra o `I*.cs` equivalente
no molde e copie a forma de lá. Conte os parâmetros. Se dois adjacentes têm o mesmo
tipo, encapsule ou reordene.

## Mandar o executor violar a camada

Três vezes eu especifiquei algo que quebrava a arquitetura, e o executor teve que me
corrigir (ou o revisor pegou depois):

- **`try/catch` na Application.** O molde tem **zero** `catch` em Domain e Application —
  verifique com `grep -rn "catch" src/*.Domain src/*.Application` antes de assumir.
  Todo tratamento vive na Infrastructure, onde a exceção nasce.
- **Capturar violação de índice único dentro do handler.** Faria a Application conhecer
  `DbUpdateException`/`PostgresException`. O molde põe isso no repositório
  (`UsuarioWriteRepository.AddOrGetExistingAsync`). O executor achou o molde certo e me
  corrigiu.
- **`try/catch` no read repository, quando o dono cobrou o `Result`.** Pareceu certo:
  dar caminho de falha real ao `Result`. Só que o `Error` herdava
  `ErrorType.Validation`, que mapeia para **HTTP 400** — então falha de banco virava
  "Requisição inválida" com `ex.Message` cru no `detail`. Era inofensivo enquanto só o
  job consumia; virou vazamento ao encostar num endpoint.

**Regra:** falha de infraestrutura é 500 pelo handler global, não `Result` de 400.
`Result` é para falha **esperada** de regra de negócio.

## Normalizar de um lado só

Mandei aplicar `Trim()` na guarda de boot da API key e não no middleware. Resultado: um
espaço acidental no fim da chave fazia a guarda **aprovar o boot** enquanto o middleware
rejeitava **todo cliente legítimo** — com 401 indistinguível de "chave errada", sem
pista nenhuma da causa. É a §10.4 do `PADROES` acontecendo por mão própria.

**Regra:** segredo que alimenta dois caminhos, normalize **uma vez na origem** e faça os
dois consumirem o mesmo valor. E aplique também no ponto de consumo, porque `Trim()` é
idempotente e blinda contra reordenamento futuro.

## Escrever contrato assumindo que os campos andam juntos

Especifiquei o `asof` como "a última data com preço e, dessa data, todos os campos".
Errado: o adapter pula campo nulo, então basta um dia sem `taxa_compra` para os campos
dessincronizarem — e aí o `pu_venda` **sumia da resposta** quando outro campo era mais
recente, sem `motivo`, sem log. O forward-fill tem que ser **por campo**.

**Regra:** ao desenhar contrato sobre dados esparsos, pergunte o que acontece quando
uma dimensão existe e a outra não.

## Endossar decisão antes de ter a resposta

Endossei trocar `Equals` por `Contains` numa lista de bloqueio — ganho real contra
placeholder com padding. Só que `Contains` é literal, e `CHANGE_ME_IN_PRODUCTION`
(underscore em vez de hífen) passava liso. Eu tinha **perguntado** sobre falso negativo
ao revisor e endossei antes da resposta chegar.

**Regra:** se você formulou a pergunta é porque desconfia. Espere a resposta.

## Erros de processo com os agents

- **Dois executores em paralelo no mesmo working tree** apagaram arquivos de teste um do
  outro. Delimitar "seus arquivos são estes" no prompt não impede: `Write` sobrescreve o
  arquivo inteiro. E mesmo com arquivos disjuntos, dois `dotnet build` concorrentes
  colidem em `obj/`/`bin/`.
- **O `revisor` NÃO é só-leitura.** Ele muta a implementação de propósito, para provar
  que um teste é vácuo, e reverte. Rodando junto com o `guardiao-padroes`, o guardião leu
  o estado mutado e reportou como defeito real um teste temporário que já não existia.
- **`isolation: "worktree"` não serve para revisar branch.** O worktree nasce da **base**
  (`main`), não do branch em que você está, e commitar antes não resolve. Tentei duas
  vezes; nas duas o revisor concluiu "a entrega não existe" e recomendou devolver ao
  executor — conclusão que, aceita sem conferir, mandaria refazer trabalho pronto.

**Regra:** `Explore` e `guardiao-padroes` são só-leitura e vão em paralelo com qualquer
coisa. O `revisor` roda **sozinho**. Executores que escrevem, um de cada vez.

## Não gravar o desvio que você mesmo aprovou

Aceitei dois desvios do molde (um record de parâmetros que o molde não tem; um índice
que decidi não criar) e não gravei nenhum dos dois. O `guardiao-padroes` cobrou os dois,
com razão: o `CLAUDE.md` exige registro, e desvio não registrado vira precedente
esquecido — o próximo repo copia sem saber que era exceção.

## Deixar o escopo vir da doc em vez do arquivo

Quase entreguei uma etapa pela metade. A fila de tarefas existia só no contexto de uma
sessão e nunca tinha sido commitada; eu me guiei pela ordem de implementação do plano
de arquitetura, que nomeava só um dos dois endpoints da etapa. **Commite o roadmap.**
A próxima sessão lê o escopo do repo, não reconstrói do contexto.

## Deixar o deploy para depois de existir código

Defini o F1 do `hub` como "esqueleto, sem endpoints de negócio" e parei aí — compilava
e subia na minha máquina. O deploy veio fases depois.

Resultado: toda a dor de infraestrutura chegou de uma vez, com código de verdade já em
cima. A colisão de alias DNS **derrubou o vizinho em produção**; o segredo com quebra
de linha fez a role nascer com uma senha e a aplicação enviar outra; o scrape do alloy
não existia, então não havia métrica nenhuma para olhar enquanto se depurava. Com um
esqueleto vazio, cada um desses seria um susto de dez minutos sem consequência.

O custo real não é o tempo: é que problema de deploy e problema de código chegam
misturados, e você não sabe qual está olhando.

**Regra:** o F1 termina **deployado e observável**, não "compilando". Ver "O que o F1
tem que alcançar" na primeira seção deste arquivo — o critério de pronto tem quatro
provas, e a última é um alerta seu chegando no Telegram.

## Dimensionar recurso sem medir, e chamar o número de "folgado"

O advisor recomendou **peso** de CPU (`cpu_shares`), com o argumento de que CPU não se
estoca. Eu mantive o peso e acrescentei um teto rígido de `0.60`, escrevendo no
comentário que era "folgado, só para conter loop desgovernado". O número não veio de
lugar nenhum — nem de medição, nem do molde, nem do advisor.

Duas semanas depois, alerta de throttling. As rajadas do broker batiam em **59,99%**:
cortadas exatamente no teto que eu inventei. E o teto não protegia nada, porque a VPS
tem **um núcleo** — o processo já não podia passar de 1.0.

**Regra:** número de limite ou é medido, ou vem do molde, ou é decisão registrada com o
motivo. "Parece folgado" não é nenhuma das três. E antes de dimensionar qualquer coisa,
saiba quantos núcleos a máquina tem — `nproc` muda o significado de todo teto de CPU.

## Remover uma garantia implícita sem procurar quem dependia dela

Troquei `depends_on: service_healthy` por `service_started` no broker, por um motivo
correto: um broker doente não pode impedir a API de **leitura** de subir. A mudança
estava certa. O que eu não fiz foi perguntar **quem estava se apoiando naquela espera**.

Estava: a verificação de credencial do deploy rodava logo depois do `up -d` e só
funcionava porque o `service_healthy` segurava o retorno. Sem ele, o `curl` bateu numa
porta fechada 6 segundos depois e o deploy reprovou dizendo "credencial diverge" —
diagnóstico errado, para um problema de tempo.

**Regra:** ao remover uma espera, um lock, um `depends_on` ou qualquer ordenação, faça
`grep` por quem vem depois no mesmo fluxo. Garantia implícita não aparece em teste
unitário e não deixa rastro no diff.

## Reintroduzir, por outra porta, o acoplamento que você acabou de evitar

Discuti com cuidado por que o relay **não** entra no `/health/ready`: o readiness
alimenta o healthcheck do container, e broker fora do ar não pode degradar a API de
leitura, que não depende dele. Provei empiricamente. Escrevi no PR.

E aí pus `depends_on: plataforma-rabbitmq: condition: service_healthy` no mesmo serviço
— que faz um broker doente impedir o Hub de subir. É o mesmo acoplamento, com outro
nome, três dezenas de linhas abaixo no mesmo arquivo.

**Regra:** depois de decidir "X não pode depender de Y", procure no arquivo inteiro
todas as formas de X depender de Y. Health check, `depends_on`, ordem de boot, timeout
compartilhado, pool compartilhada.

## Especular em vez de medir — e repetir isso três vezes

Na investigação do throttling levantei três hipóteses e escrevi cada uma com confiança:
pressão de memória do host, busy-wait dos schedulers do Erlang, e o relay abrindo canal
a cada 5s. **As três estavam erradas**, e cada uma custou uma ida e volta com o dono.

O que resolveu foram três medições: `free -m` (descartou memória), `scheduler_wall_time`
(0,19% — a VM estava ociosa, matando o busy-wait) e um laço de `docker stats` que
revelou o **formato** da curva — rajadas paradas exatamente na cota. O formato disse o
que nenhum raciocínio meu tinha dito: era trabalho cortado, não trabalho excessivo.

**Regra:** em diagnóstico de recurso, a primeira ação é medir, não hipotetizar. E
quando a primeira hipótese cai, isso é sinal para medir mais, não para hipotetizar de
novo. Se você já errou duas vezes, pare de propor causa e peça dado.

## Escolher a prova difícil quando existe uma fácil

Pedi a um executor que provasse empiricamente a diferença entre dois healthchecks do
RabbitMQ — medir com timestamps que um fica verde antes de a porta abrir e o outro não.
Ele empacou horas num problema de permissão do próprio arranjo de teste.

A prova era desnecessária. O deploy não precisa saber **quando** o healthcheck fica
verde: ele precisa esperar pela **condição que ele mesmo usa** — a API respondendo. Um
laço em volta da própria verificação se autovalida e dispensa medir qualquer coisa. Ao
matar o agente e fazer eu mesmo, o diff saiu em minutos.

**Regra:** antes de mandar alguém provar uma propriedade do sistema, pergunte se dá
para escrever o código de um jeito que não dependa daquela propriedade. E se um agente
está há muito tempo no mesmo ponto, olhe o que ele está fazendo — o problema pode não
ser a tarefa.

## Aceitar diagnóstico de terceiro sem conferir contra o código

Chegou uma análise externa do teste que estava falhando. Ela acertou **qual** teste era
e errou o resto: propunha aceitar também um código de erro `TdApi.Timeout` que **não
existe no projeto** (`AdapterErrors` tem quatro, e esse não é um deles), sem citar o
valor real da falha — sinal de que não tinha lido o detalhe.

Aplicada, teria trocado um teste vermelho por um teste **cego**: ele passaria justamente
no cenário que existe para reprovar, o do timeout não funcionar.

**Regra:** diagnóstico de fora se confere contra o código antes de virar correção.
Comece pelo mais barato: os identificadores citados existem no repo?

## Registrar em todo lugar, menos no arquivo certo

Gravei as lições desta etapa em mensagem de commit, corpo de PR e no grafo da memória.
Nenhum desses lugares é lido por quem for criar o `custodia`. O `PADROES.md` §10 e esta
seção — os dois arquivos que existem exatamente para isso — ficaram intactos até o dono
perguntar onde estavam as lições.

É a §10.10 do `PADROES` (comparar por ausência) acontecendo com quem escreveu a §10.10.

**Regra:** ao fechar uma etapa, o checklist não é "eu registrei?" e sim "está no arquivo
que a próxima pessoa vai abrir?". Commit e PR são registro de **quando**; `PADROES.md` e
este arquivo são registro de **o que não repetir**.

## Escrever no repo certo e esquecer de rastrear lá

Variante da seção anterior, achada ao conferir a fiação do `operacoes`:
`infra/grafana/cloud/rules-hub.yaml` e `infra/grafana/dashboards/hub-precos.json` — os
arquivos de alerta e dashboard do **próprio hub** — estavam no repo do `tesouro-direto`
como arquivos **não rastreados** (`??` no `git status`), nunca commitados. Não é o erro
de "escrevi no lugar errado": o arquivo nasceu no repo certo e nunca foi versionado, e
por isso some no primeiro clone limpo, e o `apply-cloud.sh` volta a pular o bloco em
silêncio — o mesmo sintoma de "ausente — pulando" que a seção "O que o F1 tem que
alcançar" descreve para um `rules.yaml` que nunca chegou a ser copiado.

**Regra:** depois de escrever arquivo de observabilidade no repo vizinho, rode `git
status` **lá** e confirme que ele está rastreado. "Existe no meu disco" não é "existe no
repo".

## Dois vícios de relato

- **Atribuir ao dono uma decisão que foi inferência sua.** Escrevi "escolha sua" sobre
  algo que eu tinha deduzido da frase dele. Se você inferiu, diga que inferiu.
- **Somar errado e afirmar com confiança.** Reportei uma contagem de testes 10 acima da
  real, com a saída do comando colada logo acima. Confira o número que você acabou de
  escrever contra a saída que você acabou de colar.
- **Ler evidência com viés de confirmação.** Querendo confirmar que um container tinha
  reiniciado, afirmei que "o `MachineName` do log mudou, logo reiniciou". Eu não tinha
  valor anterior para comparar — e `docker restart` preserva o container, então esse
  campo nem mudaria. Inventei uma prova para a conclusão que eu já queria. Antes de
  chamar algo de evidência, pergunte o que ela valeria se a sua hipótese fosse falsa.

## Aceitar "isso é da próxima fase" sem perguntar se dá para consertar depois

No F2 do `operacoes` o executor deixou de fora as validações de negócio de `Operacao.Create`
com a justificativa de que regra de negócio é do F3, e eu aceitei — parecia o recorte certo
de uma fase que era "só schema". O revisor mostrou o que eu não tinha visto: **a mesma fase
tinha tornado o dado irreparável**. A trigger de imutabilidade e o índice único parcial de
estorno são decisões do F2, e juntas fazem com que uma linha ruim gravada na janela F2→F3
não tenha conserto — nem por UPDATE, nem por DELETE, e com o evento já publicado rio abaixo.

O argumento que fecha, e que eu deveria ter aplicado sozinho: "regra de negócio é da próxima
fase" já tinha sido vencido dentro do próprio F2, porque `UNIQUE (estorna_operacao_id)`
("uma operação não se estorna duas vezes") **também** é regra de negócio e foi aceita sem
discussão. Aceitar metade da regra e adiar a metade que a torna segura é o que produz o
buraco.

**Regra:** ao aceitar um adiamento de escopo, não pergunte "isto é desta fase?" e sim "**o
que for gravado errado até a próxima fase terá conserto?**". Se a resposta for não, o
adiamento não é recorte de escopo — é dívida sem prazo de pagamento. A versão técnica disso
está no `PADROES.md` §10.21.

## Correção de achado grave é código novo — e precisa das DUAS revisões de novo

No F2, cada rodada de correção produziu um defeito novo, e cada um foi pego por uma
revisão diferente:

- O `guardiao-padroes` aprovou a primeira versão ponto a ponto. O `revisor` então achou
  dois defeitos graves (dado irreparável no estorno; readiness que não pegava drift).
- Corrigidos, mandei **o guardião de novo, só sobre o delta** — e ele achou o que tinha
  passado por ele mesmo, pelo revisor e por mim: a FK que virou composta fez o EF gerar um
  índice em PascalCase que ninguém escreveu (`PADROES.md` §10.23).
- Aí eu **ia fechar**. Só rodei o revisor de novo porque o dono perguntou "revisor já
  rodou?". Tinha rodado — antes das correções. Na segunda passada ele achou mais um
  defeito: `Operacao.Create` aceitava `estorna_operacao_id = "   "` que o banco rejeita,
  ou seja, a validação de Domínio que existia justamente para transformar 500 em 4xx furava
  no caso que ninguém testou. E mostrou que a sonda de drift não cobria a trigger de
  imutabilidade — a guarda central da fase sumindo sem o readiness notar.

Nenhuma revisão anterior foi malfeita: os defeitos **não existiam** quando elas rodaram.
Nasceram das correções.

**Regra:** ao corrigir achado grave, rode **as duas** revisões de novo sobre o delta —
`guardiao-padroes` (conformidade) e `revisor` (comportamento), em série, delimitando
"audite apenas o delta, não reaudite o que já passou". Elas acham coisas diferentes e a
segunda passada de cada uma pagou o próprio custo aqui. O sinal de que você está prestes a
errar isto é a frase "só falta commitar".

**Corolário sobre o que se conta ao dono:** eu ia pedir autorização de commit dizendo
"revisado", com a parte mais delicada — a que nasceu de defeito grave e mexe em constraint
de tabela imutável — sem revisão adversarial nenhuma. Ao relatar, diga **sobre qual versão
do código** cada revisão rodou, não só que rodou.

## Descrever o repo para o executor a partir de uma listagem truncada

Escrevi no despacho que a factory de testes de integração "ainda não existe" e mandei criá-la.
Ela existia desde o F1, junto com outros dez arquivos de teste — eu tinha listado o diretório
com `find | head -100` e a lista foi cortada antes de chegar lá. O executor conferiu, achou a
factory e reaproveitou, e me avisou no relatório; se tivesse obedecido, teria duplicado a
infraestrutura de teste ou sobrescrito a existente.

**Regra:** afirmação sobre o que o repo tem ou não tem, dentro de um prompt, é instrução —
o executor age sobre ela. Antes de escrever "não existe X", rode a busca que responde
exatamente isso (`git ls-files <dir>`, `find` sem `head`), não uma listagem geral truncada.
E prefira dizer "procure X; se não existir, crie" a afirmar a ausência.

## O condutor viola a regra que acabou de escrever

Escrevi a `PADROES.md` §10.22 depois que o revisor mostrou um comentário de código afirmando
cobrir um cenário que não cobria. A regra que tirei disso: **afirmação falsa escrita no
código é pior que a lacuna, porque desliga a desconfiança de quem lê depois.**

Na mesma sessão, no mesmo arquivo, escrevi neste texto que "o EF não tem metadado de
trigger, então essa parte não dá para derivar de `db.Model`". Falso: `TableBuilder.HasTrigger`
existe desde o EF Core 7, e a versão em uso aqui é a 8.0.11. A auditoria seguinte achou.
Ou seja, a regra contra afirmação-sem-lastro nasceu **com** uma.

O padrão, que é o achado de verdade: **os três últimos defeitos desta fase foram meus, não
dos executores** — um trim pedido pela metade, esta afirmação falsa, e uma nota de fecho de
fase desatualizada. Os executores fizeram exatamente o que eu pedi. Revisão adversarial e
auditoria de conformidade estavam apontadas para o código dos executores; ninguém estava
apontado para os meus prompts e os meus textos.

**Regra:** o que o condutor escreve — prompt, comentário, `PADROES.md`, nota de fecho — entra
na revisão junto com o código. Mande o `guardiao-padroes` conferir explicitamente "os textos
que EU escrevi descrevem o que o código faz?", com essas palavras, e liste os arquivos.
E antes de escrever "a ferramenta não permite X", procure X na documentação da **versão que
você está usando** — leva um minuto e é a diferença entre uma regra e uma crença.

## `git ls-files` não lista o que ainda não foi rastreado

Mandei varrer todos os `.cs` do repo para apagar comentários e escrevi o alvo como
`git ls-files '*.cs'`. Ele lista só arquivo **rastreado**. Os 26 arquivos novos da fase em
curso — que eram exatamente os arquivos com o código novo — nunca tinham sido `git add`ados,
então ficaram de fora. Pior: **a minha verificação usou o mesmo comando**, então ela confirmou
o próprio ponto cego e eu relatei ao dono que estava feito. Quem achou foi ele, abrindo um
arquivo no editor.

É a mesma família de "Escrever no repo certo e esquecer de rastrear lá", com a agravante de
que aqui o comando errado apareceu **duas vezes**: na ação e na conferência dela.

**Regra:** varredura que se propõe a cobrir "todos os arquivos" usa
`git ls-files` **mais** `git ls-files --others --exclude-standard`, ou `find`. E a verificação
tem que usar um caminho **diferente** do da ação — verificar com o mesmo comando que executou
não é verificação, é repetição. Se a ação foi por `git ls-files`, confira por `find`.

## Mandar "não reformate" não impede o agente de reformatar

Na mesma varredura, o spec dizia explicitamente "não reindente nem reformate nada além disso".
O agente removeu, junto com os comentários, **1092 linhas em branco** de 74 arquivos — 53 deles
sem um único comentário, ou seja, reescritos à toa. `using` colado no `namespace`, membros
colados uns nos outros.

E o meu teste de integridade não pegou porque eu comparei o "esqueleto de código" **filtrando
linhas em branco antes de comparar**. Verifiquei exatamente a dimensão que não estava em risco.
Build verde e 306 testes passando não diziam nada sobre isso: linha em branco é whitespace, não
muda semântica nenhuma em C#.

**Regra:** quando a instrução é "mude X e só X", o teste de verificação tem que medir **Y** —
a coisa que não era para mudar. Medir X de novo só confirma que a parte pedida aconteceu.
Aqui o certo era contar linhas em branco antes e depois, que é uma linha de shell.

**Corolário barato:** o estrago só foi reversível porque a maioria dos arquivos existia na
`main`. Para os que a fase tinha acabado de alterar, a formatação anterior estava só no working
tree e **se perdeu** — tive que reconstruí-la por heurística. Varredura destrutiva ampla merece
um commit (ou `git stash`) antes, mesmo que o trabalho ainda não esteja pronto para virar
commit de verdade.

## Subagente que terminou continua retomável — e com um retrato velho da árvore

No F3 o executor do POST terminou, entregou, e eu segui trabalhando na MESMA working tree:
varri comentários, mandei corrigir achados do guardião e do revisor, acrescentei código novo.
Horas depois a notificação dele disparou de novo e ele reportou, alarmado, que "outra sessão"
estava mexendo no repo — 40 arquivos modificados que ele não reconhecia, incluindo migrations
já aplicadas. Não havia outra sessão. Era eu, em série, depois que ele parou.

Ele acertou em **não** editar nada e perguntar. O risco, se tivesse agido, era grosso: teria
reescrito por cima do estado atual usando o modelo que ele guardava de antes — devolvendo os
comentários recém-apagados e desfazendo correções que ele nunca viu. E o relatório dele teria
saído com cara de autoridade, descrevendo um repo que não existe mais.

**Regra:** subagente que terminou não é subagente que morreu — ele continua retomável e
carrega um retrato congelado da árvore no instante em que parou. Antes de retomar um agente
(`SendMessage`) depois de ter mexido nos arquivos dele, ou você o informa do que mudou, ou
despacha um agente **novo**. E se a fase vai ter várias rodadas de correção na mesma árvore,
prefira despachar em série e tratar cada rodada como agente novo, em vez de manter um vivo
achando que a árvore é dele.

**Corolário sobre relato:** quando um agente descreve o repo com convicção, pergunte **de
quando** é a leitura dele. Aqui a diferença entre "achado grave" e "alarme falso" era só o
carimbo de tempo.

## Gate de compose no CI passa com dummy — e não protege o `.env` de quem desenvolve

No F3 acrescentei `OPERACOES_HUB_BASE_URL` e `OPERACOES_HUB_API_KEY` ao `docker-compose.yml`
como obrigatórias (`${VAR:?}`). O *Compose config gate* do CI ficou verde, o PR ficou verde,
e mesmo assim o `docker compose` **local do dono estava quebrado** a partir daquele commit:

```
error while interpolating services.operacoes.environment.[]:
required variable OPERACOES_HUB_BASE_URL is missing a value
```

O gate passa porque o job do CI **escreve um `.env` com valores dummy** antes de rodar. Ele
valida a sintaxe do YAML e a existência das tags — não valida que alguém com um `.env` real,
anterior à mudança, continua conseguindo subir. Quem descobriu foi o dono, perguntando outra
coisa.

É a §10.9 noutra roupa: validar com um equivalente (o `.env` sintético do CI) em vez do
literal (o `.env` que existe na máquina). E é gêmeo da §10.20 — a asserção herdada afirma
funcionalidade que o ambiente real não tem.

**Regra:** variável nova obrigatória no compose é **mudança quebrante para todo mundo que já
tem `.env`**. No mesmo commit: acrescente ao `.env.example` (o CI não lê isso), diga no PR que
o `.env` local precisa das linhas novas, e — se puder — rode `docker compose config -q`
**sem** o `.env` do CI, com o arquivo real, para ver o erro que o outro vai ver. Um gate que
constrói o próprio insumo não testa o insumo de ninguém.

## Porta publicada no compose local não existe em produção

Para provar o contrato do Hub contra a VPS, montei o comando com
`http://127.0.0.1:5080` — a porta que o `docker-compose.yml` **local** do hub publica. Na VPS
não há nada ali: o `docker-compose.prod.yml` do hub **não publica porta nenhuma**, de
propósito, porque o Hub é infraestrutura interna consumida serviço-a-serviço (§6 do
`ARQUITETURA.md`). O `docker ps` mostra a diferença numa coluna: `8080/tcp` (só exposta)
contra `127.0.0.1:5000->8080/tcp` (publicada) do vizinho.

Eu tinha lido a §6 e mesmo assim deduzi a topologia de produção do arquivo de
desenvolvimento. O caminho certo era pela rede: `docker exec` de dentro de um container que
já está na `plataforma`, batendo no alias DNS — o que, de quebra, é um teste **melhor**,
porque prova o percurso real do código (resolução de alias + chave) e não só o contrato HTTP.

**Regra:** `docker-compose.yml` e `docker-compose.prod.yml` descrevem topologias diferentes,
e a de produção costuma publicar **menos**. Antes de escrever um endereço de produção, leia o
compose **de produção** — ou, melhor, confirme com `docker ps` na máquina, que é o estado e
não a intenção.

## `curl -s` transforma falha de conexão em saída vazia

O mesmo comando errado voltou **sem nenhuma mensagem** — nem erro, nem código. `-s` (silent)
suprime a mensagem de erro do curl, e sem `-w` ou `--fail` não sobra nada que distinga
"conexão recusada" de "servidor respondeu 200 com corpo vazio". São diagnósticos opostos e a
saída é idêntica.

**Regra:** em comando de verificação, `-sS` (o `S` devolve o erro) e sempre
`-w '%{http_code}'`. Vale a mesma lógica da §10.15: distinguir "não conectou" de "conectou e
recusou" é o que manda o operador para o lado certo. Um comando de prova cuja falha é
silenciosa não é prova.

## Teste manual em tabela append-only deixa lixo que não sai

Para provar o `POST /operacoes` contra produção, mandei o dono fazer um POST real com
`clienteId: cli-teste`. Funcionou — e gravou numa tabela onde `UPDATE` e `DELETE` são
bloqueados por trigger. A linha ficaria **para sempre**, e o relay do F4 publicaria aquele
`TradeRegistered` para a Custódia, que escrituraria uma operação de um cliente que não existe.

O agravante é de quem escreveu a regra: a lição central da mesma fase, na `PADROES.md`
§10.21, é **"se entrar dado errado aqui, dá para consertar depois?"**. Propus o teste sem
fazer a pergunta que eu mesmo tinha formulado.

Saiu barato só por coincidência de timing: as tabelas continham apenas o dado de teste, o F4
ainda não existia, nada tinha sido publicado, e o F2 deixou o `TRUNCATE` aberto de propósito
para fixture (trigger de linha não dispara em TRUNCATE). Com dado real ao lado, a limpeza
teria custado `DISABLE TRIGGER` e perícia manual.

**Regra:** antes de propor um teste de escrita contra produção, pergunte o que ele deixa para
trás e como se remove. Em tabela append-only a resposta costuma ser "nada remove" — então ou
o teste roda em ambiente descartável, ou é a primeira escrita e se limpa com `TRUNCATE`
imediatamente, ou não se faz. **E decida a limpeza ANTES de rodar**, não depois de ver o 201:
depois do 201 a decisão já está tomada por você.

## Reprovar o próprio deploy por causa de um serviço que não é seu

O deploy do `operacoes` verifica, pela rede, que a credencial do broker autentica — §10.2, e
está certo. O erro foi o desfecho: eu tratei `401` (broker de pé, credencial recusada) e
"não respondeu" (broker fora do ar) do mesmo jeito, `exit 1`. Com `script_stop: true`, isso
reprova o job `deploy` inteiro.

Só que o `plataforma-rabbitmq` **é serviço do `hub-precos`**, não deste repo. Um broker fora
do ar passaria a reprovar o nosso deploy — e o job `guarda-deploy`, que existe justamente para
tornar impossível confundir "não deployado" com "deployado" (§10.17), afirmaria **"este commit
NÃO chegou a produção"** com o container no ar e `healthy`. Diagnóstico falso apontando para o
serviço errado — a mesma família de diagnóstico falso da §10.15, mas num eixo diferente: lá o
erro era de **causa** (falta de espera lida como credencial divergente, no mesmo serviço); aqui
é de **atribuição** (falha de um serviço vizinho lida como falha do nosso deploy).

O laço já distinguia os dois códigos para decidir se repetia (`000` repete, `401` falha
rápido) — a distinção existia e eu não a levei até o veredito.

**Regra:** falhe o deploy pelo que é **seu e acionável** (`401` é o secret deste repo
divergindo do que o broker aceitou), e apenas avise no que é de terceiro (broker inacessível
→ `::warning::` e siga; a outbox segura os eventos, nada se perde, e os alertas de runtime já
cobrem). Antes de pôr um `exit 1` num passo de deploy, pergunte: "se isto falhar, a culpa é
deste repositório?". Se não for, o `exit 1` está mentindo sobre o que aconteceu.

## O executor propõe a correção no lugar errado, e ela passa por ser mecânica

Um executor resolveu um estouro do diagnóstico `ManyServiceProvidersCreatedWarning` do EF
Core — que quebrou **três testes pré-existentes e não relacionados** — suprimindo o aviso, e
suprimiu inclusive no `AddInfrastructure`, isto é, **no caminho de produção**. Justificou como
"mecânica, sem impacto em produção, só há um `AddDbContext` por processo real".

A justificativa refuta a correção: se em produção há sempre um provider, o aviso nunca
dispararia lá — a supressão não compra nada, e a única coisa que ela faz é apagar o alarme do
dia em que alguém introduzir um segundo `AddDbContext` ou opções por request. Numa VPS de um
núcleo e ~1 GB, essa regressão futura é OOM. E o desvio ficaria no arquivo que o próximo
serviço copia do molde.

A causa real era o harness: os testes novos subiam **um container Postgres por método** (8),
e o contador de service providers do EF é **global ao processo** — por isso as vítimas eram
outros arquivos, e mudavam a cada execução. Uma collection fixture com um container
compartilhado derrubou o total, e os três testes quebrados **voltaram a passar sem serem
editados** — que é a prova de que a correção atacou a causa e não o sintoma. De quebra, 8
containers viraram 1.

**Regra:** quando a correção proposta fica num arquivo mais central que o problema, isso é
sinal, não conveniência — pergunte onde o problema **nasce**. E teste que quebra outro teste
não relacionado quase nunca é defeito do outro teste: é estado global do processo (contador,
cache estático, variável de ambiente, porta). O `CLAUDE.md` manda desvio do molde passar pelo
`advisor`; foi ele quem desmontou este, e o executor tinha registrado no relatório que decidiu
sozinho — ler o relatório inteiro é o que fez a diferença.

## Duas revisões que não se sobrepõem: a segunda achou o que a primeira não procurava

No F4, `guardiao-padroes` e `revisor` acharam coisas **disjuntas**, e nenhuma das duas teria
achado a da outra:

- o guardião achou um `infra/grafana/README.md` descrevendo um repo que não existe mais
  ("o F1 é esqueleto… só os 10 painéis de infraestrutura", "duas regras") enquanto o diff ao
  lado entregava 14 painéis e 4 regras. Dois executores atualizaram o JSON e o YAML e deixaram
  o texto ao lado. É a §10.20 na sua forma mais barata de cometer;
- o revisor achou o defeito grave — a amplificação da mensagem-veneno (`PADROES.md` §10.26) —
  mutando o código para provar que os testes não eram vácuos, e escrevendo um teste novo
  contra broker real para produzir a perda.

O guardião nunca acharia o segundo (o código era **fiel ao molde** — o molde é que está
errado), e o revisor não estava olhando para README. Isto é a razão de rodar as duas, e de
rodar **em série**: o revisor muta a implementação de propósito, e um guardião lendo esse
estado reporta como defeito real o que já não existe.

**Regra que se confirmou de novo:** correção de achado grave é código novo e pede as duas
revisões outra vez sobre o delta. E peça ao guardião que audite também **os textos** —
README de infra, comentário de workflow, descrição de alerta. Nesta fase, um dos dois defeitos
que ele achou estava num `.md`, não em `.cs`.

## Perder o volume do broker apaga fila e binding, e a outbox não protege contra isso

Na prova em produção do F4, o `PUT` do binding falhou com `no exchange 'prices' in vhost '/'`.
O broker estava sem a topologia da §5.

**Minha primeira conclusão foi errada, e vale mais registrada do que apagada.** Eu inferi "o
relay do `hub` nunca publicou em produção", porque o exchange é declarado na conexão e a
conexão só ocorre na primeira publicação. A inferência é válida; a premissa não foi checada. Um
comando desmentiu: a outbox do `hub` tem **2609 linhas, todas com `publicado_em`**, publicadas
entre 2026-08-24 e 2026-09-05 06:15. O relay dele funcionou. O container do broker foi criado
em 2026-09-05 10:21 — **depois** — e levou a topologia durável junto com o volume. Como a
outbox do `hub` está vazia desde então, nada mais publicou, nada reconectou, nada redeclarou.

Eu escrevi essa conclusão errada no PR, no commit, no `ROADMAP` e aqui, e ela foi mergeada.
É a §10.9 de novo: verifiquei com um equivalente (a ausência do exchange) em vez do literal (a
tabela que diz se publicou).

**O achado verdadeiro é pior que o falso.** Depois de uma recriação do broker:

- o **exchange** volta sozinho, declarado pelo primeiro publicador que conectar;
- a **fila e o binding do consumidor não voltam** — ninguém os declara do lado de cá;
- e evento publicado num exchange topic **sem binding casando é descartado em silêncio**, com o
  publish confirmado e a outbox marcando `publicado_em` normalmente.

Ou seja: a outbox garante que o evento **sai**, não que alguém o **recebe**. Entre a recriação
do broker e a redeclaração da fila do consumidor existe uma janela em que tudo fica verde —
deploy, healthcheck, `relay_ciclos_total{outcome="success"}`, `outbox_pendentes=0` — e os
eventos evaporam. Nenhum alerta atual pega isso: eles vigiam backlog e falha de ciclo, e aqui
não há backlog nem falha.

**Regra:** topologia de consumidor é estado do broker, não do código, e não sobrevive ao
volume. Depois de recriar o broker, recrie fila e bindings **antes** de qualquer publicação. E
o alerta que falta na plataforma não é sobre a outbox — é sobre o exchange `prices` ter os
bindings esperados, ou sobre `custodia.prices` existir.

**Corolário sobre a prova:** o smoke test do relay no deploy continua provando apenas
agendamento e alcance do Postgres — com a outbox vazia o ciclo fecha com sucesso sem abrir
conexão. Isso eu tinha escrito no comentário do workflow ao portar o teste; escrever o limite
não é agir sobre ele. Métrica de ciclo não é métrica de efeito.

## Guarda que sumiu junto com um tag flutuante, e o run avisava

O deploy dos três repos usava `appleboy/ssh-action@v1` com `script_stop: true`. Esse input
**foi removido da action**, e o `@v1` — tag flutuante — já aponta para uma versão que não o
conhece. O run diz isso em toda execução, numa anotação amarela:

```
Unexpected input(s) 'script_stop', valid inputs are ['host', 'port', ..., 'script', 'envs', ...]
```

Ninguém leu. Anotação amarela em job verde é exatamente o que se aprende a não ver.

No `operacoes` e no `hub-precos` não houve consequência: os scripts já começam com `set -e`, e
era ele que vinha fazendo o trabalho. No `tesouro-direto` houve: o script **não tem `set -e`** e
dependia inteiramente do `script_stop`. Pior, o comentário ao lado documenta o incidente que
motivou a guarda — *"sem parada em erro, um comando que falha não para a execução; a última
linha (`docker image prune -f`) sempre sucede, então o healthcheck testa o container ANTIGO que
continua respondendo 200, fechando o job VERDE mesmo com deploy quebrado no meio"*. Ou seja: o
modo de falha estava reaberto havia tempo indeterminado, com o comentário jurando que estava
coberto.

**O comentário virou a única evidência da guarda, e ele não é executável.** Um teste teria
pegado; um comentário nunca pega.

**Regra:** a parada em erro tem que vir de dentro do script (`set -e`), não de um input de
action de terceiro — `set -e` não depende de versão de nada. E tag flutuante (`@v1`, `@v4`)
significa que a dependência muda sozinha: o que hoje é input válido amanhã é ignorado em
silêncio, e o aviso vem no canal que ninguém lê. Ou se pina por SHA, ou não se apoia guarda
nenhuma no comportamento dela.

**Corolário sobre onde procurar:** isto não apareceu em revisão de código nem em teste — apareceu
lendo a saída de um deploy que tinha passado. Vale a mesma lição da §10.11: coisa estranha no
log de um deploy verde não é ruído.

## Especificar só a metade permissiva de um invariante de equivalência

No F5 o invariante central era "mesma origem para lista e validação": todo instrumento que o
`GET /operacoes/instrumentos` oferece tem que ser aceito pelo `POST /operacoes`. Eu escrevi o
teste que prova isso, com catálogo cheio de armadilhas (id que difere só por caixa, id que é
prefixo de outro, vencido, `nomeExibicao` que casa mas o id não), guarda contra coleção vazia, e
prova por mutação. Ele ficou verde e as mutações que eu pedi ficaram vermelhas. Parecia fechado.

O revisor adversarial então trocou, na validação do `POST`, o match exato por
`if (catalogoResult.Value.Count == 0)` — isto é, degradou a regra para "o Hub devolveu **alguma
coisa**, então aceito" — e **os 484 testes continuaram verdes**.

O motivo é que meu teste prova uma direção só. "Para todo item que a lista devolveu, o POST
aceita" continua verdadeiro quando a validação afrouxa: uma validação que aceita **tudo** satisfaz
essa afirmação perfeitamente. A metade que falta é a estrita — "um id que a lista NÃO ofereceu, o
POST recusa" — e é exatamente ela que impede a regressão. Como a busca do Hub é textual, buscar
`td:x` traz `td:x-2030`; sem a metade estrita, um `instrumentoId` inexistente que seja prefixo de
algo existente entraria numa tabela append-only.

Contribuiu para o buraco um detalhe de fixture que eu não olhei: os dois `FakeHubCatalogoClient`
traduziam "instrumento não existe" como **lista vazia**, e "existe" como uma lista de um item cujo
`Id` era o próprio termo buscado. O cenário que importa — lista **não vazia** sem match exato — era
inexprimível nos fakes, então nenhum teste podia cobri-lo, por construção. O fake não estava
errado; ele só modelava um mundo em que o defeito não cabe.

**Regra:** invariante que afirma equivalência entre dois caminhos precisa de teste nas **duas
direções**, e a direção que pega regressão é quase sempre a estrita (o que um caminho recusa),
não a permissiva (o que o outro aceita). Ao despachar o teste, escreva as duas explicitamente — o
executor entrega o que está no prompt, e "prove o invariante" será lido como a direção que o
enunciado sugerir.

**Corolário sobre fakes:** antes de confiar numa suíte, pergunte quais estados o fake é **capaz**
de representar. Fake construído a partir de um `bool` só sabe dizer sim e não; se o defeito mora
no "sim parcial", nenhuma quantidade de testes sobre aquele fake vai encontrá-lo. Cobertura é
limitada pelo vocabulário do dublê, não pelo número de casos.

## Rodar o revisor contra entrega não commitada

O `revisor` **muta a implementação de propósito** — é o que ele faz de útil. Para reverter, o
caminho que qualquer um alcança primeiro é `git checkout -- <arquivo>`. E `git checkout` não
"reverte a mutação": ele volta o arquivo ao **último commit**. Se a entrega inteira está só no
working tree, isso apaga o trabalho todo, e o agente não tem como saber que apagou — o arquivo
existe, compila, e os testes até passam, porque voltaram a um estado consistente anterior.

Aconteceu no F5, no porte da correção para o `hub-precos`: o revisor fez `git checkout` no
`TdApiClient.cs` e perdeu as mudanças não commitadas. Ele tinha feito uma cópia de segurança antes
e restaurou, e conferiu por `diff` que o conteúdo voltou idêntico — então não custou nada. Mas foi
por disciplina dele, não por proteção do processo.

**Regra:** commite antes da revisão adversarial. Branch de trabalho existe para isso, e "commit
que vai ser reescrito no squash" não custa nada. Se por algum motivo não der para commitar, tire
um snapshot (`tar czf` do `src`/`tests` para fora do repo), diga ao revisor onde ele está, e
**proíba explicitamente `git checkout`/`git restore`/`git stash`** no prompt — mandando restaurar
por `cp` da cópia. As três formas apagam trabalho não commitado, e as três parecem seguras.

**Corolário, e é o que quase enganou duas revisões:** quando boa parte da entrega está em arquivos
**não rastreados** (`??` no `git status`), o `git diff` **não mostra nada disso**. Um revisor que
comece por `git diff` conclui que a entrega é menor do que é, ou que não existe — foi o mesmo
sintoma que o `isolation: "worktree"` produziu no `hub`, com outra causa. Todo prompt de revisão
sobre trabalho não commitado tem que mandar **começar por `git status --short`** e ler os arquivos
novos direto. E ao final, conferir a reversão contra o snapshot, não só contra o `git diff` — se o
arquivo voltou para o commit anterior, o `git diff` fica **limpo**, que é exatamente o sinal
errado: limpo aqui significa "perdi tudo", não "não sobrou mutação".

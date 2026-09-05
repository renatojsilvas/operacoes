#!/usr/bin/env bash
# Hook do /docker-entrypoint-initdb.d/, executado pelo entrypoint oficial da
# imagem postgres SÓ na primeira inicialização de um volume `pgdata` vazio.
#
# O SQL de verdade (infra/postgres/sql/operacoes-role.sql) vive FORA deste
# diretório de propósito: o entrypoint do Postgres executa TODO arquivo
# `.sql` que encontrar em `/docker-entrypoint-initdb.d/` diretamente via
# `psql`, sem passar nenhuma variável — e aquele SQL exige a variável de
# ambiente OPERACOES_APP_PASSWORD. Executado sozinho ali, falharia. Por isso
# `infra/postgres/sql/` é montado num caminho separado (`/opt/operacoes/sql`, ver
# docker-compose.yml) e este script — que roda com OPERACOES_APP_PASSWORD já no
# ambiente do container — é o único arquivo deste diretório.
set -euo pipefail

if [ -z "${OPERACOES_APP_PASSWORD:-}" ]; then
  echo "ERRO: OPERACOES_APP_PASSWORD não definida (ou vazia) — obrigatória para provisionar a role operacoes." >&2
  exit 1
fi

# Não passamos a senha via `-v` (argv do psql): OPERACOES_APP_PASSWORD já está no
# ambiente do processo (herdado do container), e o SQL a lê com `\getenv`.
# Isso evita a senha aparecer em `ps`/histórico de shell.
#
# Não passamos `--single-transaction`/`-1`: a atomicidade fim-a-fim (criação
# da role + ownership/REVOKE do database, tudo ou nada) já é garantida DENTRO
# de `operacoes-role.sql` (BEGIN/COMMIT explícitos), não por uma flag desta
# invocação — ver o comentário "DEFEITO 1" no topo daquele arquivo. Isso
# também vale para o caminho manual (`docker exec ... psql -f`, ver
# `infra/postgres/README.md`), sem depender de quem digita o comando lembrar
# de repetir a flag.
#
# Não passamos `OPERACOES_DB_NAME` aqui: neste caminho o database de destino é
# sempre `$POSTGRES_DB` (definido pelo docker-compose.yml como `operacoes`), que já
# bate com o default `'operacoes'` da guarda dentro de `operacoes-role.sql` — a variável
# só é necessária no caminho manual, contra um database com outro nome (ex.:
# um futuro `hub_e2e`).
psql -v ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" \
  --dbname "$POSTGRES_DB" \
  -f /opt/operacoes/sql/operacoes-role.sql

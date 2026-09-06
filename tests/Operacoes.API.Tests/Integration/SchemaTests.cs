using Dapper;
using Npgsql;
using Operacoes.Domain.Operacoes;

namespace Operacoes.API.Tests.Integration;

[Collection("api")]
public sealed class SchemaTests
{
    private readonly string _connectionString;

    public SchemaTests(ApiTestFactory factory)
    {
        _ = factory;

        _connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings__DefaultConnection não foi definida pela ApiTestFactory.");
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private sealed record ColumnInfo(
        string ColumnName,
        string DataType,
        string IsNullable,
        string? ColumnDefault,
        int? NumericPrecision,
        int? NumericScale);

    private async Task<IReadOnlyDictionary<string, ColumnInfo>> GetColumnsAsync(
        NpgsqlConnection connection, string tableName)
    {
        var rows = await connection.QueryAsync<ColumnInfo>(
            """
            SELECT column_name AS "ColumnName",
                   data_type AS "DataType",
                   is_nullable AS "IsNullable",
                   column_default AS "ColumnDefault",
                   numeric_precision AS "NumericPrecision",
                   numeric_scale AS "NumericScale"
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @tableName
            """,
            new { tableName });

        return rows.ToDictionary(r => r.ColumnName);
    }

    private async Task<bool> TableExistsAsync(NpgsqlConnection connection, string tableName)
    {
        return await connection.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'public' AND table_name = @tableName
            )
            """,
            new { tableName });
    }

    private sealed class ForeignKeyRow
    {
        public char ConfDelType { get; set; }
        public string[] ColunasOrigem { get; set; } = [];
        public string[] ColunasReferenciadas { get; set; } = [];
    }

    private sealed record ForeignKeyInfo(
        char ConfDelType,
        IReadOnlyList<string> ColunasOrigem,
        string TabelaReferenciada,
        IReadOnlyList<string> ColunasReferenciadas);

    private async Task<ForeignKeyInfo?> GetForeignKeyAsync(
        NpgsqlConnection connection, string tableName, string referencedTableName)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ForeignKeyRow>(
            """
            SELECT
                con.confdeltype AS "ConfDelType",
                (SELECT array_agg(a.attname ORDER BY k.ord)
                 FROM unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord)
                 JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum) AS "ColunasOrigem",
                (SELECT array_agg(a.attname ORDER BY k.ord)
                 FROM unnest(con.confkey) WITH ORDINALITY AS k(attnum, ord)
                 JOIN pg_attribute a ON a.attrelid = con.confrelid AND a.attnum = k.attnum) AS "ColunasReferenciadas"
            FROM pg_constraint con
            JOIN pg_class src ON src.oid = con.conrelid
            JOIN pg_class dst ON dst.oid = con.confrelid
            WHERE con.contype = 'f'
              AND src.relname = @tableName
              AND dst.relname = @referencedTableName
            """,
            new { tableName, referencedTableName });

        return row is null
            ? null
            : new ForeignKeyInfo(row.ConfDelType, row.ColunasOrigem, referencedTableName, row.ColunasReferenciadas);
    }

    private void AssertColumn(
        IReadOnlyDictionary<string, ColumnInfo> columns,
        string tableName,
        string columnName,
        string expectedDataType,
        bool expectedNullable,
        bool expectNoDefault = true)
    {
        Assert.True(
            columns.ContainsKey(columnName),
            $"{tableName}: coluna '{columnName}' não existe. Colunas encontradas: [{string.Join(", ", columns.Keys)}]");

        var column = columns[columnName];

        Assert.True(
            string.Equals(column.DataType, expectedDataType, StringComparison.Ordinal),
            $"{tableName}.{columnName}: tipo esperado '{expectedDataType}', encontrado '{column.DataType}'.");

        var expectedIsNullableFlag = expectedNullable ? "YES" : "NO";
        Assert.True(
            string.Equals(column.IsNullable, expectedIsNullableFlag, StringComparison.Ordinal),
            $"{tableName}.{columnName}: nullability esperada '{expectedIsNullableFlag}', encontrada '{column.IsNullable}'.");

        if (expectNoDefault)
        {
            Assert.True(
                column.ColumnDefault is null,
                $"{tableName}.{columnName}: esperava column_default IS NULL, mas encontrou default '{column.ColumnDefault}'.");
        }
    }

    [Fact]
    public async Task Operacoes_TabelaExiste()
    {
        using var connection = await OpenConnectionAsync();

        var exists = await TableExistsAsync(connection, "operacoes");

        Assert.True(exists, "Tabela 'operacoes' não existe no schema public após as migrations.");
    }

    [Fact]
    public async Task Operacoes_ColunasBatemComORoadmapF2()
    {
        using var connection = await OpenConnectionAsync();
        var columns = await GetColumnsAsync(connection, "operacoes");

        var expected = new[]
        {
            "id", "cliente_id", "instrumento_id", "operacao", "quantidade",
            "valor_financeiro", "data_evento", "registrado_em", "estorna_operacao_id",
        };

        Assert.True(
            expected.ToHashSet().SetEquals(columns.Keys),
            "operacoes: conjunto de colunas divergente. Esperado: " +
            $"[{string.Join(", ", expected)}], encontrado: [{string.Join(", ", columns.Keys)}].");

        AssertColumn(columns, "operacoes", "id", "text", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "operacoes", "cliente_id", "text", expectedNullable: false);
        AssertColumn(columns, "operacoes", "instrumento_id", "text", expectedNullable: false);
        AssertColumn(columns, "operacoes", "operacao", "text", expectedNullable: false);
        AssertColumn(columns, "operacoes", "quantidade", "numeric", expectedNullable: false);
        AssertColumn(columns, "operacoes", "valor_financeiro", "numeric", expectedNullable: false);
        AssertColumn(columns, "operacoes", "data_evento", "date", expectedNullable: false);
        AssertColumn(columns, "operacoes", "estorna_operacao_id", "text", expectedNullable: true);

        var quantidade = columns["quantidade"];
        Assert.True(
            quantidade.NumericPrecision == 18 && quantidade.NumericScale == 8,
            "operacoes.quantidade: esperava numeric(18,8), encontrado " +
            $"numeric({quantidade.NumericPrecision},{quantidade.NumericScale}).");

        var valorFinanceiro = columns["valor_financeiro"];
        Assert.True(
            valorFinanceiro.NumericPrecision == 18 && valorFinanceiro.NumericScale == 2,
            "operacoes.valor_financeiro: esperava numeric(18,2), encontrado " +
            $"numeric({valorFinanceiro.NumericPrecision},{valorFinanceiro.NumericScale}).");
    }

    [Fact]
    public async Task Operacoes_RegistradoEm_TemDefaultNow()
    {
        using var connection = await OpenConnectionAsync();
        var columns = await GetColumnsAsync(connection, "operacoes");

        // Prova de presença: diferente da outbox (decisão do F2), registrado_em tem DEFAULT now()
        // no servidor — asserção explícita, não inferida da ausência de outra coisa.
        AssertColumn(columns, "operacoes", "registrado_em", "timestamp with time zone", expectedNullable: false, expectNoDefault: false);

        var registradoEm = columns["registrado_em"];
        Assert.True(
            registradoEm.ColumnDefault is not null && registradoEm.ColumnDefault.Contains("now()", StringComparison.OrdinalIgnoreCase),
            $"operacoes.registrado_em: esperava column_default contendo 'now()', encontrado '{registradoEm.ColumnDefault}'.");
    }

    [Fact]
    public async Task Outbox_CriadoEm_NaoTemDefault()
    {
        using var connection = await OpenConnectionAsync();
        var columns = await GetColumnsAsync(connection, "outbox");

        // Prova de ausência: ao contrário de operacoes.registrado_em, outbox.criado_em NÃO tem
        // DEFAULT no servidor (replica o hub-precos fielmente, decisão do F2). INSERT manual sem
        // essa coluna falha por NOT NULL — é intencional.
        AssertColumn(columns, "outbox", "criado_em", "timestamp with time zone", expectedNullable: false, expectNoDefault: true);
    }

    [Fact]
    public async Task Operacoes_ChavePrimaria_EhId()
    {
        using var connection = await OpenConnectionAsync();

        var pk = await connection.QueryAsync<string>(
            """
            SELECT a.attname
            FROM pg_index i
            JOIN pg_class c ON c.oid = i.indrelid
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY(i.indkey)
            WHERE c.relname = 'operacoes'
              AND i.indisprimary
            """);

        Assert.Equal(new[] { "id" }, pk);
    }

    [Fact]
    public async Task Operacoes_IndiceCliente_EhClienteIdEDataEventoDescendente()
    {
        using var connection = await OpenConnectionAsync();

        var indexDef = await connection.ExecuteScalarAsync<string?>(
            """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'operacoes'
              AND indexname = 'ix_operacoes_cliente'
            """);

        Assert.True(indexDef is not null, "Índice 'ix_operacoes_cliente' não existe em operacoes.");

        Assert.True(
            indexDef!.Contains("cliente_id", StringComparison.OrdinalIgnoreCase),
            $"ix_operacoes_cliente: esperava indexar 'cliente_id', encontrado: '{indexDef}'.");

        Assert.True(
            indexDef.Contains("data_evento DESC", StringComparison.OrdinalIgnoreCase),
            $"ix_operacoes_cliente: esperava 'data_evento DESC' na definição do índice, encontrado: '{indexDef}'.");
    }

    [Fact]
    public async Task Operacoes_IndiceEstornoUnico_EhUnicoEParcial()
    {
        using var connection = await OpenConnectionAsync();

        var indexDef = await connection.ExecuteScalarAsync<string?>(
            """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'operacoes'
              AND indexname = 'ix_operacoes_estorna_unico'
            """);

        Assert.True(indexDef is not null, "Índice 'ix_operacoes_estorna_unico' não existe em operacoes.");

        Assert.True(
            indexDef!.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase),
            $"ix_operacoes_estorna_unico: esperava UNIQUE, encontrado: '{indexDef}'.");

        Assert.True(
            indexDef.Contains("WHERE", StringComparison.OrdinalIgnoreCase)
            && indexDef.Contains("estorna_operacao_id", StringComparison.OrdinalIgnoreCase)
            && indexDef.Contains("IS NOT NULL", StringComparison.OrdinalIgnoreCase),
            "ix_operacoes_estorna_unico: esperava índice PARCIAL com predicado " +
            $"'estorna_operacao_id IS NOT NULL', encontrado: '{indexDef}'.");
    }

    [Fact]
    public async Task Operacoes_IndiceEstornaClienteInstrumento_CobreAsTresColunasNaOrdemCerta()
    {
        using var connection = await OpenConnectionAsync();

        var indexDef = await connection.ExecuteScalarAsync<string?>(
            """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'operacoes'
              AND indexname = 'ix_operacoes_estorna_cliente_instrumento'
            """);

        Assert.True(
            indexDef is not null,
            "Índice 'ix_operacoes_estorna_cliente_instrumento' não existe em operacoes. " +
            "Este é o índice de cobertura da FK composta — o EF Core o cria de qualquer jeito, " +
            "e precisa ter nome explícito na convenção do repo.");

        // Ordem importa: é a ordem das colunas da FK composta (estorna_operacao_id, cliente_id,
        // instrumento_id) — fora de ordem, o índice deixa de cobrir a FK com eficiência.
        var colunas = await connection.QueryAsync<string>(
            """
            SELECT a.attname
            FROM pg_index i
            JOIN pg_class c ON c.oid = i.indrelid
            JOIN pg_class ic ON ic.oid = i.indexrelid
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY(i.indkey)
            WHERE c.relname = 'operacoes'
              AND ic.relname = 'ix_operacoes_estorna_cliente_instrumento'
            ORDER BY array_position(i.indkey, a.attnum)
            """);

        Assert.Equal(
            new[] { "estorna_operacao_id", "cliente_id", "instrumento_id" },
            colunas);
    }

    [Fact]
    public async Task NaoExisteIndiceComNomeForaDaConvencao()
    {
        using var connection = await OpenConnectionAsync();

        // Todo índice de operacoes/outbox que não é o mecanismo de suporte de uma constraint
        // (pg_constraint nomeia PK_/FK_/ck_/ux_ e cria o índice correspondente com o MESMO nome)
        // precisa começar com "ix_" e ser inteiramente minúsculo. É a guarda de verdade contra o
        // defeito que motivou esta correção: nomear um índice hoje não impede o EF de gerar outro
        // amanhã, sem nome, se a configuração explícita for esquecida.
        var indexNames = await connection.QueryAsync<string>(
            """
            SELECT i.relname
            FROM pg_index idx
            JOIN pg_class c ON c.oid = idx.indrelid
            JOIN pg_class i ON i.oid = idx.indexrelid
            WHERE c.relname IN ('operacoes', 'outbox')
              AND NOT EXISTS (
                  SELECT 1 FROM pg_constraint con
                  WHERE con.conindid = idx.indexrelid
              )
            """);

        var names = indexNames.ToList();

        // Controle positivo (PADROES.md §10.8): sem isto, uma query que voltasse vazia — erro de
        // sintaxe, rename de tabela — faria o foreach abaixo não executar nenhuma asserção e o
        // teste passaria mentindo. ix_operacoes_cliente é índice comum (não-constraint) e sabidamente
        // existe (ver Operacoes_IndiceCliente_EhClienteIdEDataEventoDescendente acima).
        Assert.Contains("ix_operacoes_cliente", names);

        foreach (var name in names)
        {
            Assert.True(
                name.StartsWith("ix_", StringComparison.Ordinal) && name == name.ToLowerInvariant(),
                $"Índice '{name}' foge da convenção (ix_tabela_colunas, minúsculo, §3 do PADROES.md) " +
                "— provável índice auto-gerado pelo EF Core sem HasDatabaseName explícito.");
        }
    }

    [Fact]
    public async Task Operacoes_FkEstornaOperacaoId_ApontaParaOperacoesComRestrict()
    {
        using var connection = await OpenConnectionAsync();

        var fk = await GetForeignKeyAsync(connection, "operacoes", "operacoes");

        Assert.True(fk is not null, "operacoes: não encontrei FK auto-referente de estorna_operacao_id.");

        // Correção do defeito A (revisor): a FK é composta — (estorna_operacao_id, cliente_id,
        // instrumento_id) -> operacoes(id, cliente_id, instrumento_id) — não mais só
        // estorna_operacao_id -> id. Isso garante que um estorno só referencia uma operação do MESMO
        // cliente e do MESMO instrumento.
        Assert.Equal(
            new[] { "estorna_operacao_id", "cliente_id", "instrumento_id" },
            fk!.ColunasOrigem);
        Assert.Equal(new[] { "id", "cliente_id", "instrumento_id" }, fk.ColunasReferenciadas);

        Assert.True(
            fk.ConfDelType == 'r',
            $"operacoes.(estorna_operacao_id, cliente_id, instrumento_id) -> operacoes(id, cliente_id, instrumento_id): " +
            $"esperava ON DELETE RESTRICT (confdeltype = 'r'), encontrado '{fk.ConfDelType}'.");
    }

    [Fact]
    public async Task Outbox_TabelaExiste()
    {
        using var connection = await OpenConnectionAsync();

        var exists = await TableExistsAsync(connection, "outbox");

        Assert.True(exists, "Tabela 'outbox' não existe no schema public após as migrations.");
    }

    [Fact]
    public async Task Outbox_ColunasBatemComORoadmapF2()
    {
        using var connection = await OpenConnectionAsync();
        var columns = await GetColumnsAsync(connection, "outbox");

        var expected = new[] { "id", "tipo", "routing_key", "payload", "criado_em", "publicado_em" };

        Assert.True(
            expected.ToHashSet().SetEquals(columns.Keys),
            "outbox: conjunto de colunas divergente. Esperado: " +
            $"[{string.Join(", ", expected)}], encontrado: [{string.Join(", ", columns.Keys)}].");

        AssertColumn(columns, "outbox", "id", "bigint", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "outbox", "tipo", "text", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "outbox", "routing_key", "text", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "outbox", "payload", "jsonb", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "outbox", "publicado_em", "timestamp with time zone", expectedNullable: true);
    }

    [Fact]
    public async Task Outbox_IndicePendentes_EhParcialSobrePublicadoEmNulo()
    {
        using var connection = await OpenConnectionAsync();

        var indexDef = await connection.ExecuteScalarAsync<string?>(
            """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'outbox'
              AND indexname = 'ix_outbox_pendentes'
            """);

        Assert.True(indexDef is not null, "Índice 'ix_outbox_pendentes' não existe em outbox.");

        Assert.True(
            indexDef!.Contains("WHERE", StringComparison.OrdinalIgnoreCase)
            && indexDef.Contains("publicado_em", StringComparison.OrdinalIgnoreCase)
            && indexDef.Contains("IS NULL", StringComparison.OrdinalIgnoreCase),
            "ix_outbox_pendentes: esperava índice PARCIAL com predicado " +
            $"'publicado_em IS NULL', encontrado: '{indexDef}'.");
    }

    // --- Prova de ausência (ADR-12 / §7.2): sem tabela de clientes, sem tabela de instrumentos, sem
    // de-para local, e sem índice único sobre campos de negócio (dois aportes idênticos no mesmo dia
    // são legítimos — PADROES.md, decisão do F2). ---

    [Theory]
    [InlineData("clientes")]
    [InlineData("instrumentos")]
    [InlineData("instrumento_fontes")]
    [InlineData("cliente_instrumento")]
    [InlineData("de_para_instrumentos")]
    public async Task NaoExisteTabelaDeClientesOuInstrumentosOuDePara(string tableName)
    {
        using var connection = await OpenConnectionAsync();

        var exists = await TableExistsAsync(connection, tableName);

        Assert.False(
            exists,
            $"Tabela '{tableName}' não deveria existir (ADR-12/§7.2: sem FK, sem tabela local, sem de-para).");
    }

    [Fact]
    public async Task NaoExisteIndiceUnicoSobreCamposDeNegocioDeOperacoes()
    {
        using var connection = await OpenConnectionAsync();

        var uniqueColumnSets = await connection.QueryAsync<string>(
            """
            SELECT string_agg(a.attname, ',' ORDER BY array_position(i.indkey, a.attnum))
            FROM pg_index i
            JOIN pg_class c ON c.oid = i.indrelid
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY(i.indkey)
            WHERE c.relname = 'operacoes'
              AND i.indisunique
              AND NOT i.indisprimary
            GROUP BY i.indexrelid
            """);

        var sets = uniqueColumnSets.ToList();

        // Controle positivo (PADROES.md §10.8): sem isto, uma consulta que voltasse vazia — por
        // erro na query, rename de tabela, etc. — faria o foreach abaixo não executar nenhuma
        // asserção e o teste passaria mentindo. ix_operacoes_estorna_unico é único, não-primário e
        // sabidamente existe (ver Operacoes_IndiceEstornoUnico_EhUnicoEParcial acima).
        Assert.Contains(
            sets,
            set => set == "estorna_operacao_id");

        var camposDeNegocio = new[] { "cliente_id", "instrumento_id", "operacao", "quantidade", "valor_financeiro", "data_evento" };

        foreach (var set in sets)
        {
            var colunas = set.Split(',');

            Assert.True(
                colunas.Any(c => !camposDeNegocio.Contains(c)),
                "operacoes: encontrei um índice único que só cobre campos de negócio " +
                $"('{set}') — proibido pela decisão do F2 (dois aportes idênticos no mesmo dia são legítimos).");
        }
    }

    // --- Prova de imutabilidade (trigger criada pela migration): UPDATE e DELETE em operacoes
    // falham; o INSERT em si é o controle positivo (PADROES.md §10.8 — asserção negativa precisa
    // de controle positivo). ---

    [Fact]
    public async Task Operacoes_Insert_Funciona_ControlePositivo()
    {
        using var connection = await OpenConnectionAsync();
        var id = $"op-schema-tests-{Guid.NewGuid():N}";

        await connection.ExecuteAsync(
            """
            INSERT INTO operacoes (id, cliente_id, instrumento_id, operacao, quantidade, valor_financeiro, data_evento)
            VALUES (@id, 'cliente-1', 'td:tesouro-selic-2029', 'aporte', 10.5, 1000.00, '2026-01-01')
            """,
            new { id });

        var count = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE id = @id", new { id });

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Operacoes_Update_EhBloqueadoPelaTrigger()
    {
        using var connection = await OpenConnectionAsync();
        var id = $"op-schema-tests-{Guid.NewGuid():N}";

        await connection.ExecuteAsync(
            """
            INSERT INTO operacoes (id, cliente_id, instrumento_id, operacao, quantidade, valor_financeiro, data_evento)
            VALUES (@id, 'cliente-1', 'td:tesouro-selic-2029', 'aporte', 10.5, 1000.00, '2026-01-01')
            """,
            new { id });

        var exception = await Record.ExceptionAsync(() => connection.ExecuteAsync(
            "UPDATE operacoes SET quantidade = 99 WHERE id = @id", new { id }));

        Assert.NotNull(exception);
        var pgException = Assert.IsType<PostgresException>(exception);
        Assert.Contains("append-only", pgException.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Operacoes_Delete_EhBloqueadoPelaTrigger()
    {
        using var connection = await OpenConnectionAsync();
        var id = $"op-schema-tests-{Guid.NewGuid():N}";

        await connection.ExecuteAsync(
            """
            INSERT INTO operacoes (id, cliente_id, instrumento_id, operacao, quantidade, valor_financeiro, data_evento)
            VALUES (@id, 'cliente-1', 'td:tesouro-selic-2029', 'aporte', 10.5, 1000.00, '2026-01-01')
            """,
            new { id });

        var exception = await Record.ExceptionAsync(() => connection.ExecuteAsync(
            "DELETE FROM operacoes WHERE id = @id", new { id }));

        Assert.NotNull(exception);
        var pgException = Assert.IsType<PostgresException>(exception);
        Assert.Contains("append-only", pgException.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    // --- Prova das guardas de estorno (defeito A do revisor — dado irreparável): CHECKs
    // ck_operacoes_estorno_nao_auto / ck_operacoes_estorno_coerente, e a FK composta que amarra
    // cliente_id e instrumento_id. Cada asserção negativa vem acompanhada do seu controle positivo
    // (PADROES.md §10.8). ---

    private sealed record ConstraintRow(string ConName, char ConType);

    private async Task InsertOperacaoAsync(
        NpgsqlConnection connection,
        string id,
        string clienteId,
        string instrumentoId,
        string operacao,
        string? estornaOperacaoId = null)
    {
        await connection.ExecuteAsync(
            """
            INSERT INTO operacoes (id, cliente_id, instrumento_id, operacao, quantidade, valor_financeiro, data_evento, estorna_operacao_id)
            VALUES (@id, @clienteId, @instrumentoId, @operacao, 10.5, 1000.00, '2026-01-01', @estornaOperacaoId)
            """,
            new { id, clienteId, instrumentoId, operacao, estornaOperacaoId });
    }

    [Fact]
    public async Task Operacoes_ChecksEFkDeEstorno_ExistemComOsNomesEsperados()
    {
        using var connection = await OpenConnectionAsync();

        var rows = await connection.QueryAsync<ConstraintRow>(
            """
            SELECT conname AS "ConName", contype AS "ConType"
            FROM pg_constraint con
            JOIN pg_class c ON c.oid = con.conrelid
            WHERE c.relname = 'operacoes'
            """);

        var constraints = rows.ToDictionary(r => r.ConName, r => r.ConType);

        Assert.True(
            constraints.TryGetValue("ck_operacoes_estorno_nao_auto", out var tipoNaoAuto) && tipoNaoAuto == 'c',
            "ck_operacoes_estorno_nao_auto: CHECK constraint não encontrado em operacoes.");

        Assert.True(
            constraints.TryGetValue("ck_operacoes_estorno_coerente", out var tipoCoerente) && tipoCoerente == 'c',
            "ck_operacoes_estorno_coerente: CHECK constraint não encontrado em operacoes.");

        Assert.True(
            constraints.TryGetValue("FK_operacoes_operacoes_estorna_operacao_id", out var tipoFk) && tipoFk == 'f',
            "FK_operacoes_operacoes_estorna_operacao_id: FOREIGN KEY constraint não encontrado em operacoes.");
    }

    // --- Item 3 da segunda revisão adversarial: `operacao` não tinha restrição de domínio no banco
    // — INSERT com operacao = 'valor-que-nao-existe' e estorna_operacao_id NULL era aceito e gravava
    // lixo permanente numa tabela append-only (PADROES.md §10.21, §10.8 para o controle positivo). ---

    [Fact]
    public async Task Operacoes_CheckOperacaoValida_Existe()
    {
        using var connection = await OpenConnectionAsync();

        var rows = await connection.QueryAsync<ConstraintRow>(
            """
            SELECT conname AS "ConName", contype AS "ConType"
            FROM pg_constraint con
            JOIN pg_class c ON c.oid = con.conrelid
            WHERE c.relname = 'operacoes'
            """);

        var constraints = rows.ToDictionary(r => r.ConName, r => r.ConType);

        Assert.True(
            constraints.TryGetValue("ck_operacoes_operacao_valida", out var tipo) && tipo == 'c',
            "ck_operacoes_operacao_valida: CHECK constraint não encontrado em operacoes.");
    }

    // Lê o próprio texto do CHECK no catálogo (pg_get_constraintdef) e compara com TipoOperacao.All —
    // prova que os dois lugares batem de fato, em vez de presumir que quem editou um lembrou do
    // outro. O comentário na migration e na configuration avisam; este teste confere.
    [Fact]
    public async Task Operacoes_CheckOperacaoValida_BateExatamenteComTipoOperacaoAll()
    {
        using var connection = await OpenConnectionAsync();

        var definicao = await connection.ExecuteScalarAsync<string?>(
            """
            SELECT pg_get_constraintdef(con.oid)
            FROM pg_constraint con
            JOIN pg_class c ON c.oid = con.conrelid
            WHERE c.relname = 'operacoes' AND con.conname = 'ck_operacoes_operacao_valida'
            """);

        Assert.True(definicao is not null, "ck_operacoes_operacao_valida: definição não encontrada.");

        // definicao vem como: CHECK ((operacao = ANY (ARRAY['aplicacao'::text, 'resgate'::text, ...])))
        var valoresNoCheck = System.Text.RegularExpressions.Regex
            .Matches(definicao!, @"'([^']+)'::text")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        Assert.NotEmpty(valoresNoCheck);

        var valoresEsperados = TipoOperacao.All.Select(t => t.Name).ToHashSet();

        Assert.True(
            valoresEsperados.SetEquals(valoresNoCheck),
            "ck_operacoes_operacao_valida diverge de TipoOperacao.All — os dois têm que andar " +
            $"juntos. No CHECK: [{string.Join(", ", valoresNoCheck)}], em TipoOperacao.All: " +
            $"[{string.Join(", ", valoresEsperados)}].");
    }

    [Fact]
    public async Task Operacoes_Insert_OperacaoComValorInvalido_EhRejeitado()
    {
        using var connection = await OpenConnectionAsync();
        var id = $"op-schema-tests-{Guid.NewGuid():N}";

        var exception = await Record.ExceptionAsync(() => InsertOperacaoAsync(
            connection, id, "cliente-1", "td:tesouro-selic-2029", "valor-que-nao-existe"));

        Assert.NotNull(exception);
        var pgException = Assert.IsType<PostgresException>(exception);
        Assert.Equal("ck_operacoes_operacao_valida", pgException.ConstraintName);
    }

    // Controle positivo (PADROES.md §10.8): os quatro valores de TipoOperacao.All são aceitos pelo
    // CHECK — sem isto, a asserção negativa acima poderia estar rejeitando TUDO por engano.
    [Theory]
    [InlineData("aplicacao")]
    [InlineData("resgate")]
    [InlineData("aporte")]
    public async Task Operacoes_Insert_TipoValidoSemEstorno_EhAceito(string operacao)
    {
        using var connection = await OpenConnectionAsync();
        var id = $"op-schema-tests-{Guid.NewGuid():N}";

        await InsertOperacaoAsync(connection, id, "cliente-1", "td:tesouro-selic-2029", operacao);

        var count = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE id = @id", new { id });
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Operacoes_Insert_TipoEstornoValido_EhAceito()
    {
        using var connection = await OpenConnectionAsync();
        var original = $"op-schema-tests-{Guid.NewGuid():N}";
        await InsertOperacaoAsync(connection, original, "cliente-1", "td:tesouro-selic-2029", "aporte");

        var estorno = $"op-schema-tests-{Guid.NewGuid():N}";
        await InsertOperacaoAsync(
            connection, estorno, "cliente-1", "td:tesouro-selic-2029", "estorno", estornaOperacaoId: original);

        var count = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE id = @estorno", new { estorno });
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Operacoes_Insert_EstornaOperacaoIdIgualAoProprioId_EhRejeitado()
    {
        using var connection = await OpenConnectionAsync();
        var id = $"op-schema-tests-{Guid.NewGuid():N}";

        var exception = await Record.ExceptionAsync(() => InsertOperacaoAsync(
            connection, id, "cliente-1", "td:tesouro-selic-2029", "estorno", estornaOperacaoId: id));

        Assert.NotNull(exception);
        var pgException = Assert.IsType<PostgresException>(exception);
        Assert.Equal("ck_operacoes_estorno_nao_auto", pgException.ConstraintName);
    }

    [Fact]
    public async Task Operacoes_Insert_AporteComEstornaOperacaoId_EhRejeitado()
    {
        using var connection = await OpenConnectionAsync();
        var original = $"op-schema-tests-{Guid.NewGuid():N}";
        await InsertOperacaoAsync(connection, original, "cliente-1", "td:tesouro-selic-2029", "aporte");

        var id = $"op-schema-tests-{Guid.NewGuid():N}";
        var exception = await Record.ExceptionAsync(() => InsertOperacaoAsync(
            connection, id, "cliente-1", "td:tesouro-selic-2029", "aporte", estornaOperacaoId: original));

        Assert.NotNull(exception);
        var pgException = Assert.IsType<PostgresException>(exception);
        Assert.Equal("ck_operacoes_estorno_coerente", pgException.ConstraintName);
    }

    [Fact]
    public async Task Operacoes_Insert_EstornoSemEstornaOperacaoId_EhRejeitado()
    {
        using var connection = await OpenConnectionAsync();
        var id = $"op-schema-tests-{Guid.NewGuid():N}";

        var exception = await Record.ExceptionAsync(() => InsertOperacaoAsync(
            connection, id, "cliente-1", "td:tesouro-selic-2029", "estorno"));

        Assert.NotNull(exception);
        var pgException = Assert.IsType<PostgresException>(exception);
        Assert.Equal("ck_operacoes_estorno_coerente", pgException.ConstraintName);
    }

    [Fact]
    public async Task Operacoes_Insert_EstornoApontandoParaOutroCliente_EhRejeitado()
    {
        using var connection = await OpenConnectionAsync();
        var original = $"op-schema-tests-{Guid.NewGuid():N}";
        await InsertOperacaoAsync(connection, original, "cliente-1", "td:tesouro-selic-2029", "aporte");

        var id = $"op-schema-tests-{Guid.NewGuid():N}";
        var exception = await Record.ExceptionAsync(() => InsertOperacaoAsync(
            connection, id, "cliente-2", "td:tesouro-selic-2029", "estorno", estornaOperacaoId: original));

        Assert.NotNull(exception);
        var pgException = Assert.IsType<PostgresException>(exception);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, pgException.SqlState);
        Assert.Equal("FK_operacoes_operacoes_estorna_operacao_id", pgException.ConstraintName);
    }

    [Fact]
    public async Task Operacoes_Insert_EstornoApontandoParaOutroInstrumento_EhRejeitado()
    {
        using var connection = await OpenConnectionAsync();
        var original = $"op-schema-tests-{Guid.NewGuid():N}";
        await InsertOperacaoAsync(connection, original, "cliente-1", "td:tesouro-selic-2029", "aporte");

        var id = $"op-schema-tests-{Guid.NewGuid():N}";
        var exception = await Record.ExceptionAsync(() => InsertOperacaoAsync(
            connection, id, "cliente-1", "td:tesouro-ipca-2035", "estorno", estornaOperacaoId: original));

        Assert.NotNull(exception);
        var pgException = Assert.IsType<PostgresException>(exception);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, pgException.SqlState);
        Assert.Equal("FK_operacoes_operacoes_estorna_operacao_id", pgException.ConstraintName);
    }

    // Controle positivo (PADROES.md §10.8): sem isto, as asserções negativas acima poderiam estar
    // rejeitando TUDO por engano (ex.: um erro de sintaxe no INSERT) em vez de rejeitar só o inválido.
    [Fact]
    public async Task Operacoes_Insert_EstornoValido_EhAceito()
    {
        using var connection = await OpenConnectionAsync();
        var original = $"op-schema-tests-{Guid.NewGuid():N}";
        await InsertOperacaoAsync(connection, original, "cliente-1", "td:tesouro-selic-2029", "aporte");

        var estorno = $"op-schema-tests-{Guid.NewGuid():N}";
        await InsertOperacaoAsync(
            connection, estorno, "cliente-1", "td:tesouro-selic-2029", "estorno", estornaOperacaoId: original);

        var count = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE id = @estorno", new { estorno });
        Assert.Equal(1, count);
    }

    // Prova de A4: a cadeia de estornos (op-C estorna op-B, que estornou op-A) continua permitida —
    // é a única saída para corrigir um estorno que entrou errado, já que UPDATE/DELETE são bloqueados.
    [Fact]
    public async Task Operacoes_Insert_EstornoDeEstorno_EhAceito()
    {
        using var connection = await OpenConnectionAsync();
        var opA = $"op-schema-tests-{Guid.NewGuid():N}";
        await InsertOperacaoAsync(connection, opA, "cliente-1", "td:tesouro-selic-2029", "aporte");

        var opB = $"op-schema-tests-{Guid.NewGuid():N}";
        await InsertOperacaoAsync(connection, opB, "cliente-1", "td:tesouro-selic-2029", "estorno", estornaOperacaoId: opA);

        var opC = $"op-schema-tests-{Guid.NewGuid():N}";
        await InsertOperacaoAsync(connection, opC, "cliente-1", "td:tesouro-selic-2029", "estorno", estornaOperacaoId: opB);

        var count = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM operacoes WHERE id = @opC", new { opC });
        Assert.Equal(1, count);
    }

    // --- Item 1 da segunda revisão adversarial: Domínio e banco discordavam para estornaOperacaoId
    // vazio ou só espaços — `Operacao.Create(..., estornaOperacaoId: "   ")` devolvia IsSuccess = true
    // e o INSERT correspondente violava ck_operacoes_estorno_coerente lá no banco (500 em vez de 4xx
    // no F3). PADROES.md §10.19: dado com dois canais de saída precisa de um teste por canal — aqui o
    // ponto é mais forte, os dois canais precisam CONCORDAR. Para cada combinação, esta prova roda os
    // dois caminhos que uma referência de estorno pode seguir: se o Domínio aceita, o valor que ele
    // guardaria (já trimado) é o que se tenta inserir — é o que a persistência real faria; se o
    // Domínio rejeita, o valor CRU é que se tenta inserir direto, provando que o banco também
    // bloquearia se algo algum dia contornasse o Domínio. Nenhuma das duas camadas é a única linha de
    // defesa. ---

    public static IEnumerable<object?[]> CombinacoesDeEstornaOperacaoId()
    {
        // (estornaOperacaoIdBruto, ehEstorno) — "REF" é substituído por um id real de uma operação
        // já existente (mesmo cliente/instrumento) na hora do teste.
        yield return new object?[] { null, false }; // sem referência, não-estorno: ambos aceitam
        yield return new object?[] { null, true }; // sem referência, estorno: ambos rejeitam
        yield return new object?[] { "", false };
        yield return new object?[] { "", true };
        yield return new object?[] { "   ", false };
        yield return new object?[] { "   ", true };
        yield return new object?[] { "REF", false }; // referência preenchida fora de estorno: ambos rejeitam
        yield return new object?[] { "REF", true }; // referência válida, estorno: ambos aceitam
        yield return new object?[] { " REF ", false };
        yield return new object?[] { " REF ", true }; // com espaços ao redor: Domínio trima e persiste trimado
    }

    [Theory]
    [MemberData(nameof(CombinacoesDeEstornaOperacaoId))]
    public async Task DominioEBanco_ConcordamSobreEstornaOperacaoId(string? estornaOperacaoIdBruto, bool ehEstorno)
    {
        using var connection = await OpenConnectionAsync();

        var clienteId = $"cliente-concordancia-{Guid.NewGuid():N}";
        var instrumentoId = "td:tesouro-selic-2029";
        var id = $"op-schema-tests-{Guid.NewGuid():N}";
        var tipo = ehEstorno ? TipoOperacao.Estorno : TipoOperacao.Aporte;

        string? estornaOperacaoIdEfetivo = estornaOperacaoIdBruto;
        if (estornaOperacaoIdBruto is not null && estornaOperacaoIdBruto.Contains("REF"))
        {
            var referenciaOriginalId = $"op-schema-tests-{Guid.NewGuid():N}";
            await InsertOperacaoAsync(connection, referenciaOriginalId, clienteId, instrumentoId, "aporte");
            estornaOperacaoIdEfetivo = estornaOperacaoIdBruto.Replace("REF", referenciaOriginalId);
        }

        var domainResult = Operacao.Create(
            id: id,
            clienteId: clienteId,
            instrumentoId: instrumentoId,
            tipo: tipo,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: new DateOnly(2026, 1, 1),
            registradoEm: DateTimeOffset.UtcNow,
            estornaOperacaoId: estornaOperacaoIdEfetivo);

        if (domainResult.IsSuccess)
        {
            // O Domínio aceitou: insere exatamente o valor que ele guardaria (já normalizado) — é
            // o que a persistência real gravaria via repositório. O banco tem que concordar.
            var exception = await Record.ExceptionAsync(() => InsertOperacaoAsync(
                connection, id, clienteId, instrumentoId, tipo.Name, domainResult.Value.EstornaOperacaoId));

            Assert.True(
                exception is null,
                "Domínio aceitou, mas o banco rejeitou a mesma operação já normalizada: " +
                $"estornaOperacaoIdBruto='{estornaOperacaoIdBruto}', tipo='{tipo.Name}', " +
                $"exceção: {exception}.");
        }
        else
        {
            // O Domínio rejeitou: tenta inserir o valor CRU direto no banco, contornando o Domínio.
            // Se o banco aceitasse, o Domínio seria a única linha de defesa contra esse dado — e a
            // tabela é append-only, então dado errado ali é irreversível (PADROES.md §10.21).
            var exception = await Record.ExceptionAsync(() => InsertOperacaoAsync(
                connection, id, clienteId, instrumentoId, tipo.Name, estornaOperacaoIdEfetivo));

            Assert.True(
                exception is not null,
                "Domínio rejeitou, mas o banco aceitou o mesmo valor cru — dado irreparável numa " +
                $"tabela append-only: estornaOperacaoIdBruto='{estornaOperacaoIdBruto}', " +
                $"tipo='{tipo.Name}', erro do Domínio: {domainResult.Error.Code}.");
            Assert.IsType<PostgresException>(exception);
        }
    }

    // --- Terceira auditoria de conformidade: normalização aplicada a um campo só (EstornaOperacaoId
    // trimava, id/clienteId/instrumentoId não) — mesmo defeito do LEIA-ME-KIT ("Normalizar de um lado
    // só"), agora corrigido em Operacao.Create. Esta prova é irmã de
    // DominioEBanco_ConcordamSobreEstornaOperacaoId: cobre os outros três identificadores gravados na
    // mesma tabela append-only, confirmando que Domínio e banco concordam sobre o valor TRIMADO que de
    // fato é persistido — sem isto, " op-1 " e "op-1" virariam duas linhas distintas e permanentes. ---

    private sealed record LinhaOperacao(string Id, string ClienteId, string InstrumentoId);

    [Theory]
    [InlineData("id")]
    [InlineData("clienteId")]
    [InlineData("instrumentoId")]
    public async Task DominioEBanco_ConcordamSobreIdClienteIdInstrumentoIdComEspacos(string campo)
    {
        using var connection = await OpenConnectionAsync();

        var idBase = $"op-schema-tests-{Guid.NewGuid():N}";
        var clienteIdBase = $"cliente-concordancia-{Guid.NewGuid():N}";
        var instrumentoIdBase = "td:tesouro-selic-2029";

        var idComEspacos = campo == "id" ? $" {idBase} " : idBase;
        var clienteIdComEspacos = campo == "clienteId" ? $" {clienteIdBase} " : clienteIdBase;
        var instrumentoIdComEspacos = campo == "instrumentoId" ? $" {instrumentoIdBase} " : instrumentoIdBase;

        var domainResult = Operacao.Create(
            id: idComEspacos,
            clienteId: clienteIdComEspacos,
            instrumentoId: instrumentoIdComEspacos,
            tipo: TipoOperacao.Aporte,
            quantidade: 10m,
            valorFinanceiro: 1000m,
            dataEvento: new DateOnly(2026, 1, 1),
            registradoEm: DateTimeOffset.UtcNow);

        Assert.True(domainResult.IsSuccess, $"Domínio deveria aceitar '{campo}' com espaços ao redor.");
        var operacao = domainResult.Value;

        // Grava exatamente o que o Domínio guardaria (já trimado) — é o que a persistência real faz
        // via repositório. O banco tem que aceitar sem reclamar.
        var exception = await Record.ExceptionAsync(() => InsertOperacaoAsync(
            connection, operacao.Id, operacao.ClienteId, operacao.InstrumentoId, "aporte"));

        Assert.True(
            exception is null,
            $"Domínio aceitou '{campo}' trimado, mas o banco rejeitou o INSERT: {exception}.");

        var linha = await connection.QuerySingleAsync<LinhaOperacao>(
            """
            SELECT id AS "Id", cliente_id AS "ClienteId", instrumento_id AS "InstrumentoId"
            FROM operacoes WHERE id = @id
            """,
            new { id = operacao.Id });

        // O valor gravado é o BASE (sem espaços) nos três campos — não só no que variou no Theory,
        // porque Operacao.Create trima os três independentemente de qual deles recebeu o espaço.
        Assert.Equal(idBase, linha.Id);
        Assert.Equal(clienteIdBase, linha.ClienteId);
        Assert.Equal(instrumentoIdBase, linha.InstrumentoId);
    }
}

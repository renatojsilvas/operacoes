using Operacoes.Domain.Common;

namespace Operacoes.Domain.Operacoes;

// O fato cru: o que o cliente declarou ter feito (docs/ROADMAP.md, F2). Nasce por INSERT e nunca
// mais muda — correção é por estorno (nova linha), não por UPDATE. A guarda de imutabilidade real
// está no banco (trigger criada pela migration); esta classe não impõe isso, só não expõe setters.
public sealed class Operacao : Entity<string>
{
    private Operacao(
        string id,
        string clienteId,
        string instrumentoId,
        TipoOperacao tipo,
        decimal quantidade,
        decimal valorFinanceiro,
        DateOnly dataEvento,
        DateTimeOffset registradoEm,
        string? estornaOperacaoId)
        : base(id)
    {
        ClienteId = clienteId;
        InstrumentoId = instrumentoId;
        Tipo = tipo;
        Quantidade = quantidade;
        ValorFinanceiro = valorFinanceiro;
        DataEvento = dataEvento;
        RegistradoEm = registradoEm;
        EstornaOperacaoId = estornaOperacaoId;
    }

    // Identidade de outro contexto (a Custódia, ver §7/ADR-12): grava-se cru, sem FK, sem tabela
    // local. Ver o comentário completo em OperacaoConfiguration.
    public string ClienteId { get; }

    // Id do Hub. Sem FK e sem tabela de-para — mesmo racional do ClienteId. Validação é no F3,
    // contra o REST do Hub.
    public string InstrumentoId { get; }

    public TipoOperacao Tipo { get; }
    public decimal Quantidade { get; }
    public decimal ValorFinanceiro { get; }
    public DateOnly DataEvento { get; }
    public DateTimeOffset RegistradoEm { get; }

    // Preenchido só quando Tipo == Estorno: aponta para a operação que esta linha reverte.
    public string? EstornaOperacaoId { get; }

    public static Result<Operacao> Create(
        string id,
        string clienteId,
        string instrumentoId,
        TipoOperacao tipo,
        decimal quantidade,
        decimal valorFinanceiro,
        DateOnly dataEvento,
        DateTimeOffset registradoEm,
        string? estornaOperacaoId = null)
    {
        ArgumentNullException.ThrowIfNull(tipo);

        if (string.IsNullOrWhiteSpace(id))
        {
            return OperacaoErrors.IdVazio;
        }

        if (string.IsNullOrWhiteSpace(clienteId))
        {
            return OperacaoErrors.ClienteIdVazio;
        }

        if (string.IsNullOrWhiteSpace(instrumentoId))
        {
            return OperacaoErrors.InstrumentoIdVazio;
        }

        // null significa "sem referência"; string vazia ou só espaços é entrada malformada, não
        // sinônimo de null — quem passou uma string quis passar uma referência, e normalizar
        // silenciosamente para null esconderia bug do chamador. Sem esta guarda, `Create` retornava
        // sucesso com EstornaOperacaoId == "   " e o INSERT correspondente violava
        // ck_operacoes_estorno_coerente lá no banco (500 em vez de 4xx no F3) — provado em revisão
        // adversarial do F2.
        if (estornaOperacaoId is not null && string.IsNullOrWhiteSpace(estornaOperacaoId))
        {
            return OperacaoErrors.EstornoReferenciaVazia;
        }

        // Validar primeiro, normalizar depois — mesma ordem para os quatro identificadores da
        // tabela. Trimados a partir daqui: sem isso, "op-1" e " op-1 " virariam duas linhas distintas
        // numa tabela append-only, e a trigger de imutabilidade impede o UPDATE que corrigiria (§10
        // do PADROES.md, "Normalizar de um lado só" no LEIA-ME-KIT).
        //
        // Deliberadamente SÓ Trim(), sem ToLowerInvariant() — diferente do molde
        // (Hub.Domain.Instrumentos.InstrumentoId), que baixa caixa porque canonizar o slug é
        // responsabilidade DELE. Aqui id é o tradeId vindo de fora, e clienteId/instrumentoId são
        // identidades de OUTRO contexto (Custódia e Hub — ADR-12, §3 do PADROES): a política de
        // canonização pertence ao dono do conceito, e reimplementá-la aqui é presumir uma regra
        // alheia que pode mudar sem aviso. Trim é outra coisa: remove ruído de transporte, que nenhum
        // dono de identidade trata como parte do valor. Transformar o valor (baixar caixa) não.
        //
        // Note que o Hub hoje SEMPRE devolve minúsculas (InstrumentoId.Create faz Trim().ToLowerInvariant()
        // incondicionalmente), então na prática não há divergência de caixa vinda de lá — o motivo de
        // não baixar caixa é de posse do conceito, não de existir maiúscula no valor.
        var idNormalizado = id.Trim();
        var clienteIdNormalizado = clienteId.Trim();
        var instrumentoIdNormalizado = instrumentoId.Trim();
        var estornaOperacaoIdNormalizado = estornaOperacaoId?.Trim();

        // Mesmo invariante do CHECK ck_operacoes_estorno_nao_auto (ver OperacaoConfiguration):
        // duplicado aqui porque, sem isso, um erro previsível de cliente vira DbUpdateException
        // (500) em vez de um 4xx no F3.
        if (estornaOperacaoIdNormalizado == idNormalizado)
        {
            return OperacaoErrors.EstornoAutoReferente;
        }

        // Mesmo invariante do CHECK ck_operacoes_estorno_coerente: bicondicional entre ser do tipo
        // estorno e carregar estorna_operacao_id.
        var temReferenciaDeEstorno = estornaOperacaoIdNormalizado is not null;
        if ((tipo == TipoOperacao.Estorno) != temReferenciaDeEstorno)
        {
            return OperacaoErrors.EstornoIncoerente;
        }

        return new Operacao(
            idNormalizado, clienteIdNormalizado, instrumentoIdNormalizado, tipo, quantidade,
            valorFinanceiro, dataEvento, registradoEm, estornaOperacaoIdNormalizado);
    }
}

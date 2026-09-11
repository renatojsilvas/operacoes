using MediatR;
using Microsoft.Extensions.Logging;
using Operacoes.Application.Catalogo;
using Operacoes.Application.Common.Interfaces;
using Operacoes.Application.Outbox;
using Operacoes.Domain.Common;
using Operacoes.Domain.Operacoes;
using Operacoes.Domain.Outbox;

namespace Operacoes.Application.Operacoes;

public sealed class RegistrarOperacaoCommandHandler(
    IHubCatalogoClient hubCatalogoClient,
    IOperacaoReadRepository operacaoReadRepository,
    IOperacaoWriteRepository operacaoWriteRepository,
    IOutboxWriteRepository outboxWriteRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<RegistrarOperacaoCommandHandler> logger)
    : IRequestHandler<RegistrarOperacaoCommand, Result<RegistrarOperacaoResultado>>
{
    public async Task<Result<RegistrarOperacaoResultado>> Handle(
        RegistrarOperacaoCommand request, CancellationToken ct)
    {
        var tipoResult = TipoOperacao.FromName(request.Tipo);
        if (tipoResult.IsFailure)
        {
            return tipoResult.Error;
        }

        var agora = timeProvider.GetUtcNow();
        var hoje = DateOnly.FromDateTime(agora.UtcDateTime);

        var clienteIdParaId = request.ClienteId?.Trim() ?? string.Empty;
        var id = OperacaoIdempotencyId.Derive(clienteIdParaId, request.IdempotencyKey);

        var operacaoResult = Operacao.Create(
            id: id,
            clienteId: request.ClienteId ?? string.Empty,
            instrumentoId: request.InstrumentoId ?? string.Empty,
            tipo: tipoResult.Value,
            quantidade: request.Quantidade,
            valorFinanceiro: request.ValorFinanceiro,
            dataEvento: request.DataEvento,
            registradoEm: agora,
            hoje: hoje,
            estornaOperacaoId: request.EstornaOperacaoId,
            valorOrigemSaldo: request.ValorOrigemSaldo);

        if (operacaoResult.IsFailure)
        {
            return operacaoResult.Error;
        }

        var operacao = operacaoResult.Value;

        var catalogoResult = await hubCatalogoClient.BuscarPorTermoAsync(operacao.InstrumentoId, ct);
        if (catalogoResult.IsFailure)
        {

            return catalogoResult.Error;
        }

        if (!CatalogoMatch.ContemExato(catalogoResult.Value, operacao.InstrumentoId))
        {
            return OperacaoErrors.InstrumentoInexistente;
        }

        if (operacao.EstornaOperacaoId is not null)
        {

            var referenciaValidaResult = await operacaoReadRepository.ExisteComoReferenciaDeEstornoAsync(
                operacao.EstornaOperacaoId, operacao.ClienteId, operacao.InstrumentoId, ct);

            if (referenciaValidaResult.IsFailure)
            {
                return referenciaValidaResult.Error;
            }

            if (!referenciaValidaResult.Value)
            {
                return OperacaoErrors.EstornoReferenciaInvalida;
            }
        }

        var adicionarOperacaoResult = await operacaoWriteRepository.AdicionarAsync(operacao, ct);
        if (adicionarOperacaoResult.IsFailure)
        {
            return adicionarOperacaoResult.Error;
        }

        var payload = TradeRegisteredPayload.Serializar(operacao);
        var outboxResult = OutboxMessage.Create(
            TradeRegisteredPayload.Tipo, TradeRegisteredPayload.RoutingKey, payload, agora);

        if (outboxResult.IsFailure)
        {

            logger.LogError(
                "Falha inesperada ao construir OutboxMessage para a operação {OperacaoId}: {Code} - {Description}",
                operacao.Id, outboxResult.Error.Code, outboxResult.Error.Description);
            return outboxResult.Error;
        }

        var adicionarOutboxResult = await outboxWriteRepository.AdicionarAsync(outboxResult.Value, ct);
        if (adicionarOutboxResult.IsFailure)
        {
            return adicionarOutboxResult.Error;
        }

        var saveResult = await unitOfWork.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            return await TratarFalhaDeGravacaoAsync(saveResult.Error, id, ct);
        }

        return ParaResultado(operacao, replay: false);
    }

    private async Task<Result<RegistrarOperacaoResultado>> TratarFalhaDeGravacaoAsync(
        Error erro, string id, CancellationToken ct)
    {
        if (erro.Type != ErrorType.Conflict)
        {

            return erro;
        }

        var existenteResult = await operacaoReadRepository.ObterPorIdAsync(id, ct);
        if (existenteResult.IsFailure)
        {
            return existenteResult.Error;
        }

        if (!existenteResult.Value.Encontrada || existenteResult.Value.Linha is null)
        {

            return OperacaoErrors.EstornoJaRealizado;
        }

        var existente = existenteResult.Value.Linha;

        return new RegistrarOperacaoResultado(
            existente.Id,
            existente.ClienteId,
            existente.InstrumentoId,
            existente.Operacao,
            existente.Quantidade,
            existente.ValorFinanceiro,
            existente.DataEvento,
            existente.RegistradoEm,
            existente.EstornaOperacaoId,
            existente.ValorOrigemSaldo,
            Replay: true);
    }

    private static RegistrarOperacaoResultado ParaResultado(Operacao operacao, bool replay) => new(
        operacao.Id,
        operacao.ClienteId,
        operacao.InstrumentoId,
        operacao.Tipo.Name,
        operacao.Quantidade,
        operacao.ValorFinanceiro,
        operacao.DataEvento,
        operacao.RegistradoEm,
        operacao.EstornaOperacaoId,
        operacao.ValorOrigemSaldo,
        replay);
}

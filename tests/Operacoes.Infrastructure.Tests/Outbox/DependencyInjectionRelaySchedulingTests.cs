using Operacoes.Infrastructure.Tests;
using Quartz;
using Quartz.Impl.Triggers;

namespace Operacoes.Infrastructure.Tests.Outbox;

[Collection(QuartzSchedulingCollection.Name)]
public sealed class DependencyInjectionRelaySchedulingTests
{
    private readonly QuartzSchedulingFixture _fixture;

    public DependencyInjectionRelaySchedulingTests(QuartzSchedulingFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddInfrastructure_RegistraOJobRelayOutboxComOTriggerDoIntervaloDaConfiguracao()
    {
        var jobDetail = await _fixture.RelayAtivoScheduler.GetJobDetail(new JobKey("relay-outbox"));
        var trigger = await _fixture.RelayAtivoScheduler.GetTrigger(new TriggerKey("relay-outbox-trigger"));

        Assert.NotNull(jobDetail);
        var simpleTrigger = Assert.IsAssignableFrom<ISimpleTrigger>(trigger);
        Assert.Equal(TimeSpan.FromSeconds(QuartzSchedulingFixture.IntervaloSegundosPadrao), simpleTrigger.RepeatInterval);
        Assert.Equal(SimpleTriggerImpl.RepeatIndefinitely, simpleTrigger.RepeatCount);
    }

    [Fact]
    public async Task AddInfrastructure_TriggerRelayOutbox_TemPoliticaDeMisfireNextWithRemainingCount()
    {
        var trigger = await _fixture.RelayAtivoScheduler.GetTrigger(new TriggerKey("relay-outbox-trigger"));

        var simpleTrigger = Assert.IsAssignableFrom<ISimpleTrigger>(trigger);
        Assert.Equal(MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount, simpleTrigger.MisfireInstruction);
    }

    [Fact]
    public async Task AddInfrastructure_QuandoAgendamentoDoRelayDesativado_NaoRegistraJobNemTrigger()
    {
        var jobDetail = await _fixture.RelayInativoScheduler.GetJobDetail(new JobKey("relay-outbox"));
        var trigger = await _fixture.RelayInativoScheduler.GetTrigger(new TriggerKey("relay-outbox-trigger"));

        Assert.Null(jobDetail);
        Assert.Null(trigger);
    }
}

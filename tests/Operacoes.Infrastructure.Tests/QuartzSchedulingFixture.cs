using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Operacoes.Infrastructure.Tests;

public sealed class QuartzSchedulingFixture : IAsyncLifetime
{
    public const int IntervaloSegundosPadrao = 5;

    private readonly List<ServiceProvider> _providers = [];

    public IScheduler RelayAtivoScheduler { get; private set; } = null!;

    public IScheduler RelayInativoScheduler { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        RelayAtivoScheduler = await BuildSchedulerAsync(BuildConfiguration());
        RelayInativoScheduler = await BuildSchedulerAsync(BuildConfiguration(relayAgendamentoAtivo: false));
    }

    public async Task DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }
    }

    private async Task<IScheduler> BuildSchedulerAsync(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        return await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    private static IConfiguration BuildConfiguration(bool? relayAgendamentoAtivo = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] =
                "Host=localhost;Port=5432;Database=operacoes_teste;Username=operacoes_app;Password=segredo",
            ["Outbox:Relay:IntervaloSegundos"] = IntervaloSegundosPadrao.ToString(),
        };

        if (relayAgendamentoAtivo is not null)
        {
            values["Outbox:Relay:AgendamentoAtivo"] = relayAgendamentoAtivo.Value.ToString();
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}

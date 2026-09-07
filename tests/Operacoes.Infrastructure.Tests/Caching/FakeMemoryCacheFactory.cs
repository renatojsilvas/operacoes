using Microsoft.Extensions.Caching.Memory;

namespace Operacoes.Infrastructure.Tests.Caching;

#pragma warning disable CS0618
internal static class FakeMemoryCacheFactory
{
    public static MemoryCache ComRelogioControlavel(TimeProvider timeProvider, long? sizeLimit = null) =>
        new(new MemoryCacheOptions
        {
            Clock = new FakeSystemClock(timeProvider),
            SizeLimit = sizeLimit,
        });

    private sealed class FakeSystemClock(TimeProvider timeProvider) : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
    }
}
#pragma warning restore CS0618

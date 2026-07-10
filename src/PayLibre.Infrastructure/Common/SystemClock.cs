using PayLibre.Application.Common.Interfaces;

namespace PayLibre.Infrastructure.Common;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

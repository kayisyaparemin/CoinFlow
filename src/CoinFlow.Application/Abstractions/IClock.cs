namespace CoinFlow.Application.Abstractions;

public interface IClock
{
    DateOnly Today { get; }
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

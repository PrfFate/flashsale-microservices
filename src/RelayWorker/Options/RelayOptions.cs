namespace RelayWorker.Options;

public sealed class RelayOptions
{
    public const string SectionName = "Relay";

    public int PollIntervalSeconds { get; init; } = 5;
    public int BatchSize { get; init; } = 50;
    public string ExchangeName { get; init; } = "orders.events";
}

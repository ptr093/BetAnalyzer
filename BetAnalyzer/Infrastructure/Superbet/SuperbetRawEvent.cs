namespace BetAnalyzer.Infrastructure.Superbet;

internal sealed class SuperbetRawEvent
{
    public required string ContainerText { get; init; }

    public string? DetailsUrl { get; init; }
}
namespace BetAnalyzer.Infrastructure.Superbet;

internal sealed class SuperbetListItem
{
    public required string HomeTeam { get; init; }

    public required string AwayTeam { get; init; }

    public string? DetailsUrl { get; init; }
}
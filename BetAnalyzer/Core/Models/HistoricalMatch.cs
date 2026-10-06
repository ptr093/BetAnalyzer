namespace BetAnalyzer.Core.Models;

public sealed class HistoricalMatch
{
    public required string TeamId { get; init; }

    public required string OpponentName { get; init; }

    public DateTime? PlayedAt { get; init; }

    public bool IsHome { get; init; }

    public int GoalsFor { get; init; }

    public int GoalsAgainst { get; init; }

    public int? CornersFor { get; init; }

    public int? CornersAgainst { get; init; }

    public int? ShotsFor { get; init; }

    public int? ShotsAgainst { get; init; }

    public int? ShotsOnTargetFor { get; init; }

    public int? ShotsOnTargetAgainst { get; init; }

    public int? ShotsOffTargetFor { get; init; }

    public int? ShotsOffTargetAgainst { get; init; }

    public int? FoulsFor { get; init; }

    public int? FoulsAgainst { get; init; }

    public int? OffsidesFor { get; init; }

    public int? OffsidesAgainst { get; init; }

    public int? YellowCardsFor { get; init; }

    public int? YellowCardsAgainst { get; init; }

    public int? RedCardsFor { get; init; }

    public int? RedCardsAgainst { get; init; }

    public int? FreeKicksFor { get; init; }

    public int? FreeKicksAgainst { get; init; }

    public int? PossessionFor { get; init; }

    public int? PossessionAgainst { get; init; }
}
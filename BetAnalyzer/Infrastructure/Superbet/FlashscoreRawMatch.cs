namespace BetAnalyzer.Infrastructure.Flashscore;

internal sealed class FlashscoreRawMatch
{
    public required string Url { get; init; }

    public required string HomeTeamName { get; init; }

    public required string AwayTeamName { get; init; }

    public required string RowText { get; init; }
}
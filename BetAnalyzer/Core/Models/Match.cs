namespace BetAnalyzer.Core.Models;
public sealed class Match
{
    public required string Id { get; init; }
    public required string HomeTeamId { get; init; }
    public required string AwayTeamId { get; init; }
    public required string HomeTeamName { get; init; }
    public required string AwayTeamName { get; init; }
    public DateTime? StartTime { get; init; }
    public string DisplayName => $"{HomeTeamName} - {AwayTeamName}";
}

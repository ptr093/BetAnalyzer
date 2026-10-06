namespace BetAnalyzer.Core.Models;
public sealed class AnalysisResult
{
    public required Match Match { get; init; }
    public required Odd Odd { get; init; }
    public double EstimatedProbability { get; init; }
    public bool IsRecommended { get; init; }
    public string CriterionText { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public decimal ImpliedProbability => Odd.Price <= 0 ? 0 : 100m / Odd.Price;
}

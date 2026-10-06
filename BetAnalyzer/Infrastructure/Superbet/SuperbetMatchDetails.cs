using BetAnalyzer.Core.Models;

namespace BetAnalyzer.Infrastructure.Superbet;

internal sealed class SuperbetMatchDetails
{
    public required Match Match { get; init; }

    public required string Url { get; init; }

    public IReadOnlyList<Odd> Odds { get; init; } =
        Array.Empty<Odd>();
}
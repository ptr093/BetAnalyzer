using BetAnalyzer.Core.Models;
namespace BetAnalyzer.Core.Contracts;
public interface IOddsProvider
{
    Task<IReadOnlyList<Match>> GetUpcomingMatchesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Odd>> GetOddsAsync(Match match, CancellationToken cancellationToken = default);
}

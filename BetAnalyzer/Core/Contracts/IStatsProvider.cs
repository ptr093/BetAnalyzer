using BetAnalyzer.Core.Models;
namespace BetAnalyzer.Core.Contracts;
public interface IStatsProvider
{
    Task<TeamStats?> GetLastMatchesAsync(string teamId, string teamName, int count, HistoryScope scope, CancellationToken cancellationToken = default);
}

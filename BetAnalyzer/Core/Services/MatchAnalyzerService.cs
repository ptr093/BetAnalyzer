using System.Globalization;
using BetAnalyzer.Core.Contracts;
using BetAnalyzer.Core.Models;

namespace BetAnalyzer.Core.Services;

public sealed class MatchAnalyzerService
{
    private readonly IOddsProvider _oddsProvider;

    private readonly IStatsProvider _statsProvider;

    private readonly HistoryMarketAnalyzer _historyAnalyzer;

    public MatchAnalyzerService(
        IOddsProvider oddsProvider,
        IStatsProvider statsProvider,
        HistoryMarketAnalyzer? historyAnalyzer = null)
    {
        _oddsProvider = oddsProvider;
        _statsProvider = statsProvider;
        _historyAnalyzer =
            historyAnalyzer ??
            new HistoryMarketAnalyzer();
    }

    public async Task<IReadOnlyList<AnalysisResult>>
        AnalyzeAsync(
        decimal minimumOdd,
        int historyCount,
        int minimumGoalMatches,
        HistoryScope scope,
        CancellationToken cancellationToken = default)
    {
        var matches =
            await _oddsProvider.GetUpcomingMatchesAsync(
                DateTime.Now,
                DateTime.Now.AddDays(2),
                cancellationToken);

        var candidates =
            new List<(Match Match, Odd Odd)>();

        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var odds =
                await _oddsProvider.GetOddsAsync(
                    match,
                    cancellationToken);

            candidates.AddRange(
                odds
                    .Where(x => x.Price > minimumOdd)
                    .Select(x => (match, x)));
        }

        var results =
            new List<AnalysisResult>();

        var statsCache =
            new Dictionary<string, TeamStats?>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var home =
                await GetStatsCachedAsync(
                    candidate.Match.HomeTeamId,
                    candidate.Match.HomeTeamName,
                    historyCount,
                    scope,
                    statsCache,
                    cancellationToken);

            var away =
                await GetStatsCachedAsync(
                    candidate.Match.AwayTeamId,
                    candidate.Match.AwayTeamName,
                    historyCount,
                    scope,
                    statsCache,
                    cancellationToken);

            if (home is null || away is null)
            {
                results.Add(
                    CreateUnavailableResult(
                        candidate.Match,
                        candidate.Odd,
                        "Brak statystyk jednej z dru¿yn."));

                continue;
            }

            var result =
                AnalyzeCandidate(
                    candidate.Match,
                    candidate.Odd,
                    home,
                    away);

            results.Add(result);
        }

        return results
            .OrderByDescending(
                x => x.IsRecommended)
            .ThenByDescending(
                x => x.EstimatedProbability)
            .ThenByDescending(
                x => x.Odd.Price)
            .ToList();
    }

    private AnalysisResult AnalyzeCandidate(
        Match match,
        Odd odd,
        TeamStats home,
        TeamStats away)
    {
        // Ca³y mecz / suma obu dru¿yn:
        // zachowujemy dotychczasow¹ analizê,
        // która korzysta z sum statystyk.
        if (odd.Scope == MarketScope.Match)
        {
            var analysis =
                _historyAnalyzer.Analyze(
                    odd,
                    home,
                    away);

            return new AnalysisResult
            {
                Match = match,
                Odd = odd,
                EstimatedProbability =
                    analysis.IsAvailable
                        ? analysis.Percentage
                        : 0,

                IsRecommended =
                    analysis.IsAvailable &&
                    analysis.Passed,

                CriterionText =
                    analysis.IsAvailable
                        ? analysis.Passed
                            ? "Tak"
                            : "Nie"
                        : "Brak danych",

                Details =
                    PrefixDetails(
                        odd,
                        analysis.Details)
            };
        }

        // Konkretna dru¿yna:
        // liczymy wy³¹cznie "For", czyli statystykê
        // wykonan¹ przez wskazan¹ dru¿ynê.
        return AnalyzeTeamMarket(
            match,
            odd,
            home,
            away);
    }

    private static AnalysisResult AnalyzeTeamMarket(
        Match match,
        Odd odd,
        TeamStats home,
        TeamStats away)
    {
        if (odd.Period != MarketPeriod.FullTime)
        {
            return CreateUnavailableResult(
                match,
                odd,
                "Brak danych historycznych dla wskazanej po³owy.");
        }

        if (!odd.Line.HasValue)
        {
            return CreateUnavailableResult(
                match,
                odd,
                "Brak linii kursowej.");
        }

        var values =
            GetTeamValuesForOdd(
                odd,
                match,
                home,
                away);

        if (values.Count == 0)
        {
            return CreateUnavailableResult(
                match,
                odd,
                "Brak kompletnych statystyk dla wskazanej dru¿yny.");
        }

        var selection =
            NormalizeSelection(
                odd.Selection);

        if (selection is null)
        {
            return CreateUnavailableResult(
                match,
                odd,
                $"Nieobs³ugiwany wybór: {odd.Selection}.");
        }

        var line =
            (double)odd.Line.Value;

        var hits =
            selection == "Over"
                ? values.Count(x => x > line)
                : values.Count(x => x < line);

        var probability =
            (double)hits / values.Count;

        var percentage =
            probability * 100.0;

        var isRecommended =
            IsTeamMarketRecommended(
                probability,
                (double)odd.Price);

        return new AnalysisResult
        {
            Match = match,
            Odd = odd,
            EstimatedProbability = percentage,
            IsRecommended = isRecommended,
            CriterionText =
                isRecommended
                    ? "Tak"
                    : "Nie",

            Details =
                BuildTeamDetails(
                    odd,
                    values,
                    hits,
                    probability)
        };
    }

    private static IReadOnlyList<double>
        GetTeamValuesForOdd(
        Odd odd,
        Match match,
        TeamStats home,
        TeamStats away)
    {
        if (string.IsNullOrWhiteSpace(
                odd.TeamName))
        {
            return Array.Empty<double>();
        }

        TeamStats? selectedStats =
            null;

        if (string.Equals(
                odd.TeamName,
                match.HomeTeamName,
                StringComparison.OrdinalIgnoreCase))
        {
            selectedStats = home;
        }
        else if (string.Equals(
                     odd.TeamName,
                     match.AwayTeamName,
                     StringComparison.OrdinalIgnoreCase))
        {
            selectedStats = away;
        }

        if (selectedStats is null)
        {
            return Array.Empty<double>();
        }

        return odd.Market switch
        {
            MarketType.Corners =>
                GetValues(
                    selectedStats,
                    x => x.CornersFor),

            MarketType.Cards =>
                GetValues(
                    selectedStats,
                    x => x.YellowCardsFor),

            _ =>
                Array.Empty<double>()
        };
    }

    private static IReadOnlyList<double>
        GetValues(
        TeamStats stats,
        Func<HistoricalMatch, int?> selector)
    {
        return stats.Matches
            .Select(selector)
            .Where(x => x.HasValue)
            .Select(x => (double)x!.Value)
            .ToList();
    }

    private static bool IsTeamMarketRecommended(
        double probability,
        double price)
    {
        if (price <= 1.0)
        {
            return false;
        }

        // Wymagamy co najmniej 75% trafieñ.
        if (probability < 0.75)
        {
            return false;
        }

        // Wartoœæ oczekiwana dodatnia.
        return probability * price > 1.0;
    }

    private static string BuildTeamDetails(
        Odd odd,
        IReadOnlyList<double> values,
        int hits,
        double probability)
    {
        var period =
            GetPeriodText(
                odd.Period);

        var team =
            string.IsNullOrWhiteSpace(
                odd.TeamName)
                ? "Dru¿yna: nieokreœlona"
                : $"Dru¿yna: {odd.TeamName}";

        var line =
            odd.Line?.ToString(
                "0.0",
                CultureInfo.GetCultureInfo(
                    "pl-PL"))
            ?? "(brak)";

        var valuesText =
            string.Join(
                ", ",
                values.Select(
                    x => x.ToString(
                        "0.0",
                        CultureInfo.InvariantCulture)));

        return
            $"{period}; {team}; " +
            $"{hits}/{values.Count} " +
            $"({probability:P0}), " +
            $"linia {line}, " +
            $"wartoœci: {valuesText}";
    }

    private static AnalysisResult
        CreateUnavailableResult(
        Match match,
        Odd odd,
        string reason)
    {
        return new AnalysisResult
        {
            Match = match,
            Odd = odd,
            EstimatedProbability = 0,
            IsRecommended = false,
            CriterionText = "Brak danych",
            Details =
                $"{GetPeriodText(odd.Period)}; " +
                $"{GetScopeText(odd)}; " +
                $"{reason}"
        };
    }

    private static string PrefixDetails(
        Odd odd,
        string details)
    {
        var prefix =
            $"{GetPeriodText(odd.Period)}; " +
            $"{GetScopeText(odd)}";

        if (string.IsNullOrWhiteSpace(details))
        {
            return prefix;
        }

        if (details.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return details;
        }

        return
            $"{prefix}; {details}";
    }

    private static string GetScopeText(
        Odd odd)
    {
        if (odd.Scope == MarketScope.Team)
        {
            return string.IsNullOrWhiteSpace(
                odd.TeamName)
                ? "Dru¿yna: nieokreœlona"
                : $"Dru¿yna: {odd.TeamName}";
        }

        return "Suma meczu";
    }

    private static string GetPeriodText(
        MarketPeriod period)
    {
        return period switch
        {
            MarketPeriod.FullTime =>
                "Ca³y mecz",

            MarketPeriod.FirstHalf =>
                "1. po³owa",

            MarketPeriod.SecondHalf =>
                "2. po³owa",

            _ =>
                period.ToString()
        };
    }

    private static string? NormalizeSelection(
        string selection)
    {
        if (string.Equals(
                selection,
                "Over",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                selection,
                "Powy¿ej",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Over";
        }

        if (string.Equals(
                selection,
                "Under",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                selection,
                "Poni¿ej",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Under";
        }

        return null;
    }

    private async Task<TeamStats?>
        GetStatsCachedAsync(
        string id,
        string name,
        int count,
        HistoryScope scope,
        Dictionary<string, TeamStats?> cache,
        CancellationToken cancellationToken)
    {
        var key =
            $"{id}|{count}|{scope}";

        if (cache.TryGetValue(
                key,
                out var value))
        {
            return value;
        }

        value =
            await _statsProvider.GetLastMatchesAsync(
                id,
                name,
                count,
                scope,
                cancellationToken);

        cache[key] = value;

        return value;
    }
}
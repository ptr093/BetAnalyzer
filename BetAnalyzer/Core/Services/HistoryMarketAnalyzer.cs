using System.Globalization;
using System.Text.RegularExpressions;
using BetAnalyzer.Core.Models;

namespace BetAnalyzer.Core.Services;

public sealed class HistoryMarketAnalyzer
{
    public MarketHistoryAnalysis Analyze(
        Odd odd,
        TeamStats homeStats,
        TeamStats? awayStats = null)
    {
        if (homeStats.Matches.Count == 0 &&
            (awayStats?.Matches.Count ?? 0) == 0)
        {
            return MarketHistoryAnalysis.NoData(
                "Brak historii meczów.");
        }

        if (odd.Market == MarketType.Btts)
        {
            return AnalyzeBtts(
                homeStats,
                awayStats);
        }

        var marketText =
            Normalize(
                $"{odd.EventName} {odd.Selection}");

        var line =
            odd.Line ??
            TryReadLine(marketText);

        if (!line.HasValue)
        {
            return MarketHistoryAnalysis.NoData(
                "Brak linii zdarzenia.");
        }

        var values =
            ExtractValues(
                odd.Market,
                homeStats);

        if (values.Count == 0)
        {
            return MarketHistoryAnalysis.NoData(
                "Brak kompletnych statystyk dla rynku.");
        }

        var isOver =
            odd.Selection.Equals(
                "Over",
                StringComparison.OrdinalIgnoreCase) ||
            marketText.Contains("powyzej");

        var hitCount =
            values.Count(
                value => isOver
                    ? value > line.Value
                    : value < line.Value);

        var percentage =
            hitCount /
            (double)values.Count *
            100.0;

        return new MarketHistoryAnalysis
        {
            IsAvailable = true,
            HitCount = hitCount,
            SampleSize = values.Count,
            Percentage = percentage,
            Passed = percentage >= 80.0,
            Details =
                $"{hitCount}/{values.Count} " +
                $"({percentage:0}%), " +
                $"linia {line.Value:0.0}, " +
                $"wartości: " +
                $"{string.Join(
                    ", ",
                    values.Select(
                        x => x.ToString(
                            "0.0",
                            CultureInfo.InvariantCulture)))}"
        };
    }

    private static MarketHistoryAnalysis AnalyzeBtts(
        TeamStats homeStats,
        TeamStats? awayStats)
    {
        if (awayStats is null ||
            homeStats.Matches.Count == 0 ||
            awayStats.Matches.Count == 0)
        {
            return MarketHistoryAnalysis.NoData(
                "BTTS wymaga statystyk obu drużyn.");
        }

        var homeSample =
            homeStats.Matches
                .Take(5)
                .ToList();

        var awaySample =
            awayStats.Matches
                .Take(5)
                .ToList();

        var sampleSize =
            Math.Min(
                homeSample.Count,
                awaySample.Count);

        if (sampleSize == 0)
        {
            return MarketHistoryAnalysis.NoData(
                "Brak meczów do analizy BTTS.");
        }

        var homeHits =
            homeSample
                .Take(sampleSize)
                .Count(x => x.GoalsFor > 0);

        var awayHits =
            awaySample
                .Take(sampleSize)
                .Count(x => x.GoalsFor > 0);

        var bothTeamsMeetMinimum =
            homeHits >= 4 &&
            awayHits >= 4;

        var percentage =
            Math.Min(
                homeHits,
                awayHits) /
            (double)sampleSize *
            100.0;

        return new MarketHistoryAnalysis
        {
            IsAvailable = true,
            HitCount = Math.Min(
                homeHits,
                awayHits),
            SampleSize = sampleSize,
            Percentage = percentage,
            Passed = bothTeamsMeetMinimum,
            Details =
                $"Gole: gospodarz " +
                $"{homeHits}/{sampleSize}, " +
                $"gość {awayHits}/{sampleSize}; " +
                $"BTTS {percentage:0}%"
        };
    }

    private static List<decimal> ExtractValues(
        MarketType market,
        TeamStats stats)
    {
        return market switch
        {
            MarketType.Corners =>
                stats.Matches
                    .Where(x =>
                        x.CornersFor.HasValue &&
                        x.CornersAgainst.HasValue)
                    .Select(x =>
                        (decimal)x.CornersFor!.Value +
                        x.CornersAgainst!.Value)
                    .ToList(),

            MarketType.ShotsOnTarget =>
                stats.Matches
                    .Where(x =>
                        x.ShotsOnTargetFor.HasValue &&
                        x.ShotsOnTargetAgainst.HasValue)
                    .Select(x =>
                        (decimal)x.ShotsOnTargetFor!.Value +
                        x.ShotsOnTargetAgainst!.Value)
                    .ToList(),

            _ => []
        };
    }

    private static decimal? TryReadLine(
        string text)
    {
        var match =
            Regex.Match(
                text,
                @"(?<!\d)(\d{1,2}[.,]\d)(?!\d)");

        if (!match.Success)
            return null;

        var value =
            match.Groups[1]
                .Value
                .Replace(',', '.');

        return decimal.TryParse(
            value,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var line)
            ? line
            : null;
    }

    private static string Normalize(
        string value)
    {
        return value
            .ToLowerInvariant()
            .Replace("ą", "a")
            .Replace("ę", "e")
            .Replace("ł", "l")
            .Replace("ń", "n")
            .Replace("ó", "o")
            .Replace("ś", "s")
            .Replace("ź", "z")
            .Replace("ż", "z");
    }
}

public sealed class MarketHistoryAnalysis
{
    public bool IsAvailable { get; init; }

    public bool Passed { get; init; }

    public int HitCount { get; init; }

    public int SampleSize { get; init; }

    public double Percentage { get; init; }

    public string Details { get; init; } =
        string.Empty;

    public static MarketHistoryAnalysis NoData(
        string details)
    {
        return new MarketHistoryAnalysis
        {
            IsAvailable = false,
            Passed = false,
            HitCount = 0,
            SampleSize = 0,
            Percentage = 0,
            Details = details
        };
    }
}
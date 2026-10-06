using System.Globalization;
using System.Text.RegularExpressions;
using BetAnalyzer.Core.Models;

using BetMatch = BetAnalyzer.Core.Models.Match;
using RegexMatch = System.Text.RegularExpressions.Match;
using Regex = System.Text.RegularExpressions.Regex;

namespace BetAnalyzer.Infrastructure.Superbet;

internal static class SuperbetParser
{
    private static readonly Regex DecimalNumberRegex =
        new(
            @"(?<![\d.])\d{1,3}(?:[.,]\d{1,3})?(?![\d.])",
            RegexOptions.Compiled);

    private static readonly Regex TeamSeparatorRegex =
        new(
            @"(?<home>.+?)\s+(?:vs?\.?|[-–—])\s+(?<away>.+?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] NonTeamLines =
    [
        "bet builder", "mecz", "remis w meczu", "obstaw",
        "obejrzyj mecz", "pokaż więcej", "więcej", "live",
        "dzisiaj", "jutro", "wczoraj", "kurs", "piłka nożna"
    ];

    public static bool TryParseMatch(
        SuperbetRawEvent raw,
        out BetMatch? match)
    {
        match = null;
        var lines = NormalizeLines(raw.ContainerText);

        if (lines.Count < 2 ||
            !TryFindTeams(lines, out var homeTeam, out var awayTeam))
        {
            return false;
        }

        match = new BetMatch
        {
            Id = BuildMatchId(raw.DetailsUrl, homeTeam, awayTeam),
            HomeTeamId = NormalizeTeamId(homeTeam),
            AwayTeamId = NormalizeTeamId(awayTeam),
            HomeTeamName = homeTeam,
            AwayTeamName = awayTeam
        };

        return true;
    }

    public static IReadOnlyList<Odd> ParseOdds(
        string matchId,
        string containerText)
    {
        var result = new List<Odd>();
        var lines = NormalizeLines(containerText);
        var market = DetectMarket(lines);

        if (market is null)
            return result;

        foreach (var line in lines)
        {
            if (!TryExtractPrice(line, out var price) || price <= 1.0m)
                continue;

            var selection = DetectSelection(market.Value, line);

            if (selection is null)
                continue;

            result.Add(new Odd
            {
                MatchId = matchId,
                Market = market.Value,
                Selection = selection,
                Line = DetectLine(line),
                Price = price
            });
        }

        return result;
    }

    private static IReadOnlyList<string> NormalizeLines(string text)
    {
        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Regex.Replace(x, @"\s+", " ").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryFindTeams(
        IReadOnlyList<string> lines,
        out string home,
        out string away)
    {
        foreach (var line in lines)
        {
            RegexMatch regexMatch = TeamSeparatorRegex.Match(line);

            if (!regexMatch.Success)
                continue;

            var parsedHome = CleanTeamName(
                regexMatch.Groups["home"].Value);

            var parsedAway = CleanTeamName(
                regexMatch.Groups["away"].Value);

            if (IsValidTeamName(parsedHome) &&
                IsValidTeamName(parsedAway))
            {
                home = parsedHome;
                away = parsedAway;
                return true;
            }
        }

        var candidates = lines
            .Where(IsValidTeamName)
            .Where(x => !ContainsOdd(x))
            .Take(2)
            .ToList();

        if (candidates.Count == 2)
        {
            home = CleanTeamName(candidates[0]);
            away = CleanTeamName(candidates[1]);
            return true;
        }

        home = string.Empty;
        away = string.Empty;
        return false;
    }

    private static string CleanTeamName(string value)
    {
        value = Regex.Replace(value, @"\s+", " ");
        return value.Trim(' ', '-', '–', '—', ':');
    }

    private static bool IsValidTeamName(string value)
    {
        if (value.Length < 2 || value.Length > 80 || ContainsOdd(value))
            return false;

        var lower = value.ToLowerInvariant();
        return !NonTeamLines.Any(lower.Contains);
    }

    private static bool ContainsOdd(string value)
    {
        return DecimalNumberRegex.IsMatch(value.Replace(',', '.'));
    }

    private static MarketType? DetectMarket(IReadOnlyList<string> lines)
    {
        var text = string.Join(" ", lines).ToLowerInvariant();

        if (text.Contains("obie drużyny strzelą") ||
            text.Contains("obie drużyny strzelą gola") ||
            text.Contains("btts"))
        {
            return MarketType.Btts;
        }

        if (text.Contains("rzuty rożne") ||
            text.Contains("rożne") ||
            text.Contains("corners"))
        {
            return MarketType.Corners;
        }

        if (text.Contains("strzały celne") ||
            text.Contains("celnych strzałów") ||
            text.Contains("shots on target"))
        {
            return MarketType.ShotsOnTarget;
        }

        return null;
    }

    private static string? DetectSelection(
        MarketType market,
        string line)
    {
        var lower = line.ToLowerInvariant();

        if (market == MarketType.Btts)
        {
            if (lower.Contains("tak") || lower.Contains("yes"))
                return "Tak";

            if (lower.Contains("nie") || lower.Contains("no"))
                return "Nie";
        }

        if (lower.Contains("powyżej") || lower.Contains("over"))
            return "Over";

        if (lower.Contains("poniżej") || lower.Contains("under"))
            return "Under";

        return null;
    }

    private static bool TryExtractPrice(
        string text,
        out decimal price)
    {
        price = 0;
        var matches = DecimalNumberRegex.Matches(
            text.Replace(',', '.'));

        foreach (RegexMatch regexMatch in matches.Reverse())
        {
            if (decimal.TryParse(
                    regexMatch.Value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var parsed) &&
                parsed > 1.0m &&
                parsed <= 1000m)
            {
                price = parsed;
                return true;
            }
        }

        return false;
    }

    private static decimal? DetectLine(string text)
    {
        var normalized = text.Replace(',', '.');
        RegexMatch lineMatch = Regex.Match(
            normalized,
            @"(?<!\d)\d{1,2}[.][05](?!\d)");

        if (!lineMatch.Success)
            return null;

        return decimal.TryParse(
            lineMatch.Value,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var line)
            ? line
            : null;
    }

    private static string BuildMatchId(
        string? href,
        string home,
        string away)
    {
        if (!string.IsNullOrWhiteSpace(href))
            return href;

        return $"{NormalizeTeamId(home)}_{NormalizeTeamId(away)}";
    }

    private static string NormalizeTeamId(string value)
    {
        return Regex.Replace(
                value.ToLowerInvariant(),
                @"[^a-z0-9ąćęłńóśźż]+",
                "-")
            .Trim('-');
    }
}
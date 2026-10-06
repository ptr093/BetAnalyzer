namespace BetAnalyzer.Infrastructure.Flashscore;

public static class FlashscoreSelectors
{
    public const string BaseUrl =
        "https://www.flashscore.pl";

    public static readonly string[] SearchResults =
    [
        "a[href*='/druzyna/']",
        "a[href*='/team/']",
        "[data-testid*='search-result'] a",
        ".searchResult a",
        ".searchResult__participant a"
    ];

    public static readonly string[] TeamMatchRows =
    [
        ".event__match",
        ".event__match--static",
        "[data-testid='event-row']",
        "[data-testid*='event']"
    ];

    public static readonly string[] TeamMatchLinks =
    [
        "a[href*='/mecz/pilka-nozna/']",
        "a[href*='/match/football/']",
        "a[href*='/mecz/']"
    ];

    public static readonly string[] StatisticsLinks =
    [
        "a[href*='/szczegoly/statystyki/']",
        "a[href*='/summary/stats']",
        "a:has-text('Statystyki')",
        "button:has-text('Statystyki')"
    ];

    public static readonly string[] StatisticsRows =
    [
        ".statRow",
        ".statRow__item",
        "[data-testid*='stat-row']",
        "[class*='statRow']",
        "[class*='statistics']"
    ];

    public static readonly string[] HomeValues =
    [
        ".statRow__homeValue",
        ".statRow__value--home",
        "[data-testid*='home-value']",
        "[class*='homeValue']"
    ];

    public static readonly string[] AwayValues =
    [
        ".statRow__awayValue",
        ".statRow__value--away",
        "[data-testid*='away-value']",
        "[class*='awayValue']"
    ];

    public static readonly string[] StatisticLabels =
    [
        ".statRow__name",
        ".statRow__title",
        "[data-testid*='stat-name']",
        "[class*='statName']",
        "[class*='statLabel']"
    ];
}
namespace BetAnalyzer.Infrastructure.Superbet;

public static class SuperbetSelectors
{
    public const string FootballTodayUrl =
        "https://superbet.pl/zaklady-bukmacherskie/" +
        "pilka-nozna?day=dzisiaj";

    public static readonly string[] EventCards =
    [
        "[class*='event-card']",
        "[class*='eventCard']",
        "[class*='fixture-card']",
        "[class*='fixtureCard']"
    ];

    public static readonly string[] Categories =
    [
        "Rzuty ro¿ne",
        "Kartki",
        "Statystyki"
    ];
}
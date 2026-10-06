namespace BetAnalyzer.Core.Models;

public sealed class Odd
{
    public required string MatchId { get; init; }

    public required MarketType Market { get; init; }

    public required string Selection { get; init; }

    public decimal? Line { get; init; }

    public decimal Price { get; init; }

    public MarketScope Scope { get; init; } =
        MarketScope.Match;

    public string? TeamName { get; init; }

    public string EventName =>
        Market switch
        {
            MarketType.Btts =>
                $"BTTS {Selection}",

            MarketType.Corners =>
                $"Rzuty rożne {Selection} " +
                $"{Line:0.0}",

            MarketType.Cards =>
                $"Kartki {Selection} " +
                $"{Line:0.0}",

            MarketType.MatchStatistics =>
                $"Statystyki {Selection} " +
                $"{Line:0.0}",

            MarketType.Shots =>
                $"Strzały {Selection} " +
                $"{Line:0.0}",

            MarketType.ShotsOnTarget =>
                $"Strzały celne {Selection} " +
                $"{Line:0.0}",

            MarketType.Fouls =>
                $"Faule {Selection} " +
                $"{Line:0.0}",

            MarketType.Offsides =>
                $"Spalone {Selection} " +
                $"{Line:0.0}",

            MarketType.YellowCards =>
                $"Żółte kartki {Selection} " +
                $"{Line:0.0}",

            MarketType.RedCards =>
                $"Czerwone kartki {Selection} " +
                $"{Line:0.0}",

            MarketType.Goals =>
                $"Gole {Selection} " +
                $"{Line:0.0}",

            _ =>
                "Nieznany rynek"
        };
    public MarketPeriod Period { get; init; }

   
}
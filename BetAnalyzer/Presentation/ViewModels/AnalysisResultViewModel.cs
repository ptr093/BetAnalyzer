using System.Globalization;
using BetAnalyzer.Core.Models;

namespace BetAnalyzer.Presentation.ViewModels;

public sealed class AnalysisResultViewModel
{
    private readonly AnalysisResult _result;

    public AnalysisResultViewModel(
        AnalysisResult result)
    {
        _result = result;
    }

    public string Match =>
        _result.Match.DisplayName;

    public string EventType =>
        BuildEventType(
            _result.Odd);

    public decimal Odd =>
        _result.Odd.Price;

    // WPF StringFormat={}{0:0.0}% wymaga wartoœci 0-1,
    // dlatego Probability jest w formie np. 0.65 dla 65%.
    public double Probability =>
        _result.EstimatedProbability / 100.0;

    public double ProbabilityPercent =>
        Probability * 100.0;

    public double ImpliedProbability =>
        Odd <= 0m
            ? 0d
            : (double)(1m / Odd);

    public double ImpliedProbabilityPercent =>
        ImpliedProbability * 100.0;

    // Edge = Twoje prawdopodobieñstwo - prawdopodobieñstwo z kursu.
    public double Edge =>
        Probability - ImpliedProbability;

    public double EdgePercent =>
        Edge * 100.0;

    // Dla pojedynczej stawki:
    // EV = p * kurs - 1.
    // Dodatnie EV oznacza teoretyczn¹ przewagê.
    public double ExpectedValue =>
        (Probability * (double)Odd) - 1.0;

    public double ExpectedValuePercent =>
        ExpectedValue * 100.0;

    // Pe³na frakcja Kelly.
    public double KellyFraction
    {
        get
        {
            if (Odd <= 1m ||
                ExpectedValue <= 0d)
            {
                return 0d;
            }

            var value =
                ExpectedValue /
                ((double)Odd - 1.0);

            return Math.Clamp(
                value,
                0d,
                1d);
        }
    }

    public double KellyPercent =>
        KellyFraction * 100.0;

    // Æwieræ Kelly i limit maksymalnie 2% bankrolla.
    public double SuggestedStakePercent =>
        Math.Min(
            KellyFraction * 0.25,
            0.02) * 100.0;

    public string ConfidenceLevel
    {
        get
        {
            if (!_result.IsRecommended ||
                ExpectedValue <= 0d)
            {
                return "Brak value";
            }

            if (ProbabilityPercent >= 70d &&
                EdgePercent >= 8d &&
                ExpectedValuePercent >= 8d)
            {
                return "Bardzo wysokie";
            }

            if (ProbabilityPercent >= 60d &&
                EdgePercent >= 5d &&
                ExpectedValuePercent >= 5d)
            {
                return "Wysokie";
            }

            if (ProbabilityPercent >= 52d &&
                EdgePercent >= 2d &&
                ExpectedValuePercent >= 2d)
            {
                return "Umiarkowane";
            }

            return "Niskie";
        }
    }

    public string FiveMatchesCriterion =>
        _result.CriterionText;

    public string Recommendation =>
        _result.IsRecommended &&
        ExpectedValue > 0d
            ? "Rekomendowane"
            : "Odrzucone";

    public string Details =>
        _result.Details;

    public string Market =>
        GetMarketText(
            _result.Odd.Market);

    public string Period =>
        GetPeriodText(
            _result.Odd.Period);

    public string Scope =>
        GetScopeText(
            _result.Odd);

    public string Selection =>
        GetSelectionText(
            _result.Odd.Selection);

    public decimal? Line =>
        _result.Odd.Line;

    private static string BuildEventType(
        Odd odd)
    {
        var market =
            GetMarketText(
                odd.Market);

        var period =
            GetPeriodText(
                odd.Period);

        var scope =
            GetScopeText(odd);

        var selection =
            GetSelectionText(
                odd.Selection);

        var line =
            odd.Line.HasValue
                ? odd.Line.Value.ToString(
                    "0.0",
                    CultureInfo.GetCultureInfo(
                        "pl-PL"))
                : "(brak linii)";

        return
            $"{market} | " +
            $"{period} | " +
            $"{scope} | " +
            $"{selection} {line}";
    }

    private static string GetMarketText(
        MarketType market)
    {
        return market switch
        {
            MarketType.Corners =>
                "Rzuty ro¿ne",

            MarketType.Cards =>
                "Kartki",

            MarketType.ShotsOnTarget =>
                "Celne strza³y",

            MarketType.Offsides =>
                "Spalone",

            _ =>
                market.ToString()
        };
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

    private static string GetSelectionText(
        string selection)
    {
        if (string.Equals(
                selection,
                "Over",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Powy¿ej";
        }

        if (string.Equals(
                selection,
                "Under",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Poni¿ej";
        }

        return selection;
    }
}
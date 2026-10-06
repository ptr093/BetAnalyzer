namespace BetAnalyzer.Core.Models;

public sealed class TeamStats
{
    public required string TeamId { get; init; }

    public required string TeamName { get; init; }

    public IReadOnlyList<HistoricalMatch> Matches { get; init; } =
        Array.Empty<HistoricalMatch>();

    public int SampleSize =>
        Matches.Count;

    public int MatchesWithScoredGoal =>
        Matches.Count(x => x.GoalsFor > 0);

    public int MatchesWithBtts =>
        Matches.Count(x =>
            x.GoalsFor > 0 &&
            x.GoalsAgainst > 0);

    public double GoalScoringRate =>
        SampleSize == 0
            ? 0
            : MatchesWithScoredGoal /
              (double)SampleSize *
              100.0;

    public double BttsRate =>
        SampleSize == 0
            ? 0
            : MatchesWithBtts /
              (double)SampleSize *
              100.0;

    public int CornersSampleSize =>
        Matches.Count(x =>
            x.CornersFor.HasValue &&
            x.CornersAgainst.HasValue);

    public int ShotsSampleSize =>
        Matches.Count(x =>
            x.ShotsFor.HasValue &&
            x.ShotsAgainst.HasValue);

    public int ShotsOnTargetSampleSize =>
        Matches.Count(x =>
            x.ShotsOnTargetFor.HasValue &&
            x.ShotsOnTargetAgainst.HasValue);

    public int FoulsSampleSize =>
        Matches.Count(x =>
            x.FoulsFor.HasValue &&
            x.FoulsAgainst.HasValue);

    public int OffsidesSampleSize =>
        Matches.Count(x =>
            x.OffsidesFor.HasValue &&
            x.OffsidesAgainst.HasValue);

    public int CardsSampleSize =>
        Matches.Count(x =>
            x.YellowCardsFor.HasValue &&
            x.YellowCardsAgainst.HasValue);

    public double AverageTotalCorners =>
        Average(
            Matches
                .Where(x =>
                    x.CornersFor.HasValue &&
                    x.CornersAgainst.HasValue)
                .Select(x =>
                    x.CornersFor!.Value +
                    x.CornersAgainst!.Value));

    public double AverageTotalShots =>
        Average(
            Matches
                .Where(x =>
                    x.ShotsFor.HasValue &&
                    x.ShotsAgainst.HasValue)
                .Select(x =>
                    x.ShotsFor!.Value +
                    x.ShotsAgainst!.Value));

    public double AverageTotalShotsOnTarget =>
        Average(
            Matches
                .Where(x =>
                    x.ShotsOnTargetFor.HasValue &&
                    x.ShotsOnTargetAgainst.HasValue)
                .Select(x =>
                    x.ShotsOnTargetFor!.Value +
                    x.ShotsOnTargetAgainst!.Value));

    public double AverageTotalShotsOffTarget =>
        Average(
            Matches
                .Where(x =>
                    x.ShotsOffTargetFor.HasValue &&
                    x.ShotsOffTargetAgainst.HasValue)
                .Select(x =>
                    x.ShotsOffTargetFor!.Value +
                    x.ShotsOffTargetAgainst!.Value));

    public double AverageTotalFouls =>
        Average(
            Matches
                .Where(x =>
                    x.FoulsFor.HasValue &&
                    x.FoulsAgainst.HasValue)
                .Select(x =>
                    x.FoulsFor!.Value +
                    x.FoulsAgainst!.Value));

    public double AverageTotalOffsides =>
        Average(
            Matches
                .Where(x =>
                    x.OffsidesFor.HasValue &&
                    x.OffsidesAgainst.HasValue)
                .Select(x =>
                    x.OffsidesFor!.Value +
                    x.OffsidesAgainst!.Value));

    public double AverageTotalYellowCards =>
        Average(
            Matches
                .Where(x =>
                    x.YellowCardsFor.HasValue &&
                    x.YellowCardsAgainst.HasValue)
                .Select(x =>
                    x.YellowCardsFor!.Value +
                    x.YellowCardsAgainst!.Value));

    private static double Average(
        IEnumerable<int> values)
    {
        var list = values.ToList();

        return list.Count == 0
            ? 0
            : list.Average();
    }
}
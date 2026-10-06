using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Win32;
using BetAnalyzer.Core.Models;
using BetAnalyzer.Core.Services;
using BetAnalyzer.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetAnalyzer.Presentation.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly MatchAnalyzerService _analyzer;
    private readonly ExcelExportService _excelExportService;

    private CancellationTokenSource? _cts;

    public MainViewModel(
        MatchAnalyzerService analyzer,
        ExcelExportService excelExportService)
    {
        _analyzer = analyzer;
        _excelExportService = excelExportService;

        Results = new();
    }

    public ObservableCollection<AnalysisResultViewModel>
        Results
    { get; }

    public IReadOnlyList<HistoryScope>
        HistoryScopes
    { get; } =
        [
            HistoryScope.All,
            HistoryScope.HomeOnly,
            HistoryScope.AwayOnly
        ];

    [ObservableProperty]
    private string minimumOddText = "1.40";

    [ObservableProperty]
    private int historyCount = 5;

    [ObservableProperty]
    private int minimumGoalMatches = 4;

    [ObservableProperty]
    private HistoryScope selectedHistoryScope =
        HistoryScope.All;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Gotowy.";

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAnalysisAsync()
    {
        if (!TryReadParameters(
                out var minimumOdd,
                out var validationError))
        {
            StatusMessage = validationError;

            return;
        }

        IsBusy = true;

        Results.Clear();

        NotifyResultsChanged();

        _cts?.Cancel();
        _cts?.Dispose();

        _cts =
            new CancellationTokenSource();

        var cancellationToken =
            _cts.Token;

        StatusMessage =
            "Pobieranie kursów, meczów i statystyk...";

        try
        {
            var data =
                await _analyzer.AnalyzeAsync(
                    minimumOdd,
                    HistoryCount,
                    MinimumGoalMatches,
                    SelectedHistoryScope,
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var addedCount = 0;

            foreach (var item in data)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Results.Add(
                    new AnalysisResultViewModel(
                        item));

                addedCount++;
            }

            NotifyResultsChanged();

            StatusMessage =
                Results.Count == 0
                    ? "Zakończono. Nie znaleziono wyników spełniających kryteria."
                    : $"Zakończono. Dodano wyników: " +
                      $"{addedCount}. Łącznie: " +
                      $"{Results.Count}.";
        }
        catch (OperationCanceledException)
        {
            NotifyResultsChanged();

            StatusMessage =
                Results.Count > 0
                    ? $"Analiza anulowana. Zachowano " +
                      $"{Results.Count} wyników."
                    : "Analiza anulowana.";
        }
        catch (TimeoutException ex)
        {
            NotifyResultsChanged();

            StatusMessage =
                Results.Count > 0
                    ? $"Analiza częściowa. Zachowano " +
                      $"{Results.Count} wyników. " +
                      $"Ostatni timeout: {ShortenError(ex.Message)}"
                    : $"Timeout analizy: " +
                      $"{ShortenError(ex.Message)}";
        }
        catch (Exception ex)
        {
            NotifyResultsChanged();

            StatusMessage =
                Results.Count > 0
                    ? $"Analiza częściowa. Zachowano " +
                      $"{Results.Count} wyników. " +
                      $"Błąd: {ShortenError(ex.Message)}"
                    : $"Błąd analizy: " +
                      $"{ShortenError(ex.Message)}";
        }
        finally
        {
            IsBusy = false;

            if (ReferenceEquals(
                    _cts?.Token,
                    cancellationToken))
            {
                _cts?.Dispose();
                _cts = null;
            }

            NotifyResultsChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportToExcel))]
    private async Task ExportToExcelAsync()
    {
        if (Results.Count == 0)
        {
            StatusMessage =
                "Brak wyników do eksportu.";

            return;
        }

        var dialog =
            new SaveFileDialog
            {
                Title =
                    "Zapisz raport BetAnalyzer",

                Filter =
                    "Plik Excel (*.xlsx)|*.xlsx",

                DefaultExt =
                    ".xlsx",

                AddExtension =
                    true,

                OverwritePrompt =
                    true,

                FileName =
                    $"BetAnalyzer_" +
                    $"{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };

        var selected =
            dialog.ShowDialog();

        if (selected != true)
        {
            StatusMessage =
                "Eksport do Excela anulowany.";

            return;
        }

        IsBusy = true;

        try
        {
            StatusMessage =
                "Generowanie pliku Excel...";

            var snapshot =
                Results.ToList();

            await Task.Run(
                () =>
                    _excelExportService.Export(
                        snapshot,
                        dialog.FileName));

            StatusMessage =
                $"Zapisano raport Excel. " +
                $"Liczba wyników: {snapshot.Count}. " +
                $"Plik: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"Błąd eksportu Excel: " +
                $"{ShortenError(ex.Message)}";
        }
        finally
        {
            IsBusy = false;

            NotifyResultsChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelAnalysis()
    {
        if (_cts is null ||
            _cts.IsCancellationRequested)
        {
            return;
        }

        StatusMessage =
            "Anulowanie analizy...";

        _cts.Cancel();

        CancelAnalysisCommand
            .NotifyCanExecuteChanged();
    }

    private bool TryReadParameters(
        out decimal minimumOdd,
        out string error)
    {
        minimumOdd = 0m;
        error = string.Empty;

        var parsed =
            decimal.TryParse(
                MinimumOddText.Replace(',', '.'),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out minimumOdd);

        if (!parsed ||
            minimumOdd <= 1m)
        {
            error =
                "Niepoprawny minimalny kurs. " +
                "Podaj liczbę większą od 1, np. 1.40.";

            return false;
        }

        if (HistoryCount <= 0)
        {
            error =
                "Liczba meczów historii musi być większa od 0.";

            return false;
        }

        if (MinimumGoalMatches <= 0)
        {
            error =
                "Próg BTTS musi być większy od 0.";

            return false;
        }

        return true;
    }

    private bool CanStart()
    {
        return !IsBusy;
    }

    private bool CanCancel()
    {
        return IsBusy &&
               _cts is not null &&
               !_cts.IsCancellationRequested;
    }

    private bool CanExportToExcel()
    {
        return !IsBusy &&
               Results.Count > 0;
    }

    private void NotifyResultsChanged()
    {
        ExportToExcelCommand
            .NotifyCanExecuteChanged();
    }

    private static string ShortenError(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Brak szczegółów błędu.";
        }

        value =
            value
                .Replace(
                    Environment.NewLine,
                    " ")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

        const int maxLength = 350;

        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }

    partial void OnIsBusyChanged(
        bool value)
    {
        StartAnalysisCommand
            .NotifyCanExecuteChanged();

        CancelAnalysisCommand
            .NotifyCanExecuteChanged();

        ExportToExcelCommand
            .NotifyCanExecuteChanged();
    }
}
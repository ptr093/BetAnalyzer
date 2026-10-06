using System.Windows;
using BetAnalyzer.Core.Services;
using BetAnalyzer.Infrastructure.Browser;
using BetAnalyzer.Infrastructure.Flashscore;
using BetAnalyzer.Infrastructure.Superbet;
using BetAnalyzer.Presentation.Services;
using BetAnalyzer.Presentation.ViewModels;

namespace BetAnalyzer;

public partial class App : Application
{
    private PlaywrightBrowserSession? _browserSession;

    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        _browserSession =
            new PlaywrightBrowserSession(
                new BrowserOptions
                {
                    Headless = false,
                    Locale = "pl-PL",
                    TimeZoneId = "Europe/Warsaw",
                    ActionTimeoutMilliseconds = 8000,
                    NavigationTimeoutMilliseconds = 30000
                });

        var throttler =
            new BrowserThrottler(
                TimeSpan.FromMilliseconds(1500));

        var odds =
            new SuperbetOddsProvider(
                _browserSession,
                throttler);

        var stats =
            new FlashscoreStatsProvider(
                _browserSession,
                throttler);

        var analyzer =
            new MatchAnalyzerService(
                odds,
                stats);

        var excelExportService =
            new ExcelExportService();

        var window =
            new MainWindow
            {
                DataContext =
                    new MainViewModel(
                        analyzer,
                        excelExportService)
            };

        window.Show();
    }

    protected override async void OnExit(
        ExitEventArgs e)
    {
        if (_browserSession is not null)
        {
            await _browserSession.DisposeAsync();
        }

        base.OnExit(e);
    }
}
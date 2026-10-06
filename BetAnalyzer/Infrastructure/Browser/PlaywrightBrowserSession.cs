using BetAnalyzer.Core.Contracts;
using Microsoft.Playwright;
namespace BetAnalyzer.Infrastructure.Browser;
public sealed class PlaywrightBrowserSession : IBrowserSession
{
    private readonly BrowserOptions _options;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    public PlaywrightBrowserSession(BrowserOptions options) => _options = options;
    public async Task<IPage> CreatePageAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _playwright ??= await Playwright.CreateAsync();
        _browser ??= await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _options.Headless,
            Args = ["--disable-blink-features=AutomationControlled", "--disable-notifications", "--lang=pl-PL"]
        });
        _context ??= await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = _options.Locale, TimezoneId = _options.TimeZoneId, UserAgent = _options.UserAgent,
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 }
        });
        _context.SetDefaultTimeout(_options.ActionTimeoutMilliseconds);
        _context.SetDefaultNavigationTimeout(_options.NavigationTimeoutMilliseconds);
        return await _context.NewPageAsync();
    }
    public async ValueTask DisposeAsync()
    {
        if (_context is not null) await _context.CloseAsync();
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }
}

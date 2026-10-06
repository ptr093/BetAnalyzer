namespace BetAnalyzer.Infrastructure.Browser;
public sealed class BrowserOptions
{
    public bool Headless { get; init; }
    public string Locale { get; init; } = "pl-PL";
    public string TimeZoneId { get; init; } = "Europe/Warsaw";
    public int NavigationTimeoutMilliseconds { get; init; } = 30000;
    public int ActionTimeoutMilliseconds { get; init; } = 10000;
    public string UserAgent { get; init; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/128.0.0.0 Safari/537.36";
}

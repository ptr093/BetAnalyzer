using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BetAnalyzer.Core.Contracts;
using BetAnalyzer.Core.Models;
using BetAnalyzer.Infrastructure.Browser;
using Microsoft.Playwright;
using Match = System.Text.RegularExpressions.Match;

namespace BetAnalyzer.Infrastructure.Flashscore;

public sealed class FlashscoreStatsProvider : IStatsProvider
{
    private readonly IBrowserSession _browserSession;
    private readonly BrowserThrottler _throttler;

    private readonly Dictionary<string, TeamStats> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public FlashscoreStatsProvider(
        IBrowserSession browserSession,
        BrowserThrottler throttler)
    {
        _browserSession = browserSession;
        _throttler = throttler;
    }

    public async Task<TeamStats?> GetLastMatchesAsync(
        string teamId,
        string teamName,
        int count,
        HistoryScope scope,
        CancellationToken cancellationToken = default)
    {
        var cacheKey =
            $"{teamId}|{teamName}|{count}|{scope}";

        if (_cache.TryGetValue(
                cacheKey,
                out var cached))
        {
            return cached;
        }

        return await _throttler.ExecuteAsync(
            async () =>
            {
                var page =
                    await _browserSession.CreatePageAsync(
                        cancellationToken);

                try
                {
                    await OpenHomeAsync(
                        page,
                        cancellationToken);

                    await AcceptCookiesAsync(
                        page,
                        cancellationToken);

                    var teamUrl =
                        await SearchTeamAsync(
                            page,
                            teamName,
                            cancellationToken);

                    await OpenResultsAsync(
                        page,
                        teamUrl,
                        cancellationToken);

                    var links =
                        await ReadRecentMatchLinksAsync(
                            page,
                            teamName,
                            count,
                            scope,
                            cancellationToken);

                    await LogAsync(
                        $"Flashscore {teamName}: znaleziono " +
                        $"{links.Count} meczów.");

                    var history =
                        new List<HistoricalMatch>();

                    foreach (var link in links)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var item =
                                await ReadMatchStatisticsAsync(
                                    page,
                                    link,
                                    teamId,
                                    cancellationToken);

                            if (item is not null)
                            {
                                history.Add(item);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (TimeoutException ex)
                        {
                            await LogAsync(
                                $"Flashscore: timeout meczu; " +
                                $"team='{teamName}'; " +
                                $"mecz='{link.HomeTeamName} - " +
                                $"{link.AwayTeamName}'; " +
                                $"error='{Shorten(ex.Message, 500)}'");

                            continue;
                        }
                        catch (PlaywrightException ex)
                        {
                            await LogAsync(
                                $"Flashscore: b³¹d Playwright meczu; " +
                                $"team='{teamName}'; " +
                                $"mecz='{link.HomeTeamName} - " +
                                $"{link.AwayTeamName}'; " +
                                $"error='{Shorten(ex.Message, 500)}'");

                            continue;
                        }
                        catch (Exception ex)
                        {
                            await LogAsync(
                                $"Flashscore: b³¹d meczu; " +
                                $"team='{teamName}'; " +
                                $"mecz='{link.HomeTeamName} - " +
                                $"{link.AwayTeamName}'; " +
                                $"error='{Shorten(ex.Message, 500)}'");

                            continue;
                        }
                    }

                    var result =
                        new TeamStats
                        {
                            TeamId = teamId,
                            TeamName = teamName,
                            Matches = history
                        };

                    _cache[cacheKey] =
                        result;

                    await LogAsync(
                        $"Flashscore {teamName}: zapisano " +
                        $"{history.Count} meczów historii.");

                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (TimeoutException ex)
                {
                    await LogAsync(
                        $"Flashscore: timeout dru¿yny; " +
                        $"team='{teamName}'; " +
                        $"error='{Shorten(ex.Message, 600)}'");

                    await SaveDiagnosticsSafeAsync(
                        page,
                        $"flashscore-timeout-" +
                        $"{SanitizeFileName(teamName)}");

                    return new TeamStats
                    {
                        TeamId = teamId,
                        TeamName = teamName,
                        Matches = Array.Empty<HistoricalMatch>()
                    };
                }
                catch (PlaywrightException ex)
                {
                    await LogAsync(
                        $"Flashscore: b³¹d Playwright dru¿yny; " +
                        $"team='{teamName}'; " +
                        $"error='{Shorten(ex.Message, 600)}'");

                    await SaveDiagnosticsSafeAsync(
                        page,
                        $"flashscore-playwright-" +
                        $"{SanitizeFileName(teamName)}");

                    return new TeamStats
                    {
                        TeamId = teamId,
                        TeamName = teamName,
                        Matches = Array.Empty<HistoricalMatch>()
                    };
                }
                catch (Exception ex)
                {
                    await LogAsync(
                        $"Flashscore: nieoczekiwany b³¹d dru¿yny; " +
                        $"team='{teamName}'; " +
                        $"error='{Shorten(ex.Message, 600)}'");

                    await SaveDiagnosticsSafeAsync(
                        page,
                        $"flashscore-" +
                        $"{SanitizeFileName(teamName)}");

                    return new TeamStats
                    {
                        TeamId = teamId,
                        TeamName = teamName,
                        Matches = Array.Empty<HistoricalMatch>()
                    };
                }
                finally
                {
                    await page.CloseAsync();
                }
            },
            cancellationToken);
    }

    private static async Task OpenHomeAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await page.GotoAsync(
            "https://www.flashscore.pl/",
            new PageGotoOptions
            {
                WaitUntil =
                    WaitUntilState.DOMContentLoaded
            });

        await page.WaitForTimeoutAsync(800);
    }

    private static async Task<string> SearchTeamAsync(
        IPage page,
        string teamName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var icon =
            page.Locator(
                "svg[data-testid='wcl-icon-action-icon-search']")
            .First;

        if (await icon.CountAsync() > 0 &&
            await icon.IsVisibleAsync())
        {
            await icon.ClickAsync(
                new LocatorClickOptions
                {
                    Force = true,
                    Timeout = 5000
                });
        }
        else
        {
            var fallback =
                page.Locator(
                    "[data-testid*='search'], " +
                    "button[aria-label*='Szukaj'], " +
                    "button[title*='Szukaj']")
                .First;

            if (await fallback.CountAsync() == 0)
            {
                throw new InvalidOperationException(
                    "Nie znaleziono wyszukiwarki Flashscore.");
            }

            await fallback.ClickAsync(
                new LocatorClickOptions
                {
                    Force = true,
                    Timeout = 5000
                });
        }

        var input =
            page.Locator(
                "input[placeholder='Wpisz wyszukiwany tekst'], " +
                "input[placeholder*='wyszukiwany tekst'], " +
                "input[type='search']")
            .First;

        await input.WaitForAsync(
            new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 5000
            });

        await input.FillAsync(teamName);

        await page.WaitForTimeoutAsync(1200);

        var result =
            page.Locator(
                "a.searchResult, " +
                "[data-testid*='search-result'], " +
                ".searchResult__participant")
            .Filter(
                new LocatorFilterOptions
                {
                    HasText = teamName
                })
            .First;

        if (await result.CountAsync() == 0)
        {
            result =
                page.Locator(
                    "a.searchResult, " +
                    "[data-testid*='search-result'], " +
                    ".searchResult__participant")
                .First;
        }

        await result.WaitForAsync(
            new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 8000
            });

        var href =
            await result.GetAttributeAsync("href");

        await result.ClickAsync(
            new LocatorClickOptions
            {
                Force = true,
                Timeout = 5000
            });

        await page.WaitForLoadStateAsync(
            LoadState.DOMContentLoaded);

        await page.WaitForTimeoutAsync(1000);

        var teamUrl =
            !string.IsNullOrWhiteSpace(href)
                ? ToAbsoluteUrl(href)
                : page.Url;

        if (!teamUrl.Contains(
                "/druzyna/",
                StringComparison.OrdinalIgnoreCase))
        {
            teamUrl =
                NormalizeTeamUrl(page.Url);
        }

        return NormalizeTeamUrl(teamUrl);
    }

    private static string NormalizeTeamUrl(
        string url)
    {
        var uri =
            new Uri(url);

        var path =
            uri.AbsolutePath.TrimEnd('/');

        var index =
            path.IndexOf(
                "/druzyna/",
                StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Nie rozpoznano URL dru¿yny: {url}");
        }

        var resultsIndex =
            path.IndexOf(
                "/wyniki",
                StringComparison.OrdinalIgnoreCase);

        if (resultsIndex >= 0)
        {
            path = path[..resultsIndex];
        }

        return new UriBuilder(
            uri.Scheme,
            uri.Host)
        {
            Path = path + "/"
        }
        .Uri
        .ToString();
    }

    private static async Task OpenResultsAsync(
        IPage page,
        string teamUrl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var url =
            teamUrl.TrimEnd('/') +
            "/wyniki/";

        await LogAsync(
            $"Flashscore: otwieram Wyniki: {url}");

        await page.GotoAsync(
            url,
            new PageGotoOptions
            {
                WaitUntil =
                    WaitUntilState.DOMContentLoaded
            });

        await page.WaitForTimeoutAsync(1200);

        await page.WaitForSelectorAsync(
            ".event__match",
            new PageWaitForSelectorOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 10000
            });
    }

    private static async Task<
        IReadOnlyList<FlashscoreMatchLink>>
        ReadRecentMatchLinksAsync(
        IPage page,
        string teamName,
        int count,
        HistoryScope scope,
        CancellationToken cancellationToken)
    {
        await LoadMoreAsync(
            page,
            count,
            cancellationToken);

        var rows =
            page.Locator(".event__match");

        var result =
            new List<FlashscoreMatchLink>();

        var rowCount =
            await rows.CountAsync();

        for (var i = 0;
             i < rowCount && result.Count < count;
             i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row =
                rows.Nth(i);

            try
            {
                if (!await row.IsVisibleAsync())
                {
                    continue;
                }

                var text =
                    await row.InnerTextAsync();

                if (i < 15)
                {
                    await LogAsync(
                        $"Flashscore wiersz {i}: " +
                        $"{Shorten(text, 180)}");
                }

                var home =
                    await TeamTextAsync(
                        row,
                        ".event__homeParticipant");

                var away =
                    await TeamTextAsync(
                        row,
                        ".event__awayParticipant");

                if (string.IsNullOrWhiteSpace(home) ||
                    string.IsNullOrWhiteSpace(away))
                {
                    continue;
                }

                var isHome =
                    TeamMatches(home, teamName);

                var isAway =
                    TeamMatches(away, teamName);

                if (!MatchesScope(
                        isHome,
                        isAway,
                        scope))
                {
                    continue;
                }

                var link =
                    row.Locator(
                        "a.eventRowLink, a[href*='/mecz/']")
                    .First;

                if (await link.CountAsync() == 0)
                {
                    continue;
                }

                var href =
                    await link.GetAttributeAsync("href");

                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                result.Add(
                    new FlashscoreMatchLink
                    {
                        Url = ToAbsoluteUrl(href),
                        HomeTeamName = home,
                        AwayTeamName = away,
                        IsHome = isHome
                    });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await LogAsync(
                    $"Flashscore: b³¹d wiersza wyników; " +
                    $"team='{teamName}'; " +
                    $"index={i}; " +
                    $"error='{Shorten(ex.Message, 500)}'");
            }
        }

        return result;
    }

    private static async Task<string> TeamTextAsync(
        ILocator row,
        string selector)
    {
        var element =
            row.Locator(selector).First;

        if (await element.CountAsync() == 0 ||
            !await element.IsVisibleAsync())
        {
            return string.Empty;
        }

        return (await element.InnerTextAsync()).Trim();
    }

    private static async Task LoadMoreAsync(
        IPage page,
        int requiredCount,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < 5; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rows =
                page.Locator(".event__match");

            var beforeCount =
                await rows.CountAsync();

            if (beforeCount >= requiredCount)
            {
                return;
            }

            var more =
                page.Locator(
                    "a.event__more, " +
                    "button:has-text('Poka¿ wiêcej'), " +
                    "a:has-text('Poka¿ wiêcej')")
                .First;

            if (await more.CountAsync() == 0 ||
                !await more.IsVisibleAsync())
            {
                await LogAsync(
                    $"Flashscore: brak widocznego przycisku " +
                    $"Poka¿ wiêcej; rows={beforeCount}");

                return;
            }

            var visible =
                await TryScrollIntoViewAsync(
                    more,
                    $"load-more:{i}");

            if (!visible)
            {
                await LogAsync(
                    $"Flashscore: pominiêto niewidoczny przycisk " +
                    $"Poka¿ wiêcej; rows={beforeCount}");

                return;
            }

            try
            {
                await more.ClickAsync(
                    new LocatorClickOptions
                    {
                        Force = true,
                        Timeout = 3000
                    });

                await page.WaitForTimeoutAsync(700);

                var afterCount =
                    await rows.CountAsync();

                await LogAsync(
                    $"Flashscore: Poka¿ wiêcej; " +
                    $"rows={beforeCount}->{afterCount}");

                if (afterCount <= beforeCount)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException ex)
            {
                await LogAsync(
                    $"Flashscore: timeout Poka¿ wiêcej; " +
                    $"rows={beforeCount}; " +
                    $"error='{Shorten(ex.Message, 500)}'");

                return;
            }
            catch (PlaywrightException ex)
            {
                await LogAsync(
                    $"Flashscore: b³¹d Poka¿ wiêcej; " +
                    $"rows={beforeCount}; " +
                    $"error='{Shorten(ex.Message, 500)}'");

                return;
            }
        }
    }

    private static async Task<HistoricalMatch?>
        ReadMatchStatisticsAsync(
        IPage page,
        FlashscoreMatchLink matchLink,
        string teamId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var statsUrl =
            BuildStatisticsUrl(matchLink.Url);

        await LogAsync(
            $"Flashscore: otwieram statystyki: " +
            $"{statsUrl}");

        await page.GotoAsync(
            statsUrl,
            new PageGotoOptions
            {
                WaitUntil =
                    WaitUntilState.DOMContentLoaded
            });

        await page.WaitForTimeoutAsync(1000);

        var statisticsAvailable =
            await WaitForStatisticsAsync(
                page,
                cancellationToken);

        var score =
            await ReadScoreAsync(page);

        var statistics =
            statisticsAvailable
                ? await ReadStatisticsAsync(
                    page,
                    cancellationToken)
                : new FlashscoreStatistics();

        var isHome =
            matchLink.IsHome;

        return new HistoricalMatch
        {
            TeamId = teamId,

            OpponentName = isHome
                ? matchLink.AwayTeamName
                : matchLink.HomeTeamName,

            IsHome = isHome,

            GoalsFor = isHome
                ? score.HomeGoals
                : score.AwayGoals,

            GoalsAgainst = isHome
                ? score.AwayGoals
                : score.HomeGoals,

            CornersFor = isHome
                ? statistics.HomeCorners
                : statistics.AwayCorners,

            CornersAgainst = isHome
                ? statistics.AwayCorners
                : statistics.HomeCorners,

            ShotsFor = isHome
                ? statistics.HomeShots
                : statistics.AwayShots,

            ShotsAgainst = isHome
                ? statistics.AwayShots
                : statistics.HomeShots,

            ShotsOnTargetFor = isHome
                ? statistics.HomeShotsOnTarget
                : statistics.AwayShotsOnTarget,

            ShotsOnTargetAgainst = isHome
                ? statistics.AwayShotsOnTarget
                : statistics.HomeShotsOnTarget,

            ShotsOffTargetFor = isHome
                ? statistics.HomeShotsOffTarget
                : statistics.AwayShotsOffTarget,

            ShotsOffTargetAgainst = isHome
                ? statistics.AwayShotsOffTarget
                : statistics.HomeShotsOffTarget,

            FoulsFor = isHome
                ? statistics.HomeFouls
                : statistics.AwayFouls,

            FoulsAgainst = isHome
                ? statistics.AwayFouls
                : statistics.HomeFouls,

            OffsidesFor = isHome
                ? statistics.HomeOffsides
                : statistics.AwayOffsides,

            OffsidesAgainst = isHome
                ? statistics.AwayOffsides
                : statistics.HomeOffsides,

            YellowCardsFor = isHome
                ? statistics.HomeYellowCards
                : statistics.AwayYellowCards,

            YellowCardsAgainst = isHome
                ? statistics.AwayYellowCards
                : statistics.HomeYellowCards,

            RedCardsFor = isHome
                ? statistics.HomeRedCards
                : statistics.AwayRedCards,

            RedCardsAgainst = isHome
                ? statistics.AwayRedCards
                : statistics.HomeRedCards,

            FreeKicksFor = isHome
                ? statistics.HomeFreeKicks
                : statistics.AwayFreeKicks,

            FreeKicksAgainst = isHome
                ? statistics.AwayFreeKicks
                : statistics.HomeFreeKicks,

            PossessionFor = isHome
                ? statistics.HomePossession
                : statistics.AwayPossession,

            PossessionAgainst = isHome
                ? statistics.AwayPossession
                : statistics.HomePossession,

            PlayedAt = null
        };
    }

    private static string BuildStatisticsUrl(
        string matchUrl)
    {
        var uri =
            new Uri(matchUrl);

        var path =
            uri.AbsolutePath.TrimEnd('/');

        if (!path.EndsWith(
                "/szczegoly/statystyki",
                StringComparison.OrdinalIgnoreCase))
        {
            path += "/szczegoly/statystyki";
        }

        return new UriBuilder(
            uri.Scheme,
            uri.Host)
        {
            Path = path + "/"
        }
        .Uri
        .ToString();
    }

    private static async Task<bool>
        WaitForStatisticsAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        if (await IsPreMatchStatisticsMessageVisibleAsync(page))
        {
            await LogAsync(
                "Flashscore: wykryto komunikat o statystykach " +
                "dostêpnych po rozpoczêciu meczu.");

            var openedPreviousMatch =
                await ClickPreviousMatchDetailsAsync(
                    page,
                    cancellationToken);

            if (openedPreviousMatch)
            {
                await page.WaitForTimeoutAsync(700);
            }
        }

        var selectors =
            new[]
            {
                "[data-testid='wcl-statistics']",
                "text=Rzuty ro¿ne",
                "text=Strza³y na bramkê",
                "text=Corner kicks",
                "text=Shots on target"
            };

        foreach (var selector in selectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await page.WaitForSelectorAsync(
                    selector,
                    new PageWaitForSelectorOptions
                    {
                        State = WaitForSelectorState.Visible,
                        Timeout = 2500
                    });

                return true;
            }
            catch (PlaywrightException)
            {
                // SprawdŸ kolejny selector.
            }
        }

        await LogAsync(
            "Flashscore: po próbie otwarcia poprzedniego meczu " +
            "nie znaleziono widocznej tabeli statystyk.");

        await SaveDiagnosticsSafeAsync(
            page,
            "flashscore-statistics-not-found");

        return false;
    }

    private static async Task<bool>
        IsPreMatchStatisticsMessageVisibleAsync(
        IPage page)
    {
        try
        {
            var body =
                await page.Locator("body")
                    .InnerTextAsync();

            return body.Contains(
                "Statystyki bêd¹ dostêpne po rozpoczêciu meczu",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private static async Task<bool>
        ClickPreviousMatchDetailsAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var button =
            page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "Poka¿ szczegó³y meczu",
                    Exact = false
                })
            .First;

        if (await button.CountAsync() == 0 ||
            !await button.IsVisibleAsync())
        {
            button =
                page.GetByText(
                    "POKA¯ SZCZEGÓ£Y MECZU",
                    new PageGetByTextOptions
                    {
                        Exact = false
                    })
                .First;
        }

        if (await button.CountAsync() == 0 ||
            !await button.IsVisibleAsync())
        {
            // Dodatkowy selektor oparty na tekœcie przycisku.
            button =
                page.Locator(
                    "button, a, [role='button']")
                .Filter(
                    new LocatorFilterOptions
                    {
                        HasText = "POKA¯ SZCZEGÓ£Y MECZU"
                    })
                .First;
        }

        if (await button.CountAsync() == 0 ||
            !await button.IsVisibleAsync())
        {
            await LogAsync(
                "Flashscore: komunikat przedmeczowy jest widoczny, " +
                "ale nie znaleziono przycisku " +
                "'Poka¿ szczegó³y meczu'.");

            return false;
        }

        var visible =
            await TryScrollIntoViewAsync(
                button,
                "previous-match-details-button");

        if (!visible)
        {
            await LogAsync(
                "Flashscore: przycisk poprzedniego meczu " +
                "jest niewidoczny albo nie da siê go przewin¹æ.");

            return false;
        }

        try
        {
            await button.ClickAsync(
                new LocatorClickOptions
                {
                    Force = true,
                    Timeout = 3000
                });

            await page.WaitForTimeoutAsync(700);

            await LogAsync(
                "Flashscore: klikniêto " +
                "'Poka¿ szczegó³y meczu'.");

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PlaywrightException ex)
        {
            await LogAsync(
                $"Flashscore: b³¹d klikniêcia " +
                $"'Poka¿ szczegó³y meczu'; " +
                $"error='{Shorten(ex.Message, 500)}'");

            return false;
        }
    }

    private static async Task<(int HomeGoals, int AwayGoals)>
        ReadScoreAsync(
        IPage page)
    {
        var score =
            page.Locator(
                "div.duelParticipant__score " +
                "div.detailScore__wrapper, " +
                ".detailScore__wrapper, " +
                "[data-testid*='score']")
            .First;

        if (await score.CountAsync() == 0 ||
            !await score.IsVisibleAsync())
        {
            return (0, 0);
        }

        try
        {
            var spans =
                score.Locator(
                    "span:not(.detailScore__divider)");

            if (await spans.CountAsync() >= 2)
            {
                return
                (
                    ParseInt(
                        await spans.Nth(0)
                            .InnerTextAsync()),

                    ParseInt(
                        await spans.Nth(1)
                            .InnerTextAsync())
                );
            }
        }
        catch (PlaywrightException ex)
        {
            await LogAsync(
                $"Flashscore: b³¹d odczytu wyniku; " +
                $"error='{Shorten(ex.Message, 400)}'");
        }

        return (0, 0);
    }

    private static async Task<FlashscoreStatistics>
        ReadStatisticsAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        var result =
            new FlashscoreStatistics
            {
                HomeCorners =
                    await GetStatValueAsync(
                        page,
                        ["Rzuty ro¿ne", "Corner kicks"],
                        0,
                        cancellationToken),

                AwayCorners =
                    await GetStatValueAsync(
                        page,
                        ["Rzuty ro¿ne", "Corner kicks"],
                        1,
                        cancellationToken),

                HomeShots =
                    await GetStatValueAsync(
                        page,
                        ["Strza³y ³¹cznie", "Total shots"],
                        0,
                        cancellationToken),

                AwayShots =
                    await GetStatValueAsync(
                        page,
                        ["Strza³y ³¹cznie", "Total shots"],
                        1,
                        cancellationToken),

                HomeShotsOnTarget =
                    await GetStatValueAsync(
                        page,
                        [
                            "Strza³y na bramkê",
                            "Strza³y celne",
                            "Shots on target"
                        ],
                        0,
                        cancellationToken),

                AwayShotsOnTarget =
                    await GetStatValueAsync(
                        page,
                        [
                            "Strza³y na bramkê",
                            "Strza³y celne",
                            "Shots on target"
                        ],
                        1,
                        cancellationToken),

                HomeShotsOffTarget =
                    await GetStatValueAsync(
                        page,
                        [
                            "Strza³y niecelne",
                            "Shots off target"
                        ],
                        0,
                        cancellationToken),

                AwayShotsOffTarget =
                    await GetStatValueAsync(
                        page,
                        [
                            "Strza³y niecelne",
                            "Shots off target"
                        ],
                        1,
                        cancellationToken),

                HomeFouls =
                    await GetStatValueAsync(
                        page,
                        ["Faule", "Fouls"],
                        0,
                        cancellationToken),

                AwayFouls =
                    await GetStatValueAsync(
                        page,
                        ["Faule", "Fouls"],
                        1,
                        cancellationToken),

                HomeOffsides =
                    await GetStatValueAsync(
                        page,
                        ["Spalone", "Offsides"],
                        0,
                        cancellationToken),

                AwayOffsides =
                    await GetStatValueAsync(
                        page,
                        ["Spalone", "Offsides"],
                        1,
                        cancellationToken),

                HomeYellowCards =
                    await GetStatValueAsync(
                        page,
                        ["¯ó³te kartki", "Yellow cards"],
                        0,
                        cancellationToken),

                AwayYellowCards =
                    await GetStatValueAsync(
                        page,
                        ["¯ó³te kartki", "Yellow cards"],
                        1,
                        cancellationToken),

                HomeRedCards =
                    await GetStatValueAsync(
                        page,
                        ["Czerwone kartki", "Red cards"],
                        0,
                        cancellationToken),

                AwayRedCards =
                    await GetStatValueAsync(
                        page,
                        ["Czerwone kartki", "Red cards"],
                        1,
                        cancellationToken),

                HomeFreeKicks =
                    await GetStatValueAsync(
                        page,
                        ["Rzuty wolne", "Free kicks"],
                        0,
                        cancellationToken),

                AwayFreeKicks =
                    await GetStatValueAsync(
                        page,
                        ["Rzuty wolne", "Free kicks"],
                        1,
                        cancellationToken),

                HomePossession =
                    await GetStatValueAsync(
                        page,
                        ["Posiadanie pi³ki", "Ball possession"],
                        0,
                        cancellationToken),

                AwayPossession =
                    await GetStatValueAsync(
                        page,
                        ["Posiadanie pi³ki", "Ball possession"],
                        1,
                        cancellationToken)
            };

        await LogAsync(
            $"Flashscore: " +
            $"ro¿ne={FormatPair(result.HomeCorners, result.AwayCorners)}; " +
            $"strza³y={FormatPair(result.HomeShots, result.AwayShots)}; " +
            $"celne={FormatPair(result.HomeShotsOnTarget, result.AwayShotsOnTarget)}; " +
            $"niecelne={FormatPair(result.HomeShotsOffTarget, result.AwayShotsOffTarget)}; " +
            $"faule={FormatPair(result.HomeFouls, result.AwayFouls)}; " +
            $"spalone={FormatPair(result.HomeOffsides, result.AwayOffsides)}; " +
            $"kartki={FormatPair(result.HomeYellowCards, result.AwayYellowCards)}");

        return result;
    }

    private static string FormatPair(
        int? home,
        int? away)
    {
        return home.HasValue &&
               away.HasValue
            ? $"{home.Value}-{away.Value}"
            : "brak danych";
    }

    private static async Task<int?>
        GetStatValueAsync(
        IPage page,
        IReadOnlyList<string> keywords,
        int index,
        CancellationToken cancellationToken)
    {
        var selectors =
            new[]
            {
                "[data-testid='wcl-statistics']",
                "[class*='statRow']",
                "[class*='stat-row']",
                "[class*='statistics']"
            };

        foreach (var keyword in keywords)
        {
            foreach (var selector in selectors)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var candidates =
                        page.Locator(selector)
                            .Filter(
                                new LocatorFilterOptions
                                {
                                    HasText = keyword
                                });

                    var count =
                        await candidates.CountAsync();

                    for (var i = 0; i < count; i++)
                    {
                        var candidate =
                            candidates.Nth(i);

                        if (!await candidate.IsVisibleAsync())
                        {
                            continue;
                        }

                        var text =
                            await candidate.InnerTextAsync();

                        var values =
                            ExtractStatValues(text);

                        if (values.Count <= index)
                        {
                            continue;
                        }

                        return values[index];
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (PlaywrightException ex)
                {
                    await LogAsync(
                        $"Flashscore: b³¹d odczytu statystyki; " +
                        $"keyword='{keyword}'; " +
                        $"selector='{selector}'; " +
                        $"error='{Shorten(ex.Message, 350)}'");
                }
            }
        }

        await LogAsync(
            $"Brak statystyki: " +
            $"{string.Join(", ", keywords)}");

        return null;
    }

    private static List<int>
        ExtractStatValues(
        string text)
    {
        var lineValues =
            text
                .Split(
                    new[]
                    {
                        '\r',
                        '\n'
                    },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(
                    x => Regex.IsMatch(
                        x,
                        @"^\d{1,3}$"))
                .Select(
                    x => int.Parse(
                        x,
                        CultureInfo.InvariantCulture))
                .ToList();

        if (lineValues.Count >= 2)
        {
            return lineValues
                .Take(2)
                .ToList();
        }

        return Regex
            .Matches(
                text,
                @"(?<![\d.])\d{1,3}(?![\d.])")
            .Cast<Match>()
            .Select(
                x => int.Parse(
                    x.Value,
                    CultureInfo.InvariantCulture))
            .Take(2)
            .ToList();
    }

    private static async Task<bool>
        TryScrollIntoViewAsync(
        ILocator locator,
        string context)
    {
        try
        {
            if (await locator.CountAsync() == 0)
            {
                await LogAsync(
                    $"Flashscore: brak elementu przed scrollowaniem; " +
                    $"context='{context}'");

                return false;
            }

            if (!await locator.IsVisibleAsync())
            {
                await LogAsync(
                    $"Flashscore: element niewidoczny przed scrollowaniem; " +
                    $"context='{context}'");

                return false;
            }

            var box =
                await locator.BoundingBoxAsync();

            if (box is null ||
                box.Width <= 0 ||
                box.Height <= 0)
            {
                await LogAsync(
                    $"Flashscore: element bez bounding box; " +
                    $"context='{context}'");

                return false;
            }

            await locator.ScrollIntoViewIfNeededAsync(
                new LocatorScrollIntoViewIfNeededOptions
                {
                    Timeout = 1500
                });

            return true;
        }
        catch (TimeoutException ex)
        {
            await LogAsync(
                $"Flashscore: timeout scrollowania; " +
                $"context='{context}'; " +
                $"error='{Shorten(ex.Message, 500)}'");

            return false;
        }
        catch (PlaywrightException ex)
        {
            await LogAsync(
                $"Flashscore: b³¹d scrollowania; " +
                $"context='{context}'; " +
                $"error='{Shorten(ex.Message, 500)}'");

            return false;
        }
    }

    private static bool TeamMatches(
        string value,
        string teamName)
    {
        return value.Contains(
                   teamName,
                   StringComparison.OrdinalIgnoreCase) ||
               teamName.Contains(
                   value,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesScope(
        bool home,
        bool away,
        HistoryScope scope)
    {
        return scope switch
        {
            HistoryScope.HomeOnly =>
                home,

            HistoryScope.AwayOnly =>
                away,

            _ =>
                home || away
        };
    }

    private static int ParseInt(
        string value)
    {
        var match =
            Regex.Match(
                value,
                @"-?\d+");

        return match.Success
            ? int.Parse(
                match.Value,
                CultureInfo.InvariantCulture)
            : 0;
    }

    private static string ToAbsoluteUrl(
        string href)
    {
        return href.StartsWith(
                "http",
                StringComparison.OrdinalIgnoreCase)
            ? href
            : new Uri(
                new Uri("https://www.flashscore.pl"),
                href).ToString();
    }

    private static async Task AcceptCookiesAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var button =
            page.Locator(
                "#onetrust-accept-btn-handler")
            .First;

        if (await button.CountAsync() == 0)
        {
            return;
        }

        try
        {
            await button.ClickAsync(
                new LocatorClickOptions
                {
                    Timeout = 2500
                });
        }
        catch (PlaywrightException)
        {
            // Baner móg³ zostaæ wczeœniej zamkniêty.
        }
    }

    private static async Task SaveDiagnosticsSafeAsync(
        IPage page,
        string name)
    {
        try
        {
            await SaveDiagnosticsAsync(
                page,
                name);
        }
        catch (Exception ex)
        {
            await LogAsync(
                $"Flashscore: b³¹d zapisu diagnostyki; " +
                $"name='{name}'; " +
                $"error='{Shorten(ex.Message, 400)}'");
        }
    }

    private static async Task SaveDiagnosticsAsync(
        IPage page,
        string name)
    {
        Directory.CreateDirectory(
            "scraper-diagnostics");

        var safe =
            SanitizeFileName(name);

        await page.ScreenshotAsync(
            new PageScreenshotOptions
            {
                Path =
                    $"scraper-diagnostics/" +
                    $"{safe}.png",
                FullPage = true
            });

        await File.WriteAllTextAsync(
            $"scraper-diagnostics/" +
            $"{safe}.html",
            await page.ContentAsync());
    }

    private static async Task LogAsync(
        string message)
    {
        Directory.CreateDirectory(
            "scraper-diagnostics");

        await File.AppendAllTextAsync(
            "scraper-diagnostics/flashscore.log",
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} " +
            $"{message}{Environment.NewLine}");
    }

    private static string Shorten(
        string? value,
        int maxLength)
    {
        value ??= string.Empty;

        value =
            Regex.Replace(
                value,
                @"\s+",
                " ").Trim();

        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }

    private static string SanitizeFileName(
        string value)
    {
        foreach (var invalid in
                 Path.GetInvalidFileNameChars())
        {
            value =
                value.Replace(
                    invalid,
                    '_');
        }

        return value;
    }

    private sealed class FlashscoreMatchLink
    {
        public required string Url { get; init; }

        public required string HomeTeamName { get; init; }

        public required string AwayTeamName { get; init; }

        public bool IsHome { get; init; }
    }

    private sealed class FlashscoreStatistics
    {
        public int? HomeCorners { get; init; }

        public int? AwayCorners { get; init; }

        public int? HomeShots { get; init; }

        public int? AwayShots { get; init; }

        public int? HomeShotsOnTarget { get; init; }

        public int? AwayShotsOnTarget { get; init; }

        public int? HomeShotsOffTarget { get; init; }

        public int? AwayShotsOffTarget { get; init; }

        public int? HomeFouls { get; init; }

        public int? AwayFouls { get; init; }

        public int? HomeOffsides { get; init; }

        public int? AwayOffsides { get; init; }

        public int? HomeYellowCards { get; init; }

        public int? AwayYellowCards { get; init; }

        public int? HomeRedCards { get; init; }

        public int? AwayRedCards { get; init; }

        public int? HomeFreeKicks { get; init; }

        public int? AwayFreeKicks { get; init; }

        public int? HomePossession { get; init; }

        public int? AwayPossession { get; init; }
    }
}
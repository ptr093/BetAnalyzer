using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BetAnalyzer.Core.Contracts;
using BetAnalyzer.Core.Models;
using BetAnalyzer.Infrastructure.Browser;
using Microsoft.Playwright;

using BetMatch = BetAnalyzer.Core.Models.Match;
using RegexMatch = System.Text.RegularExpressions.Match;

namespace BetAnalyzer.Infrastructure.Superbet;

public sealed class SuperbetOddsProvider : IOddsProvider
{
    private readonly IBrowserSession _browserSession;
    private readonly BrowserThrottler _throttler;

    private readonly Dictionary<string, string>
        _detailsUrlCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<
        string,
        IReadOnlyList<Odd>> _oddsCache =
        new(StringComparer.OrdinalIgnoreCase);

    public SuperbetOddsProvider(
        IBrowserSession browserSession,
        BrowserThrottler throttler)
    {
        _browserSession = browserSession;
        _throttler = throttler;
    }

    public async Task<IReadOnlyList<BetMatch>>
        GetUpcomingMatchesAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        return await _throttler.ExecuteAsync(
            async () =>
            {
                var page =
                    await _browserSession.CreatePageAsync(
                        cancellationToken);

                try
                {
                    await NavigateAsync(
                        page,
                        SuperbetSelectors.FootballTodayUrl,
                        cancellationToken);

                    await AcceptCookiesAsync(
                        page,
                        cancellationToken);

                    var items =
                        await ReadListItemsProgressivelyAsync(
                            page,
                            cancellationToken);

                    await LogAsync(
                        $"Superbet: odczytano " +
                        $"{items.Count} unikalnych meczów.");

                    var result =
                        new List<BetMatch>();

                    foreach (var item in items)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var detailsUrl =
                            item.DetailsUrl;

                        if (!IsUrlForMatch(
                                detailsUrl,
                                item.HomeTeam,
                                item.AwayTeam))
                        {
                            detailsUrl =
                                await OpenFreshCardAsync(
                                    page,
                                    item.HomeTeam,
                                    item.AwayTeam,
                                    cancellationToken);
                        }

                        if (!IsUrlForMatch(
                                detailsUrl,
                                item.HomeTeam,
                                item.AwayTeam))
                        {
                            await LogAsync(
                                $"Superbet: pominięto mecz bez " +
                                $"poprawnego URL; " +
                                $"mecz='{item.HomeTeam} - " +
                                $"{item.AwayTeam}'; " +
                                $"url='{detailsUrl}'");

                            continue;
                        }

                        var match =
                            new BetMatch
                            {
                                Id = detailsUrl!,
                                HomeTeamId =
                                    NormalizeTeamId(
                                        item.HomeTeam),
                                AwayTeamId =
                                    NormalizeTeamId(
                                        item.AwayTeam),
                                HomeTeamName =
                                    item.HomeTeam,
                                AwayTeamName =
                                    item.AwayTeam
                            };

                        _detailsUrlCache[match.Id] =
                            detailsUrl!;

                        result.Add(match);

                        await LogAsync(
                            $"Superbet: mecz " +
                            $"{match.DisplayName}; " +
                            $"URL={detailsUrl}");

                        await NavigateAsync(
                            page,
                            SuperbetSelectors.FootballTodayUrl,
                            cancellationToken);

                        await AcceptCookiesAsync(
                            page,
                            cancellationToken);
                    }

                    return result
                        .GroupBy(
                            x => x.Id,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(x => x.First())
                        .ToList();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await LogAsync(
                        $"Superbet: błąd pobierania listy meczów; " +
                        $"error='{ex.Message}'");

                    await SaveDiagnosticsSafeAsync(
                        page,
                        "superbet-list");

                    throw;
                }
                finally
                {
                    await page.CloseAsync();
                }
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<Odd>>
        GetOddsAsync(
        BetMatch match,
        CancellationToken cancellationToken = default)
    {
        if (_oddsCache.TryGetValue(
                match.Id,
                out var cachedOdds))
        {
            return cachedOdds;
        }

        var detailsUrl =
            _detailsUrlCache.TryGetValue(
                match.Id,
                out var cachedUrl)
            ? cachedUrl
            : match.Id;

        if (!IsUrlForMatch(
                detailsUrl,
                match.HomeTeamName,
                match.AwayTeamName))
        {
            await LogAsync(
                $"Superbet: odrzucono URL przed pobraniem kursów; " +
                $"mecz='{match.DisplayName}'; " +
                $"url='{detailsUrl}'");

            return Array.Empty<Odd>();
        }

        return await _throttler.ExecuteAsync(
            async () =>
            {
                var page =
                    await _browserSession.CreatePageAsync(
                        cancellationToken);

                try
                {
                    await NavigateAsync(
                        page,
                        detailsUrl,
                        cancellationToken);

                    await AcceptCookiesAsync(
                        page,
                        cancellationToken);

                    await CloseBlockingOverlaysAsync(page);

                    var oddsActivated =
                        await EnsureOddsTabAsync(
                            page,
                            cancellationToken);

                    if (!oddsActivated)
                    {
                        await LogAsync(
                            $"Superbet: nie aktywowano zakładki Kursy; " +
                            $"mecz='{match.DisplayName}'");

                        return Array.Empty<Odd>();
                    }

                    var odds =
                        await ReadCategoriesAsync(
                            page,
                            match,
                            cancellationToken);

                    _oddsCache[match.Id] =
                        odds;

                    await LogAsync(
                        $"Superbet: szczegóły " +
                        $"{match.DisplayName}; " +
                        $"kursów={odds.Count}");

                    return odds;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (TimeoutException ex)
                {
                    await LogAsync(
                        $"Superbet: timeout całej strony szczegółów; " +
                        $"mecz='{match.DisplayName}'; " +
                        $"error='{ex.Message}'");

                    return Array.Empty<Odd>();
                }
                catch (PlaywrightException ex)
                {
                    await LogAsync(
                        $"Superbet: błąd Playwright strony szczegółów; " +
                        $"mecz='{match.DisplayName}'; " +
                        $"error='{ex.Message}'");

                    return Array.Empty<Odd>();
                }
                catch (Exception ex)
                {
                    await LogAsync(
                        $"Superbet: nieoczekiwany błąd strony szczegółów; " +
                        $"mecz='{match.DisplayName}'; " +
                        $"error='{ex.Message}'");

                    await SaveDiagnosticsSafeAsync(
                        page,
                        $"superbet-details-" +
                        $"{SanitizeFileName(match.Id)}");

                    return Array.Empty<Odd>();
                }
                finally
                {
                    await page.CloseAsync();
                }
            },
            cancellationToken);
    }

    private static async Task<
        IReadOnlyList<SuperbetListItem>>
        ReadListItemsProgressivelyAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        const int targetMatches = 12;

        var result =
            new Dictionary<string, SuperbetListItem>(
                StringComparer.OrdinalIgnoreCase);

        await ResetSuperbetListScrollAsync(page);

        await page.WaitForTimeoutAsync(500);

        var previousTop =
            -1L;

        var stableRounds =
            0;

        for (var round = 0; round < 80; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cards =
                await FindMatchCardsAsync(page);

            foreach (var card in cards)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var raw =
                        await ReadRawEventAsync(card);

                    if (!TryExtractTeams(
                            raw.ContainerText,
                            out var home,
                            out var away))
                    {
                        continue;
                    }

                    var key =
                        $"{NormalizeTeamId(home)}|" +
                        $"{NormalizeTeamId(away)}";

                    var url =
                        raw.DetailsUrl;

                    if (!IsUrlForMatch(
                            url,
                            home,
                            away))
                    {
                        url = null;
                    }

                    if (!result.ContainsKey(key))
                    {
                        result[key] =
                            new SuperbetListItem
                            {
                                HomeTeam = home,
                                AwayTeam = away,
                                DetailsUrl = url
                            };

                        await LogAsync(
                            $"Superbet: znaleziono mecz " +
                            $"#{result.Count}: " +
                            $"{home} - {away}");
                    }
                    else if (
                        result[key].DetailsUrl is null &&
                        url is not null)
                    {
                        result[key] =
                            new SuperbetListItem
                            {
                                HomeTeam = home,
                                AwayTeam = away,
                                DetailsUrl = url
                            };
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await LogAsync(
                        $"Superbet: błąd odczytu karty meczu; " +
                        $"error='{ex.Message}'");
                }
            }

            if (result.Count >= targetMatches)
            {
                await LogAsync(
                    $"Superbet: osiągnięto limit " +
                    $"{targetMatches} meczów.");

                break;
            }

            var state =
                await page.EvaluateAsync<ScrollState>(
                    """
                    () => ({
                        top: Math.round(window.scrollY),
                        height: document.documentElement.scrollHeight,
                        viewport: window.innerHeight
                    })
                    """);

            if (state.Top == previousTop)
            {
                stableRounds++;
            }
            else
            {
                stableRounds = 0;
            }

            previousTop =
                state.Top;

            if (stableRounds >= 3 ||
                state.Top + state.Viewport >= state.Height)
            {
                break;
            }

            await ScrollPageStepAsync(page);

            await page.WaitForTimeoutAsync(850);
        }

        await LogAsync(
            $"Superbet: końcowo zebrano " +
            $"{result.Count} unikalnych meczów.");

        return result
            .Values
            .Take(targetMatches)
            .ToList();
    }

    private static async Task<IReadOnlyList<ILocator>>
        FindMatchCardsAsync(
        IPage page)
    {
        foreach (var selector in SuperbetSelectors.EventCards)
        {
            var allCards =
                page.Locator(selector);

            var count =
                await allCards.CountAsync();

            await LogAsync(
                $"Lista selector: {selector}; elementów: {count}");

            var result =
                new List<ILocator>();

            for (var i = 0; i < count; i++)
            {
                var card =
                    allCards.Nth(i);

                try
                {
                    if (!await card.IsVisibleAsync())
                    {
                        continue;
                    }

                    var text =
                        await card.InnerTextAsync();

                    if (LooksLikeMatchCard(text))
                    {
                        result.Add(card);
                    }
                }
                catch (Exception ex)
                {
                    await LogAsync(
                        $"Superbet: błąd odczytu karty #{i}; " +
                        $"error='{ex.Message}'");
                }
            }

            await LogAsync(
                $"Selector: {selector}; " +
                $"poprawnych kart: {result.Count}");

            if (result.Count > 0)
            {
                return result;
            }
        }

        return Array.Empty<ILocator>();
    }

    private static bool LooksLikeMatchCard(
        string text)
    {
        return TryExtractTeams(
            text,
            out _,
            out _);
    }

    private static async Task<SuperbetRawEvent>
        ReadRawEventAsync(
        ILocator card)
    {
        var text =
            await card.InnerTextAsync();

        string? url =
            null;

        var links =
            card.Locator("a[href]");

        var count =
            await links.CountAsync();

        for (var i = 0; i < count; i++)
        {
            var href =
                await links.Nth(i)
                    .GetAttributeAsync("href");

            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            if (!href.Contains(
                    "/kursy/pilka-nozna/",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            url =
                ToAbsoluteUrl(href);

            break;
        }

        return new SuperbetRawEvent
        {
            ContainerText = text,
            DetailsUrl = url
        };
    }

    private static async Task<string?>
        OpenFreshCardAsync(
        IPage page,
        string home,
        string away,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await NavigateAsync(
            page,
            SuperbetSelectors.FootballTodayUrl,
            cancellationToken);

        await AcceptCookiesAsync(
            page,
            cancellationToken);

        await ResetSuperbetListScrollAsync(page);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cards =
                await FindMatchCardsAsync(page);

            ILocator? matchingCard =
                null;

            foreach (var card in cards)
            {
                var text =
                    await card.InnerTextAsync();

                if (ContainsTeams(
                        text,
                        home,
                        away))
                {
                    matchingCard = card;
                    break;
                }
            }

            if (matchingCard is not null)
            {
                await LogAsync(
                    $"Superbet: znaleziono kartę " +
                    $"{home} - {away}; " +
                    $"próba={attempt + 1}");

                var directUrl =
                    await ReadMatchingHrefFromCardAsync(
                        matchingCard,
                        home,
                        away);

                if (IsUrlForMatch(
                        directUrl,
                        home,
                        away))
                {
                    return directUrl;
                }

                var cardVisible =
                    await TryScrollIntoViewAsync(
                        matchingCard,
                        $"match-card:{home}-{away}");

                if (!cardVisible)
                {
                    await LogAsync(
                        $"Superbet: karta meczu jest niewidoczna; " +
                        $"mecz='{home} - {away}'");

                    continue;
                }

                var clickableLinks =
                    matchingCard.Locator(
                        "a[href*='/kursy/pilka-nozna/']");

                var linkCount =
                    await clickableLinks.CountAsync();

                for (var i = 0; i < linkCount; i++)
                {
                    var link =
                        clickableLinks.Nth(i);

                    try
                    {
                        if (!await link.IsVisibleAsync())
                        {
                            continue;
                        }

                        await link.ClickAsync(
                            new LocatorClickOptions
                            {
                                Timeout = 5000
                            });

                        await page.WaitForTimeoutAsync(1200);

                        if (IsUrlForMatch(
                                page.Url,
                                home,
                                away))
                        {
                            return page.Url;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        await LogAsync(
                            $"Superbet: błąd kliknięcia linku; " +
                            $"mecz='{home} - {away}'; " +
                            $"error='{ex.Message}'");
                    }
                }

                var foundOnPage =
                    await FindMatchingDetailsLinkAsync(
                        page,
                        home,
                        away);

                if (IsUrlForMatch(
                        foundOnPage,
                        home,
                        away))
                {
                    return foundOnPage;
                }
            }

            var moved =
                await ScrollSuperbetListStepAsync(page);

            await page.WaitForTimeoutAsync(
                moved ? 900 : 400);
        }

        await LogAsync(
            $"Superbet: nie udało się pobrać URL; " +
            $"mecz='{home} - {away}'");

        return null;
    }

    private static async Task<string?>
        ReadMatchingHrefFromCardAsync(
        ILocator card,
        string home,
        string away)
    {
        var links =
            card.Locator(
                "a[href*='/kursy/pilka-nozna/']");

        var count =
            await links.CountAsync();

        for (var i = 0; i < count; i++)
        {
            var link =
                links.Nth(i);

            try
            {
                if (!await link.IsVisibleAsync())
                {
                    continue;
                }

                var href =
                    await link.GetAttributeAsync(
                        "href");

                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                var absoluteUrl =
                    ToAbsoluteUrl(href);

                await LogAsync(
                    $"Superbet: link w karcie " +
                    $"{home} - {away}: {absoluteUrl}");

                if (IsUrlForMatch(
                        absoluteUrl,
                        home,
                        away))
                {
                    return absoluteUrl;
                }
            }
            catch (Exception ex)
            {
                await LogAsync(
                    $"Superbet: błąd odczytu href; " +
                    $"mecz='{home} - {away}'; " +
                    $"error='{ex.Message}'");
            }
        }

        return null;
    }

    private static async Task<string?>
        FindMatchingDetailsLinkAsync(
        IPage page,
        string home,
        string away)
    {
        var links =
            page.Locator(
                "a[href*='/kursy/pilka-nozna/']");

        var count =
            await links.CountAsync();

        for (var i = 0; i < count; i++)
        {
            try
            {
                var href =
                    await links.Nth(i)
                        .GetAttributeAsync("href");

                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                var absoluteUrl =
                    ToAbsoluteUrl(href);

                if (IsUrlForMatch(
                        absoluteUrl,
                        home,
                        away))
                {
                    return absoluteUrl;
                }
            }
            catch (Exception ex)
            {
                await LogAsync(
                    $"Superbet: błąd odczytu linku strony; " +
                    $"mecz='{home} - {away}'; " +
                    $"error='{ex.Message}'");
            }
        }

        return null;
    }

    private static bool IsUrlForMatch(
        string? url,
        string home,
        string away)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!url.Contains(
                "/kursy/pilka-nozna/",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var slug =
            url
                .Split('?')[0]
                .TrimEnd('/')
                .Split('/')
                .Last();

        var normalizedSlug =
            NormalizeText(slug);

        var homeSlug =
            NormalizeTeamId(home);

        var awaySlug =
            NormalizeTeamId(away);

        var exactExpected =
            $"{homeSlug}-vs-{awaySlug}";

        if (normalizedSlug.StartsWith(
                exactExpected,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var homeParts =
            homeSlug.Split(
                '-',
                StringSplitOptions.RemoveEmptyEntries);

        var awayParts =
            awaySlug.Split(
                '-',
                StringSplitOptions.RemoveEmptyEntries);

        return homeParts.All(
                   x => normalizedSlug.Contains(
                       x,
                       StringComparison.OrdinalIgnoreCase)) &&
               awayParts.All(
                   x => normalizedSlug.Contains(
                       x,
                       StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsTeams(
        string text,
        string home,
        string away)
    {
        var normalizedText =
            NormalizeText(text);

        var normalizedHome =
            NormalizeText(home);

        var normalizedAway =
            NormalizeText(away);

        return normalizedText.Contains(
                   normalizedHome,
                   StringComparison.OrdinalIgnoreCase) &&
               normalizedText.Contains(
                   normalizedAway,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ResetSuperbetListScrollAsync(
        IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
                window.scrollTo(0, 0);

                const elements = [
                    document.scrollingElement,
                    document.documentElement,
                    document.body,
                    ...document.querySelectorAll('*')
                ];

                for (const element of elements) {
                    if (!element) {
                        continue;
                    }

                    const style =
                        getComputedStyle(element);

                    const canScroll =
                        /(auto|scroll)/.test(style.overflowY) &&
                        element.scrollHeight > element.clientHeight;

                    if (canScroll) {
                        element.scrollTop = 0;
                    }
                }
            }
            """);
    }

    private static async Task<bool>
        ScrollSuperbetListStepAsync(
        IPage page)
    {
        var before =
            await page.EvaluateAsync<ScrollSnapshot>(
                """
                () => ({
                    windowTop: Math.round(window.scrollY),
                    documentHeight: document.documentElement.scrollHeight,
                    cards: document.querySelectorAll("[class*='event-card']").length
                })
                """);

        await ScrollPageStepAsync(page);

        await page.WaitForTimeoutAsync(500);

        var after =
            await page.EvaluateAsync<ScrollSnapshot>(
                """
                () => ({
                    windowTop: Math.round(window.scrollY),
                    documentHeight: document.documentElement.scrollHeight,
                    cards: document.querySelectorAll("[class*='event-card']").length
                })
                """);

        await LogAsync(
            $"Superbet: krok scrolla " +
            $"window={before.WindowTop}->{after.WindowTop}; " +
            $"height={before.DocumentHeight}->{after.DocumentHeight}; " +
            $"cards={before.Cards}->{after.Cards}");

        return after.WindowTop > before.WindowTop ||
               after.DocumentHeight > before.DocumentHeight ||
               after.Cards > before.Cards;
    }

    private static async Task ScrollPageStepAsync(
        IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
                const amount =
                    Math.max(
                        500,
                        Math.floor(window.innerHeight * 0.8));

                window.scrollBy({
                    top: amount,
                    behavior: "instant"
                });

                const elements =
                    Array.from(
                        document.querySelectorAll("*"));

                for (const element of elements) {
                    const style =
                        getComputedStyle(element);

                    const canScroll =
                        /(auto|scroll)/.test(style.overflowY) &&
                        element.scrollHeight > element.clientHeight;

                    if (!canScroll) {
                        continue;
                    }

                    const rect =
                        element.getBoundingClientRect();

                    if (rect.bottom > 0 &&
                        rect.top < window.innerHeight) {
                        element.scrollTop += amount;
                    }
                }
            }
            """);
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
                    $"Superbet: brak elementu przed scrollowaniem; " +
                    $"context='{context}'");

                return false;
            }

            if (!await locator.IsVisibleAsync())
            {
                await LogAsync(
                    $"Superbet: element niewidoczny przed scrollowaniem; " +
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
                    $"Superbet: element bez poprawnego bounding box; " +
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
                $"Superbet: timeout scrollowania; " +
                $"context='{context}'; " +
                $"error='{ex.Message}'");

            return false;
        }
        catch (PlaywrightException ex)
        {
            await LogAsync(
                $"Superbet: błąd scrollowania; " +
                $"context='{context}'; " +
                $"error='{ex.Message}'");

            return false;
        }
    }

    private static async Task<bool>
        EnsureOddsTabAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var activeOdds =
            page.Locator(
                "button[role='tab'][data-value='o'][aria-selected='true'], " +
                "button.e2e-event-details-odds-tab[aria-selected='true']")
            .First;

        if (await activeOdds.CountAsync() > 0 &&
            await activeOdds.IsVisibleAsync())
        {
            await LogAsync(
                "Superbet: zakładka Kursy jest już aktywna.");

            return true;
        }

        var tabs =
            page.Locator(
                "button[role='tab'][data-value='o'], " +
                "button.e2e-event-details-odds-tab");

        var count =
            await tabs.CountAsync();

        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tab =
                tabs.Nth(i);

            try
            {
                if (!await tab.IsVisibleAsync())
                {
                    continue;
                }

                var tabVisible =
                    await TryScrollIntoViewAsync(
                        tab,
                        "odds-tab");

                if (!tabVisible)
                {
                    continue;
                }

                await CloseBlockingOverlaysAsync(page);

                await tab.ClickAsync(
                    new LocatorClickOptions
                    {
                        Timeout = 5000
                    });

                await page.WaitForTimeoutAsync(800);

                await LogAsync(
                    "Superbet: aktywowano Kursy.");

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await LogAsync(
                    $"Superbet: błąd aktywacji Kursy; " +
                    $"index={i}; " +
                    $"error='{ex.Message}'");
            }
        }

        await LogAsync(
            "Superbet: nie znaleziono głównej zakładki Kursy.");

        return false;
    }

    private static async Task<IReadOnlyList<Odd>>
        ReadCategoriesAsync(
        IPage page,
        BetMatch match,
        CancellationToken cancellationToken)
    {
        var result =
            new List<Odd>();

        var categories =
            new[]
            {
                "Rzuty rożne",
                "Kartki",
                "Statystyki"
            };

        foreach (var categoryName in categories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var activated =
                await SelectCategoryAsync(
                    page,
                    categoryName,
                    cancellationToken);

            if (!activated)
            {
                await LogAsync(
                    $"Superbet: pominięto kategorię " +
                    $"'{categoryName}'");

                continue;
            }

            // Jedno przewinięcie zamiast wielokrotnego skanowania.
            await ScrollPageStepAsync(page);
            await page.WaitForTimeoutAsync(300);

            var definitions =
                GetMarketsForCategory(categoryName);

            foreach (var definition in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var odds =
                        await ReadMarketDefinitionAsync(
                            page,
                            match,
                            categoryName,
                            definition,
                            cancellationToken);

                    result.AddRange(odds);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (TimeoutException ex)
                {
                    await LogAsync(
                        $"Superbet: timeout definicji rynku; " +
                        $"category='{categoryName}'; " +
                        $"header='{definition.Header}'; " +
                        $"error='{ex.Message}'");

                    continue;
                }
                catch (PlaywrightException ex)
                {
                    await LogAsync(
                        $"Superbet: błąd Playwright definicji rynku; " +
                        $"category='{categoryName}'; " +
                        $"header='{definition.Header}'; " +
                        $"error='{ex.Message}'");

                    continue;
                }
                catch (Exception ex)
                {
                    await LogAsync(
                        $"Superbet: nieoczekiwany błąd definicji rynku; " +
                        $"category='{categoryName}'; " +
                        $"header='{definition.Header}'; " +
                        $"error='{ex.Message}'");

                    continue;
                }
            }
        }

        return result
            .GroupBy(
                x =>
                    $"{x.MatchId}|" +
                    $"{x.Market}|" +
                    $"{x.Period}|" +
                    $"{x.Scope}|" +
                    $"{x.TeamName}|" +
                    $"{x.Selection}|" +
                    $"{x.Line}|" +
                    $"{x.Price}",
                StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Market)
            .ThenBy(x => x.Period)
            .ThenBy(x => x.Scope)
            .ThenBy(x => x.TeamName)
            .ThenBy(x => x.Line ?? decimal.MaxValue)
            .ThenBy(
                x => string.Equals(
                    x.Selection,
                    "Under",
                    StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1)
            .ToList();
    }

    private static IReadOnlyList<MarketDefinition>
        GetMarketsForCategory(
        string categoryName)
    {
        return categoryName switch
        {
            "Rzuty rożne" =>
            [
                new MarketDefinition(
                    "Liczba rzutów rożnych",
                    MarketType.Corners,
                    MarketPeriod.FullTime,
                    MarketScope.Match,
                    false),

                new MarketDefinition(
                    "Liczba rzutów rożnych drużyny",
                    MarketType.Corners,
                    MarketPeriod.FullTime,
                    MarketScope.Team,
                    true),

                new MarketDefinition(
                    "1. połowa - liczba rzutów rożnych",
                    MarketType.Corners,
                    MarketPeriod.FirstHalf,
                    MarketScope.Match,
                    false),

                new MarketDefinition(
                    "1. połowa - liczba rzutów rożnych drużyny",
                    MarketType.Corners,
                    MarketPeriod.FirstHalf,
                    MarketScope.Team,
                    true),

                new MarketDefinition(
                    "2. połowa - liczba rzutów rożnych",
                    MarketType.Corners,
                    MarketPeriod.SecondHalf,
                    MarketScope.Match,
                    false),

                new MarketDefinition(
                    "2. połowa - liczba rzutów rożnych drużyny",
                    MarketType.Corners,
                    MarketPeriod.SecondHalf,
                    MarketScope.Team,
                    true)
            ],

            "Kartki" =>
            [
                new MarketDefinition(
                    "Liczba kartek",
                    MarketType.Cards,
                    MarketPeriod.FullTime,
                    MarketScope.Match,
                    false),

                new MarketDefinition(
                    "Liczba kartek drużyny",
                    MarketType.Cards,
                    MarketPeriod.FullTime,
                    MarketScope.Team,
                    true),

                new MarketDefinition(
                    "1. połowa - liczba kartek",
                    MarketType.Cards,
                    MarketPeriod.FirstHalf,
                    MarketScope.Match,
                    false),

                new MarketDefinition(
                    "1. połowa - liczba kartek drużyny",
                    MarketType.Cards,
                    MarketPeriod.FirstHalf,
                    MarketScope.Team,
                    true),

                new MarketDefinition(
                    "2. połowa - liczba kartek",
                    MarketType.Cards,
                    MarketPeriod.SecondHalf,
                    MarketScope.Match,
                    false),

                new MarketDefinition(
                    "2. połowa - liczba kartek drużyny",
                    MarketType.Cards,
                    MarketPeriod.SecondHalf,
                    MarketScope.Team,
                    true)
            ],

            "Statystyki" =>
            [
                new MarketDefinition(
                    "Liczba celnych strzałów",
                    MarketType.ShotsOnTarget,
                    MarketPeriod.FullTime,
                    MarketScope.Match,
                    false),

                new MarketDefinition(
                    "Liczba celnych strzałów drużyny",
                    MarketType.ShotsOnTarget,
                    MarketPeriod.FullTime,
                    MarketScope.Team,
                    true),

                new MarketDefinition(
                    "1. połowa - liczba celnych strzałów",
                    MarketType.ShotsOnTarget,
                    MarketPeriod.FirstHalf,
                    MarketScope.Match,
                    false),

                new MarketDefinition(
                    "1. połowa - liczba celnych strzałów drużyny",
                    MarketType.ShotsOnTarget,
                    MarketPeriod.FirstHalf,
                    MarketScope.Team,
                    true),

                new MarketDefinition(
                    "2. połowa - liczba celnych strzałów",
                    MarketType.ShotsOnTarget,
                    MarketPeriod.SecondHalf,
                    MarketScope.Match,
                    false),

                new MarketDefinition(
                    "2. połowa - liczba celnych strzałów drużyny",
                    MarketType.ShotsOnTarget,
                    MarketPeriod.SecondHalf,
                    MarketScope.Team,
                    true),

                new MarketDefinition(
                    "Liczba spalonych",
                    MarketType.Offsides,
                    MarketPeriod.FullTime,
                    MarketScope.Match,
                    false),

                new MarketDefinition(
                    "Liczba spalonych drużyny",
                    MarketType.Offsides,
                    MarketPeriod.FullTime,
                    MarketScope.Team,
                    true),

                new MarketDefinition(
                    "1. połowa - liczba spalonych",
                    MarketType.Offsides,
                    MarketPeriod.FirstHalf,
                    MarketScope.Match,
                    false),

                new MarketDefinition(
                    "1. połowa - liczba spalonych drużyny",
                    MarketType.Offsides,
                    MarketPeriod.FirstHalf,
                    MarketScope.Team,
                    true),

                new MarketDefinition(
                    "2. połowa - liczba spalonych",
                    MarketType.Offsides,
                    MarketPeriod.SecondHalf,
                    MarketScope.Match,
                    false),

                new MarketDefinition(
                    "2. połowa - liczba spalonych drużyny",
                    MarketType.Offsides,
                    MarketPeriod.SecondHalf,
                    MarketScope.Team,
                    true)
            ],

            _ =>
                Array.Empty<MarketDefinition>()
        };
    }

    private static async Task<IReadOnlyList<Odd>>
        ReadMarketDefinitionAsync(
        IPage page,
        BetMatch match,
        string categoryName,
        MarketDefinition definition,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var loaded =
                definition.Market == MarketType.Offsides
                    ? await EnsureOffsidesMarketLoadedAsync(
                        page,
                        definition.Header,
                        cancellationToken)
                    : await EnsureMarketLoadedAsync(
                        page,
                        definition.Header,
                        cancellationToken);

            if (!loaded)
            {
                await LogAsync(
                    $"Superbet: pominięto niedostępny rynek; " +
                    $"category='{categoryName}'; " +
                    $"header='{definition.Header}'");

                return Array.Empty<Odd>();
            }

            var card =
                await FindMarketCardAsync(
                    page,
                    definition.Header);

            if (card is null)
            {
                await LogAsync(
                    $"Superbet: karta zniknęła po załadowaniu rynku; " +
                    $"header='{definition.Header}'");

                return Array.Empty<Odd>();
            }

            var text =
                await card.InnerTextAsync();

            if (!definition.HasTeamTabs)
            {
                var odds =
                    ParseMarketTable(
                        match.Id,
                        definition.Market,
                        definition.Period,
                        MarketScope.Match,
                        null,
                        text);

                await LogOddResultsAsync(
                    categoryName,
                    definition.Period,
                    MarketScope.Match,
                    null,
                    odds);

                return odds;
            }

            return await ReadTeamMarketDefinitionAsync(
                page,
                match,
                categoryName,
                definition,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            await LogAsync(
                $"Superbet: timeout rynku — pominięto; " +
                $"category='{categoryName}'; " +
                $"header='{definition.Header}'; " +
                $"error='{ex.Message}'");

            return Array.Empty<Odd>();
        }
        catch (PlaywrightException ex)
        {
            await LogAsync(
                $"Superbet: błąd Playwright rynku — pominięto; " +
                $"category='{categoryName}'; " +
                $"header='{definition.Header}'; " +
                $"error='{ex.Message}'");

            return Array.Empty<Odd>();
        }
        catch (Exception ex)
        {
            await LogAsync(
                $"Superbet: nieoczekiwany błąd rynku — pominięto; " +
                $"category='{categoryName}'; " +
                $"header='{definition.Header}'; " +
                $"error='{ex.Message}'");

            return Array.Empty<Odd>();
        }
    }

    private static async Task<IReadOnlyList<Odd>>
        ReadTeamMarketDefinitionAsync(
        IPage page,
        BetMatch match,
        string categoryName,
        MarketDefinition definition,
        CancellationToken cancellationToken)
    {
        var result =
            new List<Odd>();

        var teams =
            new[]
            {
                match.HomeTeamName,
                match.AwayTeamName
            };

        foreach (var teamName in teams)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var loaded =
                    definition.Market == MarketType.Offsides
                        ? await EnsureOffsidesMarketLoadedAsync(
                            page,
                            definition.Header,
                            cancellationToken)
                        : await EnsureMarketLoadedAsync(
                            page,
                            definition.Header,
                            cancellationToken);

                if (!loaded)
                {
                    await LogAsync(
                        $"Superbet: pominięto rynek drużyny; " +
                        $"header='{definition.Header}'; " +
                        $"team='{teamName}'");

                    continue;
                }

                var card =
                    await FindMarketCardAsync(
                        page,
                        definition.Header);

                if (card is null)
                {
                    continue;
                }

                var selected =
                    await SelectTeamInMarketCardAsync(
                        card,
                        teamName,
                        cancellationToken);

                if (!selected)
                {
                    await LogAsync(
                        $"Superbet: nie wybrano drużyny; " +
                        $"category='{categoryName}'; " +
                        $"header='{definition.Header}'; " +
                        $"team='{teamName}'");

                    continue;
                }

                await page.WaitForTimeoutAsync(500);

                var refreshedCard =
                    await FindMarketCardAsync(
                        page,
                        definition.Header);

                if (refreshedCard is null)
                {
                    continue;
                }

                var text =
                    await refreshedCard.InnerTextAsync();

                var odds =
                    ParseMarketTable(
                        match.Id,
                        definition.Market,
                        definition.Period,
                        MarketScope.Team,
                        teamName,
                        text);

                result.AddRange(odds);

                await LogOddResultsAsync(
                    categoryName,
                    definition.Period,
                    MarketScope.Team,
                    teamName,
                    odds);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException ex)
            {
                await LogAsync(
                    $"Superbet: timeout drużyny — pominięto; " +
                    $"header='{definition.Header}'; " +
                    $"team='{teamName}'; " +
                    $"error='{ex.Message}'");

                continue;
            }
            catch (PlaywrightException ex)
            {
                await LogAsync(
                    $"Superbet: błąd Playwright drużyny — pominięto; " +
                    $"header='{definition.Header}'; " +
                    $"team='{teamName}'; " +
                    $"error='{ex.Message}'");

                continue;
            }
            catch (Exception ex)
            {
                await LogAsync(
                    $"Superbet: nieoczekiwany błąd drużyny — pominięto; " +
                    $"header='{definition.Header}'; " +
                    $"team='{teamName}'; " +
                    $"error='{ex.Message}'");

                continue;
            }
        }

        return result;
    }

    private static async Task<bool>
        EnsureMarketLoadedAsync(
        IPage page,
        string marketHeader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var card =
            await FindMarketCardAsync(
                page,
                marketHeader);

        if (card is null)
        {
            return false;
        }

        var text =
            await card.InnerTextAsync();

        if (ContainsOddsRows(text))
        {
            return true;
        }

        return await ExpandMarketCardAsync(
            page,
            marketHeader,
            cancellationToken);
    }

    private static async Task<bool>
        EnsureOffsidesMarketLoadedAsync(
        IPage page,
        string header,
        CancellationToken cancellationToken)
    {
        await LogAsync(
            $"Superbet: próba załadowania rynku spalonych " +
            $"'{header}'");

        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var card =
                await FindMarketCardAsync(
                    page,
                    header);

            if (card is null)
            {
                await ScrollPageStepAsync(page);
                await page.WaitForTimeoutAsync(400);
                continue;
            }

            var cardVisible =
                await TryScrollIntoViewAsync(
                    card,
                    $"offsides-card:{header}");

            if (!cardVisible)
            {
                await LogAsync(
                    $"Superbet: karta spalonych niewidoczna; " +
                    $"header='{header}'");

                return false;
            }

            var text =
                await card.InnerTextAsync();

            if (ContainsOddsRows(text))
            {
                return true;
            }

            var expanded =
                await ExpandMarketCardAsync(
                    page,
                    header,
                    cancellationToken);

            if (!expanded)
            {
                await page.WaitForTimeoutAsync(350);
                continue;
            }

            await page.WaitForTimeoutAsync(500);

            var refreshed =
                await FindMarketCardAsync(
                    page,
                    header);

            if (refreshed is null)
            {
                continue;
            }

            var refreshedText =
                await refreshed.InnerTextAsync();

            if (ContainsOddsRows(refreshedText))
            {
                await LogAsync(
                    $"Superbet: załadowano rynek spalonych; " +
                    $"header='{header}'");

                return true;
            }
        }

        await LogAsync(
            $"Superbet: rynek spalonych nadal bez kursów; " +
            $"header='{header}'");

        return false;
    }

    private static async Task<bool>
        SelectCategoryAsync(
        IPage page,
        string categoryName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates =
            page.Locator(
                "button, [role='tab'], [role='button']")
            .Filter(
                new LocatorFilterOptions
                {
                    HasText = categoryName
                });

        var count =
            await candidates.CountAsync();

        await LogAsync(
            $"Superbet: szukanie kategorii " +
            $"'{categoryName}'; kandydatów={count}");

        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate =
                candidates.Nth(i);

            try
            {
                if (!await candidate.IsVisibleAsync())
                {
                    continue;
                }

                if (await IsMainMatchNavigationTabAsync(
                        candidate))
                {
                    continue;
                }

                if (!await IsMarketCategoryTabAsync(
                        candidate))
                {
                    continue;
                }

                var selected =
                    await candidate.GetAttributeAsync(
                        "aria-selected");

                var className =
                    await candidate.GetAttributeAsync(
                        "class");

                var active =
                    selected == "true" ||
                    (className ?? string.Empty).Contains(
                        "active",
                        StringComparison.OrdinalIgnoreCase) ||
                    (className ?? string.Empty).Contains(
                        "selected",
                        StringComparison.OrdinalIgnoreCase);

                if (!active)
                {
                    var visible =
                        await TryScrollIntoViewAsync(
                            candidate,
                            $"category:{categoryName}");

                    if (!visible)
                    {
                        continue;
                    }

                    await candidate.ClickAsync(
                        new LocatorClickOptions
                        {
                            Timeout = 5000
                        });

                    await page.WaitForTimeoutAsync(800);
                }

                if (categoryName == "Statystyki")
                {
                    var statisticsCard =
                        await FindStatisticsMarketCardAsync(
                            page);

                    if (statisticsCard is null)
                    {
                        continue;
                    }
                }

                await LogAsync(
                    $"Superbet: aktywowano kategorię " +
                    $"{categoryName}");

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await LogAsync(
                    $"Superbet: błąd aktywacji kategorii; " +
                    $"category='{categoryName}'; " +
                    $"index={i}; " +
                    $"error='{ex.Message}'");

                continue;
            }
        }

        await LogAsync(
            $"Superbet: nie znaleziono kategorii " +
            $"{categoryName}");

        return false;
    }

    private static async Task<bool>
        IsMainMatchNavigationTabAsync(
        ILocator candidate)
    {
        var html =
            await candidate.EvaluateAsync<string>(
                "element => element.outerHTML");

        var className =
            await candidate.GetAttributeAsync(
                "class");

        var dataValue =
            await candidate.GetAttributeAsync(
                "data-value");

        var parentText =
            await candidate.EvaluateAsync<string>(
                """
                element => {
                    const parent = element.parentElement;

                    return parent
                        ? (parent.innerText || '')
                        : '';
                }
                """);

        var normalizedHtml =
            NormalizeText(html);

        var normalizedClass =
            NormalizeText(
                className ?? string.Empty);

        var normalizedParent =
            NormalizeText(parentText);

        var mainByClass =
            normalizedHtml.Contains(
                "e2e-event-details") ||
            normalizedClass.Contains(
                "event-details") ||
            normalizedClass.Contains(
                "event-header");

        var mainByParent =
            normalizedParent.Contains("chat") &&
            normalizedParent.Contains("kursy") &&
            normalizedParent.Contains("h2h");

        var mainByDataValue =
            string.Equals(
                dataValue,
                "s",
                StringComparison.OrdinalIgnoreCase);

        return mainByClass ||
               mainByParent ||
               mainByDataValue;
    }

    private static async Task<bool>
        IsMarketCategoryTabAsync(
        ILocator candidate)
    {
        var html =
            await candidate.EvaluateAsync<string>(
                "element => element.outerHTML");

        var className =
            await candidate.GetAttributeAsync(
                "class");

        var ancestorText =
            await candidate.EvaluateAsync<string>(
                """
                element => {
                    let current = element.parentElement;
                    let result = '';

                    for (var i = 0;
                         i < 4 && current;
                         i++)
                    {
                        result += ' ' +
                            (current.innerText || '');

                        current =
                            current.parentElement;
                    }

                    return result;
                }
                """);

        var normalizedHtml =
            NormalizeText(html);

        var normalizedClass =
            NormalizeText(
                className ?? string.Empty);

        var normalizedAncestor =
            NormalizeText(ancestorText);

        if (normalizedHtml.Contains(
                "e2e-event-details") ||
            normalizedClass.Contains(
                "event-details"))
        {
            return false;
        }

        return normalizedHtml.Contains(
                   "segmented") ||
               normalizedHtml.Contains(
                   "filter-bar") ||
               normalizedHtml.Contains(
                   "market") ||
               normalizedAncestor.Contains(
                   "bet builder") ||
               normalizedAncestor.Contains(
                   "rzuty rozne") ||
               normalizedAncestor.Contains(
                   "kartki") ||
               normalizedAncestor.Contains(
                   "statystyki") ||
               normalizedAncestor.Contains(
                   "laczne") ||
               normalizedAncestor.Contains(
                   "szybkie");
    }

    private static async Task<ILocator?>
        FindStatisticsMarketCardAsync(
        IPage page)
    {
        var cards =
            page.Locator(
                "[class*='market-layout-card']");

        var count =
            await cards.CountAsync();

        for (var i = 0; i < count; i++)
        {
            var card =
                cards.Nth(i);

            try
            {
                if (!await card.IsVisibleAsync())
                {
                    continue;
                }

                var text =
                    await card.InnerTextAsync();

                var normalized =
                    NormalizeMarketText(text);

                var isStatisticsMarket =
                    normalized.Contains(
                        "celnych strzalow") ||
                    normalized.Contains(
                        "spalone") ||
                    normalized.Contains(
                        "liczba fauli") ||
                    normalized.Contains(
                        "liczba strzalow");

                if (!isStatisticsMarket)
                {
                    continue;
                }

                await LogAsync(
                    $"Superbet: potwierdzono Statystyki; " +
                    $"index={i}; " +
                    $"text='{Shorten(text, 850)}'");

                return card;
            }
            catch (Exception ex)
            {
                await LogAsync(
                    $"Superbet: błąd wyszukiwania karty statystyk; " +
                    $"index={i}; " +
                    $"error='{ex.Message}'");
            }
        }

        return null;
    }

    private static async Task<ILocator?>
        FindMarketCardAsync(
        IPage page,
        string marketHeader)
    {
        var cards =
            page.Locator(
                "[class*='market-layout-card']");

        var count =
            await cards.CountAsync();

        var expected =
            NormalizeMarketText(marketHeader);

        for (var i = 0; i < count; i++)
        {
            var card =
                cards.Nth(i);

            try
            {
                if (!await card.IsVisibleAsync())
                {
                    continue;
                }

                var text =
                    await card.InnerTextAsync();

                var normalized =
                    NormalizeMarketText(text);

                if (!normalized.Contains(
                        expected,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await LogAsync(
                    $"Superbet: znaleziono kartę rynku; " +
                    $"header='{marketHeader}'; " +
                    $"index={i}; " +
                    $"hasRows={ContainsOddsRows(text)}");

                return card;
            }
            catch (Exception ex)
            {
                await LogAsync(
                    $"Superbet: błąd odczytu karty rynku; " +
                    $"header='{marketHeader}'; " +
                    $"index={i}; " +
                    $"error='{ex.Message}'");
            }
        }

        await LogAsync(
            $"Superbet: nie znaleziono karty rynku; " +
            $"header='{marketHeader}'; " +
            $"cards={count}");

        return null;
    }

    private static async Task<bool>
        ExpandMarketCardAsync(
        IPage page,
        string marketHeader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var card =
            await FindMarketCardAsync(
                page,
                marketHeader);

        if (card is null)
        {
            return false;
        }

        var cardVisible =
            await TryScrollIntoViewAsync(
                card,
                $"market-card:{marketHeader}");

        if (!cardVisible)
        {
            await LogAsync(
                $"Superbet: pominięto niewidoczną kartę; " +
                $"header='{marketHeader}'");

            return false;
        }

        var beforeText =
            await card.InnerTextAsync();

        if (ContainsOddsRows(beforeText))
        {
            return true;
        }

        var expandTargets =
            card.Locator(
                """
                button[aria-expanded='false'],
                [role='button'][aria-expanded='false'],
                [data-id*='expand' i],
                [data-id*='collapse' i],
                [class*='chevron'],
                [class*='expand'],
                [class*='collapse']
                """);

        var targetCount =
            await expandTargets.CountAsync();

        await LogAsync(
            $"Superbet: kandydaci rozwinięcia; " +
            $"header='{marketHeader}'; " +
            $"count={targetCount}");

        for (var i = 0; i < targetCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target =
                expandTargets.Nth(i);

            try
            {
                if (!await target.IsVisibleAsync())
                {
                    continue;
                }

                var ariaLabel =
                    await target.GetAttributeAsync(
                        "aria-label");

                var dataId =
                    await target.GetAttributeAsync(
                        "data-id");

                var className =
                    await target.GetAttributeAsync(
                        "class");

                var description =
                    $"{ariaLabel} {dataId} {className}";

                if (IsInformationOrFavouriteControl(
                        description))
                {
                    continue;
                }

                var targetVisible =
                    await TryScrollIntoViewAsync(
                        target,
                        $"expand-target:{marketHeader}:{i}");

                if (!targetVisible)
                {
                    continue;
                }

                await CloseBlockingOverlaysAsync(page);

                await target.DispatchEventAsync(
                    "click");

                await page.WaitForTimeoutAsync(500);

                var refreshed =
                    await FindMarketCardAsync(
                        page,
                        marketHeader);

                if (refreshed is null)
                {
                    continue;
                }

                var refreshedText =
                    await refreshed.InnerTextAsync();

                if (ContainsOddsRows(refreshedText))
                {
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await LogAsync(
                    $"Superbet: błąd rozwijania rynku; " +
                    $"header='{marketHeader}'; " +
                    $"index={i}; " +
                    $"error='{ex.Message}'");

                continue;
            }
        }

        var clickedHeader =
            await TryClickMarketHeaderAsync(
                page,
                card,
                marketHeader,
                cancellationToken);

        if (!clickedHeader)
        {
            return false;
        }

        await page.WaitForTimeoutAsync(500);

        var refreshedCard =
            await FindMarketCardAsync(
                page,
                marketHeader);

        if (refreshedCard is null)
        {
            return false;
        }

        var refreshedCardText =
            await refreshedCard.InnerTextAsync();

        return ContainsOddsRows(
            refreshedCardText);
    }

    private static async Task<bool>
        TryClickMarketHeaderAsync(
        IPage page,
        ILocator card,
        string marketHeader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var expected =
            NormalizeMarketText(
                marketHeader);

        var header =
            card.Locator(
                """
                [data-id='market-header-base'],
                [class*='market-header-base']
                """)
            .First;

        if (await header.CountAsync() == 0 ||
            !await header.IsVisibleAsync())
        {
            return false;
        }

        var headerText =
            await header.InnerTextAsync();

        if (!NormalizeMarketText(headerText).Contains(
                expected,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var visible =
            await TryScrollIntoViewAsync(
                header,
                $"market-header:{marketHeader}");

        if (!visible)
        {
            return false;
        }

        await CloseBlockingOverlaysAsync(page);

        try
        {
            await header.DispatchEventAsync(
                "click");

            await page.WaitForTimeoutAsync(500);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await LogAsync(
                $"Superbet: błąd kliknięcia nagłówka; " +
                $"header='{marketHeader}'; " +
                $"error='{ex.Message}'");

            return false;
        }
    }

    private static async Task
        CloseBlockingOverlaysAsync(
        IPage page)
    {
        var overlay =
            page.Locator(
                """
                div.modal__overlay:visible,
                div[class*='modal__overlay']:visible,
                div[class*='modal-overlay']:visible,
                div[class*='onboarding']:visible
                """)
            .Last;

        if (await overlay.CountAsync() == 0)
        {
            return;
        }

        try
        {
            if (!await overlay.IsVisibleAsync())
            {
                return;
            }

            var code =
                await overlay.GetAttributeAsync(
                    "code");

            var className =
                await overlay.GetAttributeAsync(
                    "class");

            await LogAsync(
                $"Superbet: aktywny overlay; " +
                $"code='{code}'; " +
                $"class='{className}'");

            var closeButton =
                overlay.Locator(
                    """
                    button[aria-label*='close' i],
                    button[aria-label*='zamknij' i],
                    button[class*='close'],
                    [role='button'][aria-label*='close' i],
                    [role='button'][aria-label*='zamknij' i]
                    """)
                .First;

            if (await closeButton.CountAsync() > 0 &&
                await closeButton.IsVisibleAsync())
            {
                try
                {
                    await closeButton.ClickAsync(
                        new LocatorClickOptions
                        {
                            Timeout = 1000
                        });

                    await page.WaitForTimeoutAsync(100);

                    return;
                }
                catch (PlaywrightException)
                {
                    // Spróbuj Escape.
                }
            }

            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(100);
        }
        catch (Exception ex)
        {
            await LogAsync(
                $"Superbet: błąd obsługi overlay; " +
                $"error='{ex.Message}'");
        }
    }

    private static bool IsInformationOrFavouriteControl(
        string value)
    {
        var normalized =
            NormalizeText(value ?? string.Empty);

        return normalized.Contains("info") ||
               normalized.Contains("information") ||
               normalized.Contains("market-header-info") ||
               normalized.Contains("favorite") ||
               normalized.Contains("favourite") ||
               normalized.Contains("bookmark") ||
               normalized.Contains("star");
    }

    private static async Task<bool>
        SelectTeamInMarketCardAsync(
        ILocator marketCard,
        string teamName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates =
            marketCard.Locator(
                "button, [role='tab'], [role='button']");

        var count =
            await candidates.CountAsync();

        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate =
                candidates.Nth(i);

            try
            {
                if (!await candidate.IsVisibleAsync())
                {
                    continue;
                }

                var text =
                    (await candidate.InnerTextAsync()).Trim();

                if (!string.Equals(
                        text,
                        teamName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var selected =
                    await candidate.GetAttributeAsync(
                        "aria-selected");

                if (selected == "true")
                {
                    return true;
                }

                var visible =
                    await TryScrollIntoViewAsync(
                        candidate,
                        $"team:{teamName}");

                if (!visible)
                {
                    continue;
                }

                await candidate.ClickAsync(
                    new LocatorClickOptions
                    {
                        Timeout = 4000
                    });

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await LogAsync(
                    $"Superbet: błąd wyboru drużyny; " +
                    $"team='{teamName}'; " +
                    $"error='{ex.Message}'");

                continue;
            }
        }

        return false;
    }

    private static bool ContainsOddsRows(
        string text)
    {
        var normalized =
            Regex.Replace(
                text.Replace(',', '.'),
                @"\s+",
                " ").Trim();

        return Regex.IsMatch(
            normalized,
            @"(?<![\d.])" +
            @"\d{1,2}[.][05]" +
            @"\s+" +
            @"\d{1,3}[.]\d{2,3}" +
            @"\s+" +
            @"\d{1,3}[.]\d{2,3}" +
            @"(?![\d.])");
    }

    private static IReadOnlyList<Odd>
        ParseMarketTable(
        string matchId,
        MarketType market,
        MarketPeriod period,
        MarketScope scope,
        string? teamName,
        string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<Odd>();
        }

        var normalized =
            Regex.Replace(
                text.Replace(',', '.'),
                @"\s+",
                " ").Trim();

        var rowPattern =
            new Regex(
                @"(?<![\d.])" +
                @"(?<line>\d{1,2}[.][05])" +
                @"\s+" +
                @"(?<under>\d{1,3}[.]\d{2,3})" +
                @"\s+" +
                @"(?<over>\d{1,3}[.]\d{2,3})" +
                @"(?![\d.])",
                RegexOptions.CultureInvariant);

        var result =
            new List<Odd>();

        foreach (RegexMatch row in
                 rowPattern.Matches(normalized))
        {
            if (!decimal.TryParse(
                    row.Groups["line"].Value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var line))
            {
                continue;
            }

            if (!decimal.TryParse(
                    row.Groups["under"].Value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var underPrice))
            {
                continue;
            }

            if (!decimal.TryParse(
                    row.Groups["over"].Value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var overPrice))
            {
                continue;
            }

            if (underPrice <= 1.0m ||
                overPrice <= 1.0m)
            {
                continue;
            }

            result.Add(
                new Odd
                {
                    MatchId = matchId,
                    Market = market,
                    Period = period,
                    Scope = scope,
                    TeamName = teamName,
                    Selection = "Under",
                    Line = line,
                    Price = underPrice
                });

            result.Add(
                new Odd
                {
                    MatchId = matchId,
                    Market = market,
                    Period = period,
                    Scope = scope,
                    TeamName = teamName,
                    Selection = "Over",
                    Line = line,
                    Price = overPrice
                });
        }

        return result;
    }

    private static async Task LogOddResultsAsync(
        string categoryName,
        MarketPeriod period,
        MarketScope scope,
        string? teamName,
        IReadOnlyList<Odd> odds)
    {
        foreach (var odd in odds)
        {
            await LogAsync(
                $"Superbet ODD: " +
                $"category='{categoryName}'; " +
                $"market='{odd.Market}'; " +
                $"period='{GetPeriodName(period)}'; " +
                $"scope='{scope}'; " +
                $"team='{teamName ?? "(mecz)"}'; " +
                $"selection='{odd.Selection}'; " +
                $"line='{odd.Line?.ToString(
                    "0.0",
                    CultureInfo.InvariantCulture) ??
                    "(brak)"}'; " +
                $"price='{odd.Price.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture)}'");
        }
    }

    private static string GetPeriodName(
        MarketPeriod period)
    {
        return period switch
        {
            MarketPeriod.FullTime =>
                "FullTime",

            MarketPeriod.FirstHalf =>
                "FirstHalf",

            MarketPeriod.SecondHalf =>
                "SecondHalf",

            _ =>
                period.ToString()
        };
    }

    private static async Task ScrollPageToBottomAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < 20; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var before =
                await page.EvaluateAsync<long>(
                    "document.documentElement.scrollHeight");

            await ScrollPageStepAsync(page);

            await page.Keyboard.PressAsync("End");

            await page.WaitForTimeoutAsync(400);

            var after =
                await page.EvaluateAsync<long>(
                    "document.documentElement.scrollHeight");

            if (after == before)
            {
                break;
            }
        }
    }

    private static bool TryExtractTeams(
        string text,
        out string home,
        out string away)
    {
        home = string.Empty;
        away = string.Empty;

        var lines =
            NormalizeLines(text);

        var oneIndex =
            lines
                .Select(
                    (value, index) =>
                        new
                        {
                            Value = value,
                            Index = index
                        })
                .FirstOrDefault(
                    x => x.Value == "1")
                ?.Index ?? -1;

        if (oneIndex < 2)
        {
            return false;
        }

        var beforeOdds =
            lines
                .Take(oneIndex)
                .Where(x => !IsMetadataLine(x))
                .ToList();

        if (beforeOdds.Count < 2)
        {
            return false;
        }

        var homeCandidate =
            beforeOdds[^2];

        var awayCandidate =
            beforeOdds[^1];

        if (!IsTeamName(homeCandidate) ||
            !IsTeamName(awayCandidate))
        {
            return false;
        }

        home =
            homeCandidate;

        away =
            awayCandidate;

        return true;
    }

    private static bool IsTeamName(
        string value)
    {
        if (value.Length < 2 ||
            value.Length > 60)
        {
            return false;
        }

        var normalized =
            NormalizeText(value);

        var excluded =
            new[]
            {
                "pilka",
                "nozna",
                "mecz",
                "bet builder",
                "dzisiaj",
                "jutro",
                "liga",
                "uefa",
                "miedzynarodowe",
                "remis"
            };

        return !excluded.Any(
                   normalized.Contains) &&
               !Regex.IsMatch(
                   value,
                   @"\d");
    }

    private static bool IsMetadataLine(
        string value)
    {
        var text =
            NormalizeText(value);

        return text.Contains("pilka") ||
               text.Contains("nozna") ||
               text.Contains("dzisiaj") ||
               text.Contains("jutro") ||
               text.Contains("liga") ||
               text.Contains("uefa") ||
               text.Contains("bet builder") ||
               text.Contains("mecz") ||
               text.Contains("remis");
    }

    private static List<string>
        NormalizeLines(
        string text)
    {
        return text
            .Split(
                new[]
                {
                    '\r',
                    '\n'
                },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(
                x => Regex.Replace(
                    x,
                    @"\s+",
                    " ").Trim())
            .Where(x => x.Length > 0)
            .ToList();
    }

    private static string NormalizeMarketText(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized =
            NormalizeText(value)
                .Replace(
                    "1.polowa",
                    "1 polowa")
                .Replace(
                    "2.polowa",
                    "2 polowa")
                .Replace(
                    "1. polowa",
                    "1 polowa")
                .Replace(
                    "2. polowa",
                    "2 polowa");

        return Regex.Replace(
            normalized,
            @"\s+",
            " ").Trim();
    }

    private static string NormalizeText(
        string value)
    {
        return value
            .ToLowerInvariant()
            .Replace("ą", "a")
            .Replace("ć", "c")
            .Replace("ę", "e")
            .Replace("ł", "l")
            .Replace("ń", "n")
            .Replace("ó", "o")
            .Replace("ś", "s")
            .Replace("ź", "z")
            .Replace("ż", "z");
    }

    private static string NormalizeTeamId(
        string value)
    {
        return Regex.Replace(
                NormalizeText(value),
                @"[^a-z0-9]+",
                "-")
            .Trim('-');
    }

    private static string ToAbsoluteUrl(
        string href)
    {
        return href.StartsWith(
                "http",
                StringComparison.OrdinalIgnoreCase)
            ? href
            : new Uri(
                new Uri("https://superbet.pl"),
                href).ToString();
    }

    private static async Task NavigateAsync(
        IPage page,
        string url,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await page.GotoAsync(
            url,
            new PageGotoOptions
            {
                WaitUntil =
                    WaitUntilState.DOMContentLoaded
            });

        await page.WaitForTimeoutAsync(1000);
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
                    Timeout = 5000
                });
        }
        catch (PlaywrightException)
        {
            // Baner został zamknięty wcześniej.
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
                $"Superbet: nie udało się zapisać diagnostyki; " +
                $"name='{name}'; " +
                $"error='{ex.Message}'");
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
            "scraper-diagnostics/superbet.log",
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} " +
            $"{message}{Environment.NewLine}");
    }

    private static string Shorten(
        string value,
        int maxLength)
    {
        value =
            Regex.Replace(
                value ?? string.Empty,
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

    private sealed record MarketDefinition(
        string Header,
        MarketType Market,
        MarketPeriod Period,
        MarketScope Scope,
        bool HasTeamTabs);

    private sealed class SuperbetRawEvent
    {
        public required string ContainerText { get; init; }

        public string? DetailsUrl { get; init; }
    }

    private sealed class ScrollState
    {
        public long Top { get; init; }

        public long Height { get; init; }

        public long Viewport { get; init; }
    }

    private sealed class SuperbetListItem
    {
        public required string HomeTeam { get; init; }

        public required string AwayTeam { get; init; }

        public string? DetailsUrl { get; init; }
    }

    private sealed class ScrollSnapshot
    {
        public long WindowTop { get; init; }

        public long DocumentHeight { get; init; }

        public int Cards { get; init; }
    }
}
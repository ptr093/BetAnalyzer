using Microsoft.Playwright;
namespace BetAnalyzer.Core.Contracts;
public interface IBrowserSession : IAsyncDisposable
{
    Task<IPage> CreatePageAsync(CancellationToken cancellationToken = default);
}

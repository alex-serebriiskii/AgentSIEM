using Microsoft.FSharp.Collections;

namespace Siem.Api.Services;

public interface IListCacheService
{
    FSharpSet<string> ResolveList(Guid listId);
    Task<long> RefreshAsync(CancellationToken ct = default);
    IReadOnlyList<ListCacheInfo> GetCacheInfo();
}

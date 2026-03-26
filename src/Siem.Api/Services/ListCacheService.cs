using Microsoft.EntityFrameworkCore;
using Microsoft.FSharp.Collections;
using Siem.Api.Data;

namespace Siem.Api.Services;

/// <summary>
/// Singleton that caches managed lists from the database.
/// Uses a volatile field for lock-free reads by background workers.
/// The cache is refreshed as part of the recompilation pipeline to
/// guarantee consistency with compiled rules.
/// </summary>
public class ListCacheService : IListCacheService
{
    private volatile IReadOnlyDictionary<Guid, FrozenListSnapshot> _cache
        = new Dictionary<Guid, FrozenListSnapshot>();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ListCacheService> _logger;

    public ListCacheService(IServiceScopeFactory scopeFactory, ILogger<ListCacheService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Called by the F# compiler's list resolver during compilation.
    /// Returns the current members of a managed list as an F# Set.
    /// If the list doesn't exist, returns an empty set -- a missing list
    /// shouldn't crash compilation, it should produce a rule that never
    /// matches the list condition.
    /// </summary>
    public FSharpSet<string> ResolveList(Guid listId)
    {
        if (_cache.TryGetValue(listId, out var snapshot))
            return snapshot.MembersSet;

        _logger.LogWarning("List {ListId} not found in cache during compilation", listId);
        return SetModule.Empty<string>();
    }

    /// <summary>
    /// Refresh the entire list cache from the database.
    /// Called BEFORE rule compilation to ensure the compiler sees the latest
    /// list contents. Returns a version token for logging consistency.
    /// </summary>
    public async Task<long> RefreshAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SiemDbContext>();

        var lists = await db.ManagedLists
            .Include(l => l.Members)
            .Where(l => l.Enabled)
            .ToListAsync(ct);

        var newCache = new Dictionary<Guid, FrozenListSnapshot>(lists.Count);
        var totalMembers = 0;

        foreach (var list in lists)
        {
            var members = list.Members
                .Select(m => m.Value)
                .ToArray();

            var fsharpSet = SetModule.OfArray(members);

            newCache[list.Id] = new FrozenListSnapshot
            {
                ListId = list.Id,
                Name = list.Name,
                MembersSet = fsharpSet,
                MemberCount = members.Length,
                LoadedAt = DateTime.UtcNow
            };

            totalMembers += members.Length;
        }

        // Atomic swap -- same pattern as the rule engine
        _cache = newCache;

        _logger.LogInformation(
            "List cache refreshed: {ListCount} lists, {MemberCount} total members",
            lists.Count, totalMembers);

        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Diagnostic: returns the current cache state for the management API.
    /// </summary>
    public IReadOnlyList<ListCacheInfo> GetCacheInfo()
    {
        return _cache.Values
            .Select(s => new ListCacheInfo(s.ListId, s.Name, s.MemberCount, s.LoadedAt))
            .ToList();
    }

    internal class FrozenListSnapshot
    {
        public Guid ListId { get; init; }
        public string Name { get; init; } = string.Empty;
        public FSharpSet<string> MembersSet { get; init; } = SetModule.Empty<string>();
        public int MemberCount { get; init; }
        public DateTime LoadedAt { get; init; }
    }
}

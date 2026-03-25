namespace Siem.Api.Services;

public record ListCacheInfo(Guid ListId, string Name, int MemberCount, DateTime LoadedAt);

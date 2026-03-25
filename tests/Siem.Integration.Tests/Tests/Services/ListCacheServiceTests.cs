using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Api.Services;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;

namespace Siem.Integration.Tests.Tests.Services;

[NotInParallel("database")]
public class ListCacheServiceTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    private static ListCacheService CreateService()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SiemDbContext>(options =>
            options.UseNpgsql(IntegrationTestFixture.TimescaleConnectionString));
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        return new ListCacheService(scopeFactory, NullLogger<ListCacheService>.Instance);
    }

    [Test]
    public async Task RefreshAsync_LoadsEnabledLists()
    {
        var listId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.ManagedLists.Add(new ManagedListEntity
            {
                Id = listId,
                Name = "Approved Tools",
                Description = "Tools",
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now,
                Members =
                [
                    new ListMemberEntity { ListId = listId, Value = "tool-a", AddedAt = now },
                    new ListMemberEntity { ListId = listId, Value = "tool-b", AddedAt = now }
                ]
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();
        await service.RefreshAsync();

        var resolved = service.ResolveList(listId);
        resolved.Count.Should().Be(2);
        resolved.Contains("tool-a").Should().BeTrue();
        resolved.Contains("tool-b").Should().BeTrue();
    }

    [Test]
    public async Task ResolveList_NonexistentList_ReturnsEmptySet()
    {
        var service = CreateService();
        await service.RefreshAsync();

        var resolved = service.ResolveList(Guid.NewGuid());
        resolved.Count.Should().Be(0);
    }

    [Test]
    public async Task RefreshAsync_DisabledListsExcluded()
    {
        var enabledId = Guid.NewGuid();
        var disabledId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.ManagedLists.Add(new ManagedListEntity
            {
                Id = enabledId,
                Name = "Enabled List",
                Description = "Enabled",
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now,
                Members = [new ListMemberEntity { ListId = enabledId, Value = "val", AddedAt = now }]
            });
            db.ManagedLists.Add(new ManagedListEntity
            {
                Id = disabledId,
                Name = "Disabled List",
                Description = "Disabled",
                Enabled = false,
                CreatedAt = now,
                UpdatedAt = now,
                Members = [new ListMemberEntity { ListId = disabledId, Value = "val", AddedAt = now }]
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();
        await service.RefreshAsync();

        service.ResolveList(enabledId).Count.Should().Be(1);
        service.ResolveList(disabledId).Count.Should().Be(0);
    }
}

using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;

namespace Siem.Api.Tests.Controllers.Helpers;

public static class DbContextFactory
{
    public static SiemDbContext Create([CallerMemberName] string? testName = null)
    {
        var options = new DbContextOptionsBuilder<SiemDbContext>()
            .UseInMemoryDatabase(databaseName: $"SiemTest_{testName}_{Guid.NewGuid():N}")
            .Options;
        return new SiemDbContext(options);
    }
}

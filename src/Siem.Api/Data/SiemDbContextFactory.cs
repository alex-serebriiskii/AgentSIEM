using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Siem.Api.Data;

public class SiemDbContextFactory : IDesignTimeDbContextFactory<SiemDbContext>
{
    public SiemDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SiemDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=agentsiem;Username=siem;Password=siem");

        return new SiemDbContext(optionsBuilder.Options);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Rail.Api.Models;

// Migration tooling must not start the web host or its platform event logger.
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var root = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "Rail.Api.csproj"))
            ? Directory.GetCurrentDirectory() : Path.Combine(Directory.GetCurrentDirectory(), "Rail.Api");
        var configuration = new ConfigurationBuilder().SetBasePath(root)
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<AppDbContextFactory>(optional: true).AddEnvironmentVariables().Build();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(configuration.GetConnectionString("DefaultConnection")).Options);
    }
}

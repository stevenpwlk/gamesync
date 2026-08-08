using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GameSaveHub.Server.Infrastructure;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GameSaveHubDbContext>
{
    public GameSaveHubDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("GSH_CONNECTION_STRING")
            ?? "Data Source=data/gamesavehub.db;Cache=Shared;Pooling=True";
        var options = new DbContextOptionsBuilder<GameSaveHubDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new GameSaveHubDbContext(options);
    }
}

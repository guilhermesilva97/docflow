using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocFlow.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    public static async Task MigrateWithRetryAsync(DbContext context, ILogger logger, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await context.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Migrações aplicadas em {Database}", context.Database.GetDbConnection().Database);
                return;
            }
            catch (SqlException error) when (attempt < 20)
            {
                logger.LogWarning("Banco indisponível (tentativa {Attempt}): {Message}", attempt, error.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }
}

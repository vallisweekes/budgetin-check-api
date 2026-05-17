using Npgsql;

namespace BudgetinCheck.Api.Infrastructure.Data;

internal sealed class BudgetDbConnectionFactory(IConfiguration configuration)
{
    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("BudgetDb")
            ?? configuration["ConnectionStrings:BudgetDb"]
            ?? configuration["DATABASE_URL"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A PostgreSQL connection string is required. Set ConnectionStrings:BudgetDb or DATABASE_URL.");
        }

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
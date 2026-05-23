using Npgsql;
using NpgsqlTypes;

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

        var connection = new NpgsqlConnection(NormalizeConnectionString(connectionString));
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string NormalizeConnectionString(string rawConnectionString)
    {
        var connectionString = rawConnectionString.Trim();
        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var uri = new Uri(connectionString, UriKind.Absolute);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
        };

        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length > 0 && !string.IsNullOrWhiteSpace(userInfo[0]))
        {
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
        }

        if (userInfo.Length > 1 && !string.IsNullOrWhiteSpace(userInfo[1]))
        {
            builder.Password = Uri.UnescapeDataString(userInfo[1]);
        }

        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
        {
            return builder.ConnectionString;
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2);
            var key = parts[0].Trim();
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Trim()) : string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            switch (key.ToLowerInvariant())
            {
                case "sslmode":
                    if (Enum.TryParse<SslMode>(value, true, out var sslMode))
                    {
                        builder.SslMode = sslMode;
                    }
                    break;
                case "connect_timeout":
                    if (int.TryParse(value, out var timeoutSeconds) && timeoutSeconds > 0)
                    {
                        builder.Timeout = timeoutSeconds;
                    }
                    break;
                case "application_name":
                    builder.ApplicationName = value;
                    break;
                case "pgbouncer":
                    break;
            }
        }

        return builder.ConnectionString;
    }
}
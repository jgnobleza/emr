using Npgsql;

namespace medrec.Data;

public sealed class PostgresConnectionFactory
{
    private readonly IConfiguration _configuration;
    private readonly string? _connectionString;

    public PostgresConnectionFactory(IConfiguration configuration)
    {
        _configuration = configuration;
        _connectionString = BuildConnectionString();
    }

    public string? ConnectionString => _connectionString;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    public NpgsqlConnection CreateConnection()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("PostgreSQL connection string 'DefaultConnection' is not configured.");
        }

        return new NpgsqlConnection(ConnectionString);
    }

    private string? BuildConnectionString()
    {
        var configured = _configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(configured)
            && !configured.Contains("change_this_password", StringComparison.OrdinalIgnoreCase))
        {
            return configured;
        }

        var databaseUrl = _configuration["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return configured;
        }

        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(0) ?? string.Empty),
            Password = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? string.Empty),
            SslMode = SslMode.Require
        };

        return builder.ConnectionString;
    }
}


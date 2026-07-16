using MySqlConnector;

namespace medrec.Data;

public sealed class MySqlConnectionFactory
{
    private readonly IConfiguration _configuration;

    public MySqlConnectionFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string? ConnectionString => _configuration.GetConnectionString("DefaultConnection");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    public MySqlConnection CreateConnection()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("MySQL connection string 'DefaultConnection' is not configured.");
        }

        return new MySqlConnection(ConnectionString);
    }
}

using Microsoft.Data.Sqlite;

namespace medrec.Data;

public sealed class SqliteConnectionFactory
{
    private readonly LocalAppPaths _paths;

    public SqliteConnectionFactory(LocalAppPaths paths)
    {
        _paths = paths;
    }

    public string ConnectionString
    {
        get
        {
            _paths.EnsureCreated();
            return new SqliteConnectionStringBuilder
            {
                DataSource = _paths.DatabasePath,
                Cache = SqliteCacheMode.Shared
            }.ToString();
        }
    }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(ConnectionString);
    }
}

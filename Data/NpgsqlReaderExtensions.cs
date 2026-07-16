using Npgsql;

namespace medrec.Data;

public static class NpgsqlReaderExtensions
{
    public static string GetString(this NpgsqlDataReader reader, string name) =>
        reader.GetString(reader.GetOrdinal(name));

    public static int GetInt32(this NpgsqlDataReader reader, string name) =>
        reader.GetInt32(reader.GetOrdinal(name));

    public static decimal GetDecimal(this NpgsqlDataReader reader, string name) =>
        reader.GetDecimal(reader.GetOrdinal(name));

    public static bool GetBoolean(this NpgsqlDataReader reader, string name) =>
        reader.GetBoolean(reader.GetOrdinal(name));

    public static DateTime GetDateTime(this NpgsqlDataReader reader, string name) =>
        reader.GetDateTime(reader.GetOrdinal(name));
}

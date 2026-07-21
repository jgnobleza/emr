using Microsoft.Data.Sqlite;
using Npgsql;

var localDb = @"C:\Users\Administrator\AppData\Roaming\medrec-desktop\MedRec\medrec.local.db";
var uri = new Uri("postgresql://emrdb_mtxm_user:1CW1uy8lv3PQRxCEbHR451O8qA6RXHkk@dpg-d9ciidb7uimc73dt3dl0-a.oregon-postgres.render.com/emrdb_mtxm");
var userInfo = uri.UserInfo.Split(':', 2);
var pg = new NpgsqlConnectionStringBuilder
{
    Host = uri.Host,
    Database = uri.AbsolutePath.TrimStart('/'),
    Username = Uri.UnescapeDataString(userInfo[0]),
    Password = Uri.UnescapeDataString(userInfo[1]),
    SslMode = SslMode.Require
}.ConnectionString;

Console.WriteLine("LOCAL PATIENT PHOTOS");
await using (var local = new SqliteConnection($"Data Source={localDb}"))
{
    await local.OpenAsync();
    await using var cmd = local.CreateCommand();
    cmd.CommandText = """
        SELECT id, client_uid, full_name, photo_url, sync_status, updated_at
        FROM patients
        WHERE photo_url IS NOT NULL AND trim(photo_url) <> ''
        ORDER BY updated_at DESC
        LIMIT 100;
        """;
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"{reader["id"]} | {reader["client_uid"]} | {reader["full_name"]} | {reader["sync_status"]} | {reader["updated_at"]} | {reader["photo_url"]}");
    }
}

Console.WriteLine();
Console.WriteLine("CLOUD PATIENT PHOTOS");
await using (var cloud = new NpgsqlConnection(pg))
{
    await cloud.OpenAsync();
    await using var cmd = new NpgsqlCommand("""
        SELECT id, client_uid, full_name, photo_url, sync_status, updated_at
        FROM patients
        WHERE photo_url IS NOT NULL AND trim(photo_url) <> ''
        ORDER BY updated_at DESC
        LIMIT 100;
        """, cloud);
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"{reader["id"]} | {reader["client_uid"]} | {reader["full_name"]} | {reader["sync_status"]} | {reader["updated_at"]} | {reader["photo_url"]}");
    }
}

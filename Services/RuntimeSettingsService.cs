using System.Text.Json;
using medrec.Data;
using medrec.ViewModels;
using Microsoft.Data.Sqlite;

namespace medrec.Services;

public sealed class RuntimeSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly SqliteConnectionFactory _sqliteConnections;

    public RuntimeSettingsService(IConfiguration configuration, IWebHostEnvironment environment, SqliteConnectionFactory sqliteConnections)
    {
        _configuration = configuration;
        _environment = environment;
        _sqliteConnections = sqliteConnections;
    }

    private string SettingsPath => Path.Combine(_environment.ContentRootPath, "App_Data", "runtime-settings.json");

    public async Task SavePostgresConnectionStringAsync(string connectionString)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);

        var document = await ReadDocumentAsync();
        document.ConnectionStrings["DefaultConnection"] = connectionString;
        document.MedRec[nameof(MedRecStorageOptions.FileStorageProvider)] = CurrentValue("MedRec:FileStorageProvider", "Local");

        await WriteDocumentAsync(document);
        await SaveLocalSettingsAsync(document);

        if (_configuration is IConfigurationRoot root)
        {
            root.Reload();
        }
    }

    public async Task SaveGoogleDriveSettingsAsync(GoogleDriveSettingsFormModel form)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);

        var document = await ReadDocumentAsync();
        document.GoogleDrive[nameof(GoogleDriveStorageOptions.ApplicationName)] = string.IsNullOrWhiteSpace(form.ApplicationName) ? "MedRec" : form.ApplicationName.Trim();
        document.GoogleDrive[nameof(GoogleDriveStorageOptions.FolderId)] = form.FolderId.Trim();
        document.GoogleDrive[nameof(GoogleDriveStorageOptions.ServiceAccountJson)] = form.ServiceAccountJson.Trim();
        document.GoogleDrive[nameof(GoogleDriveStorageOptions.ServiceAccountJsonBase64)] = form.ServiceAccountJsonBase64.Trim();
        document.GoogleDrive[nameof(GoogleDriveStorageOptions.ServiceAccountJsonPath)] = form.ServiceAccountJsonPath.Trim();
        document.MedRec[nameof(MedRecStorageOptions.FileStorageProvider)] = "GoogleDrive";

        await WriteDocumentAsync(document);
        await SaveLocalSettingsAsync(document);

        if (_configuration is IConfigurationRoot root)
        {
            root.Reload();
        }
    }

    public async Task ApplySyncedSettingsAsync(IReadOnlyDictionary<string, string> settings)
    {
        if (settings.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var document = await ReadDocumentAsync();
        foreach (var (key, value) in settings)
        {
            var separator = key.IndexOf(':');
            if (separator <= 0 || separator >= key.Length - 1)
            {
                continue;
            }

            var section = key[..separator];
            var name = key[(separator + 1)..];
            if (section.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase))
            {
                document.ConnectionStrings[name] = value;
            }
            else if (section.Equals("GoogleDrive", StringComparison.OrdinalIgnoreCase))
            {
                document.GoogleDrive[name] = value;
            }
            else if (section.Equals("MedRec", StringComparison.OrdinalIgnoreCase))
            {
                document.MedRec[name] = value;
            }
        }

        await WriteDocumentAsync(document);
        if (_configuration is IConfigurationRoot root)
        {
            root.Reload();
        }
    }

    private async Task SaveLocalSettingsAsync(RuntimeSettingsDocument document)
    {
        try
        {
            await using var connection = _sqliteConnections.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();
            foreach (var item in Flatten(document))
            {
                await using var command = new SqliteCommand("""
                    INSERT INTO app_settings (key, value, is_secret, sync_status, updated_at)
                    VALUES (@key, @value, 1, 'Pending', CURRENT_TIMESTAMP)
                    ON CONFLICT(key) DO UPDATE SET
                      value=excluded.value,
                      is_secret=1,
                      sync_status='Pending',
                      updated_at=CURRENT_TIMESTAMP;
                    """, connection, transaction);
                command.Parameters.AddWithValue("@key", item.Key);
                command.Parameters.AddWithValue("@value", item.Value);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            // The web/cloud app may not have a local SQLite database. The JSON settings file remains the source in that case.
        }
    }

    private async Task WriteDocumentAsync(RuntimeSettingsDocument document)
    {
        await using var stream = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions);
    }

    private string CurrentValue(string key, string fallback) =>
        string.IsNullOrWhiteSpace(_configuration[key]) ? fallback : _configuration[key]!;

    private static IEnumerable<KeyValuePair<string, string>> Flatten(RuntimeSettingsDocument document)
    {
        foreach (var item in document.ConnectionStrings)
        {
            yield return new KeyValuePair<string, string>($"ConnectionStrings:{item.Key}", item.Value);
        }

        foreach (var item in document.GoogleDrive)
        {
            yield return new KeyValuePair<string, string>($"GoogleDrive:{item.Key}", item.Value);
        }

        foreach (var item in document.MedRec)
        {
            yield return new KeyValuePair<string, string>($"MedRec:{item.Key}", item.Value);
        }
    }

    private async Task<RuntimeSettingsDocument> ReadDocumentAsync()
    {
        if (!File.Exists(SettingsPath))
        {
            return new RuntimeSettingsDocument();
        }

        await using var stream = File.OpenRead(SettingsPath);
        return await JsonSerializer.DeserializeAsync<RuntimeSettingsDocument>(stream, JsonOptions)
            ?? new RuntimeSettingsDocument();
    }

    private sealed class RuntimeSettingsDocument
    {
        public Dictionary<string, string> ConnectionStrings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> GoogleDrive { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> MedRec { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

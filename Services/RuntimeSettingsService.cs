using System.Text.Json;
using medrec.Data;
using medrec.ViewModels;

namespace medrec.Services;

public sealed class RuntimeSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public RuntimeSettingsService(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    private string SettingsPath => Path.Combine(_environment.ContentRootPath, "App_Data", "runtime-settings.json");

    public async Task SavePostgresConnectionStringAsync(string connectionString)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);

        var document = await ReadDocumentAsync();
        document.ConnectionStrings["DefaultConnection"] = connectionString;

        await using (var stream = File.Create(SettingsPath))
        {
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions);
        }

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

        await using (var stream = File.Create(SettingsPath))
        {
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions);
        }

        if (_configuration is IConfigurationRoot root)
        {
            root.Reload();
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

namespace medrec.Data;

public sealed class LocalAppPaths
{
    private readonly IWebHostEnvironment _environment;
    private readonly MedRecStorageOptions _options;

    public LocalAppPaths(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _options = configuration.GetSection("MedRec").Get<MedRecStorageOptions>() ?? new MedRecStorageOptions();
    }

    public string DataRoot
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_options.LocalDataPath))
            {
                return _options.LocalDataPath;
            }

            if (OperatingSystem.IsWindows())
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrWhiteSpace(localAppData))
                {
                    return Path.Combine(localAppData, "MedRec");
                }
            }

            return Path.Combine(_environment.ContentRootPath, "App_Data", "local");
        }
    }

    public string DatabasePath => Path.Combine(DataRoot, "medrec.local.db");

    public string FilesRoot => Path.Combine(DataRoot, "files");

    public string GoogleDriveTokenRoot => Path.Combine(DataRoot, "google-drive-token");

    public string GoogleDriveCacheRoot => Path.Combine(DataRoot, "google-drive-cache");

    public string FileFolder(string folder) => Path.Combine(FilesRoot, folder);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(FilesRoot);
        Directory.CreateDirectory(GoogleDriveTokenRoot);
        Directory.CreateDirectory(GoogleDriveCacheRoot);
        foreach (var folder in new[] { "patients", "labs", "signatures", "logos", "layout-images" })
        {
            Directory.CreateDirectory(FileFolder(folder));
        }
    }
}

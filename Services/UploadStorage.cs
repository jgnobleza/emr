using medrec.Data;

namespace medrec.Services;

public sealed class UploadStorage
{
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly LocalAppPaths _localPaths;
    private readonly GoogleDriveStorage _googleDrive;
    private readonly ILogger<UploadStorage> _logger;

    public UploadStorage(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        LocalAppPaths localPaths,
        GoogleDriveStorage googleDrive,
        ILogger<UploadStorage> logger)
    {
        _environment = environment;
        _configuration = configuration;
        _localPaths = localPaths;
        _googleDrive = googleDrive;
        _logger = logger;
    }

    public async Task<string?> SaveAsync(IFormFile? file, string folder)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        var extension = Path.GetExtension(file.FileName);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var options = _configuration.GetSection("MedRec").Get<MedRecStorageOptions>() ?? new MedRecStorageOptions();

        if (options.UseGoogleDriveStorage && _googleDrive.IsConfigured)
        {
            try
            {
                return await _googleDrive.UploadAsync(file, folder);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Google Drive upload failed. Saving the file locally for now.");
            }
        }

        string root;
        string publicPrefix;
        if (options.UseLocalStorage)
        {
            _localPaths.EnsureCreated();
            root = _localPaths.FileFolder(folder);
            publicPrefix = "/local-files";
        }
        else
        {
            root = Path.Combine(_environment.WebRootPath, "uploads", folder);
            publicPrefix = "/uploads";
        }

        Directory.CreateDirectory(root);
        var path = Path.Combine(root, fileName);
        await using var stream = File.Create(path);
        await file.CopyToAsync(stream);

        return $"{publicPrefix}/{folder}/{fileName}";
    }
}

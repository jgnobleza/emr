using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using medrec.Data;
using System.Text;

namespace medrec.Services;

public sealed class GoogleDriveStorage
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleDriveStorage> _logger;
    private DriveService? _drive;
    private string? _driveCacheKey;

    public GoogleDriveStorage(IConfiguration configuration, ILogger<GoogleDriveStorage> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsConfigured
    {
        get
        {
            var options = GetOptions();
            return !string.IsNullOrWhiteSpace(options.FolderId) && TryReadCredentialJson(options) is not null;
        }
    }

    public async Task<string?> UploadAsync(IFormFile? file, string folder, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        var extension = Path.GetExtension(file.FileName);
        var safeFolder = string.Concat(folder.Where(char.IsLetterOrDigit));
        var fileName = $"{safeFolder}-{Guid.NewGuid():N}{extension}";

        await using var stream = file.OpenReadStream();
        return await UploadStreamAsync(stream, fileName, folder, string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType, cancellationToken);
    }

    public async Task<string> UploadFileAsync(string path, string folder, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The local file to upload was not found.", path);
        }

        var extension = Path.GetExtension(path);
        var safeFolder = string.Concat(folder.Where(char.IsLetterOrDigit));
        var fileName = $"{safeFolder}-{Guid.NewGuid():N}{extension}";
        await using var stream = File.OpenRead(path);
        return await UploadStreamAsync(stream, fileName, folder, ContentTypeFromExtension(extension), cancellationToken);
    }

    private async Task<string> UploadStreamAsync(Stream stream, string fileName, string folder, string contentType, CancellationToken cancellationToken)
    {
        var options = GetOptions();
        var service = GetDriveService(options);
        var metadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = fileName,
            Parents = [options.FolderId],
            AppProperties = new Dictionary<string, string>
            {
                ["medrecFolder"] = folder
            }
        };
        var request = service.Files.Create(metadata, stream, string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        request.Fields = "id,name,mimeType,size";
        request.SupportsAllDrives = true;
        var result = await request.UploadAsync(cancellationToken);
        if (result.Exception is not null)
        {
            throw new InvalidOperationException(ReadableUploadError(result.Exception), result.Exception);
        }

        var uploaded = request.ResponseBody;
        if (uploaded?.Id is null)
        {
            throw new InvalidOperationException("Google Drive upload did not return a file id.");
        }

        return $"/files/drive/{Uri.EscapeDataString(uploaded.Id)}/{Uri.EscapeDataString(fileName)}";
    }

    private static string ContentTypeFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    public async Task<DriveDownload?> DownloadAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var service = GetDriveService(GetOptions());
        var metadataRequest = service.Files.Get(fileId);
        metadataRequest.Fields = "id,name,mimeType,size";
        metadataRequest.SupportsAllDrives = true;
        var metadata = await metadataRequest.ExecuteAsync(cancellationToken);

        var stream = new MemoryStream();
        var downloadRequest = service.Files.Get(fileId);
        downloadRequest.SupportsAllDrives = true;
        await downloadRequest.DownloadAsync(stream, cancellationToken);
        stream.Position = 0;

        return new DriveDownload(
            stream,
            string.IsNullOrWhiteSpace(metadata.MimeType) ? "application/octet-stream" : metadata.MimeType,
            string.IsNullOrWhiteSpace(metadata.Name) ? $"{fileId}.bin" : metadata.Name);
    }

    private DriveService GetDriveService(GoogleDriveStorageOptions options)
    {
        var credentialJson = TryReadCredentialJson(options);
        var cacheKey = $"{options.ApplicationName}|{options.FolderId}|{options.ServiceAccountJsonPath}|{options.ServiceAccountJsonBase64}|{options.ServiceAccountJson}";
        if (_drive is not null && string.Equals(_driveCacheKey, cacheKey, StringComparison.Ordinal))
        {
            return _drive;
        }

        if (credentialJson is null || string.IsNullOrWhiteSpace(options.FolderId))
        {
            throw new InvalidOperationException("Google Drive storage is not configured. Set GoogleDrive:FolderId and a service account JSON value/path.");
        }

        using var credentialStream = new MemoryStream(Encoding.UTF8.GetBytes(credentialJson));
        var credential = CredentialFactory
            .FromStream<ServiceAccountCredential>(credentialStream)
            .ToGoogleCredential()
            .CreateScoped(DriveService.Scope.Drive);

        _drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = string.IsNullOrWhiteSpace(options.ApplicationName) ? "MedRec" : options.ApplicationName
        });
        _driveCacheKey = cacheKey;

        return _drive;
    }

    private GoogleDriveStorageOptions GetOptions() =>
        _configuration.GetSection("GoogleDrive").Get<GoogleDriveStorageOptions>() ?? new GoogleDriveStorageOptions();

    private static string ReadableUploadError(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("Service Accounts do not have storage quota", StringComparison.OrdinalIgnoreCase))
        {
            return "Google Drive upload failed because the service account has no personal storage quota. Use a folder inside a Google Workspace Shared Drive and share that Shared Drive/folder with the service account as Contributor or Content manager, then save that folder ID in Admin settings.";
        }

        return "Google Drive upload failed.";
    }

    private string? TryReadCredentialJson(GoogleDriveStorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceAccountJson))
        {
            return options.ServiceAccountJson;
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceAccountJsonBase64))
        {
            try
            {
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(options.ServiceAccountJsonBase64));
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "Google Drive service account JSON base64 is invalid.");
                return null;
            }
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceAccountJsonPath) && File.Exists(options.ServiceAccountJsonPath))
        {
            return File.ReadAllText(options.ServiceAccountJsonPath);
        }

        return null;
    }
}

public sealed record DriveDownload(Stream Stream, string ContentType, string FileName);

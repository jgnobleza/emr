using medrec.Services;
using medrec.Data;
using Microsoft.AspNetCore.Mvc;

namespace medrec.Controllers;

[Route("files")]
public sealed class FilesController : Controller
{
    private readonly GoogleDriveStorage _googleDrive;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LocalAppPaths _localPaths;

    public FilesController(GoogleDriveStorage googleDrive, IHttpClientFactory httpClientFactory, LocalAppPaths localPaths)
    {
        _googleDrive = googleDrive;
        _httpClientFactory = httpClientFactory;
        _localPaths = localPaths;
    }

    [HttpGet("drive/{fileId}/{*fileName}")]
    public async Task<IActionResult> Drive(string fileId, string? fileName = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return NotFound();
        }

        var cachedPath = CachedDriveFilePath(fileId);
        if (cachedPath is not null)
        {
            return PhysicalFile(cachedPath, ContentTypeFromExtension(Path.GetExtension(cachedPath)), enableRangeProcessing: true);
        }

        DriveDownload? download;
        try
        {
            download = await _googleDrive.DownloadAsync(fileId, cancellationToken);
        }
        catch
        {
            return await PublicDriveFile(fileId, cancellationToken);
        }

        if (download is null)
        {
            return await PublicDriveFile(fileId, cancellationToken);
        }

        Response.Headers.CacheControl = "private, max-age=3600";
        return File(download.Stream, download.ContentType, enableRangeProcessing: true);
    }

    private string? CachedDriveFilePath(string fileId)
    {
        var root = Path.GetFullPath(_localPaths.GoogleDriveCacheRoot);
        var folder = Path.GetFullPath(Path.Combine(root, SafePathSegment(fileId)));
        if (!folder.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(folder))
        {
            return null;
        }

        var path = Directory.EnumerateFiles(folder).FirstOrDefault();
        return path is null ? null : Path.GetFullPath(path);
    }

    private async Task<IActionResult> PublicDriveFile(string fileId, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync(GoogleDriveStorage.PublicDownloadUrl(fileId), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return NotFound();
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(stream, string.IsNullOrWhiteSpace(contentType) ? "application/pdf" : contentType, enableRangeProcessing: false);
    }

    private static string SafePathSegment(string value)
    {
        var safe = new string(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "file" : safe;
    }

    private static string ContentTypeFromExtension(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
}

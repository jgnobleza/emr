using medrec.Services;
using Microsoft.AspNetCore.Mvc;

namespace medrec.Controllers;

[Route("files")]
public sealed class FilesController : Controller
{
    private readonly GoogleDriveStorage _googleDrive;
    private readonly IHttpClientFactory _httpClientFactory;

    public FilesController(GoogleDriveStorage googleDrive, IHttpClientFactory httpClientFactory)
    {
        _googleDrive = googleDrive;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("drive/{fileId}/{*fileName}")]
    public async Task<IActionResult> Drive(string fileId, string? fileName = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return NotFound();
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
}

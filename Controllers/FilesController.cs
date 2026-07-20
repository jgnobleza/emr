using medrec.Services;
using Microsoft.AspNetCore.Mvc;

namespace medrec.Controllers;

[Route("files")]
public sealed class FilesController : Controller
{
    private readonly GoogleDriveStorage _googleDrive;

    public FilesController(GoogleDriveStorage googleDrive)
    {
        _googleDrive = googleDrive;
    }

    [HttpGet("drive/{fileId}/{*fileName}")]
    public async Task<IActionResult> Drive(string fileId, string? fileName = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return NotFound();
        }

        var download = await _googleDrive.DownloadAsync(fileId, cancellationToken);
        if (download is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=3600";
        return File(download.Stream, download.ContentType, enableRangeProcessing: true);
    }
}

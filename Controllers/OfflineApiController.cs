using medrec.Models;
using medrec.Services;
using Microsoft.AspNetCore.Mvc;

namespace medrec.Controllers;

[ApiController]
[Route("api/offline")]
public sealed class OfflineApiController : ControllerBase
{
    private readonly OfflineSyncService _offlineSync;

    public OfflineApiController(OfflineSyncService offlineSync)
    {
        _offlineSync = offlineSync;
    }

    [HttpGet("snapshot")]
    public async Task<ActionResult<OfflineSyncSnapshot>> Snapshot()
    {
        return await _offlineSync.GetSnapshotAsync(CurrentUser());
    }

    [HttpPost("push")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<OfflineSyncResult>> Push([FromBody] OfflineSyncBatch batch)
    {
        return await _offlineSync.ApplyBatchAsync(batch, CurrentUser());
    }

    private AppUser CurrentUser()
    {
        return new AppUser
        {
            Id = HttpContext.Session.GetInt32("UserId") ?? 0,
            Email = HttpContext.Session.GetString("UserEmail") ?? string.Empty,
            FullName = HttpContext.Session.GetString("UserName") ?? "Doctor",
            Role = HttpContext.Session.GetString("UserRole") ?? "Doctor",
            Specialty = HttpContext.Session.GetString("UserSpecialty") ?? string.Empty,
            LicenseNumber = HttpContext.Session.GetString("UserLicenseNumber") ?? string.Empty,
            ContactNumber = HttpContext.Session.GetString("UserContactNumber") ?? string.Empty,
            SignatureUrl = HttpContext.Session.GetString("UserSignatureUrl"),
            IsActive = true
        };
    }
}

using medrec.Services;
using medrec.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace medrec.Controllers;

public sealed class SettingsController : Controller
{
    private readonly AccountService _accounts;
    private readonly UploadStorage _uploads;

    public SettingsController(AccountService accounts, UploadStorage uploads)
    {
        _accounts = accounts;
        _uploads = uploads;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();
        return View(new DoctorProfileViewModel
        {
            FullName = user.FullName,
            Specialty = user.Specialty,
            LicenseNumber = user.LicenseNumber,
            ContactNumber = user.ContactNumber,
            Email = user.Email,
            SignatureUrl = user.SignatureUrl
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(DoctorProfileViewModel model)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();
        model.Email = user.Email;
        model.SignatureUrl = user.SignatureUrl;

        if (model.Signature is { Length: > 0 })
        {
            if (!IsAllowedImage(model.Signature))
            {
                ModelState.AddModelError(nameof(model.Signature), "Upload a PNG, JPG, GIF, or WEBP image.");
            }
            else if (model.Signature.Length > 5 * 1024 * 1024)
            {
                ModelState.AddModelError(nameof(model.Signature), "Signature image must be 5 MB or smaller.");
            }
        }

        if (!ModelState.IsValid) return View(model);

        var signatureUrl = await _uploads.SaveAsync(model.Signature, "signatures") ?? user.SignatureUrl;
        await _accounts.UpdateDoctorProfileAsync(user.Id, model.FullName, model.Specialty, model.LicenseNumber, model.ContactNumber, signatureUrl);
        HttpContext.Session.SetString("UserName", model.FullName.Trim());
        HttpContext.Session.SetString("UserSpecialty", model.Specialty.Trim());
        HttpContext.Session.SetString("UserLicenseNumber", model.LicenseNumber.Trim());
        HttpContext.Session.SetString("UserContactNumber", model.ContactNumber.Trim());
        if (!string.IsNullOrWhiteSpace(signatureUrl)) HttpContext.Session.SetString("UserSignatureUrl", signatureUrl);
        TempData["Success"] = "Professional profile saved.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<medrec.Models.AppUser?> CurrentUserAsync()
    {
        var id = HttpContext.Session.GetInt32("UserId");
        return id.HasValue ? await _accounts.GetUserAsync(id.Value) : null;
    }

    private static bool IsAllowedImage(IFormFile file) =>
        new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" }.Contains(Path.GetExtension(file.FileName), StringComparer.OrdinalIgnoreCase);

}

using medrec.Services;
using medrec.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace medrec.Controllers;

public sealed class AccountController : Controller
{
    private readonly AccountService _accounts;
    private readonly OfflineSyncService _offlineSync;

    public AccountController(AccountService accounts, OfflineSyncService offlineSync)
    {
        _accounts = accounts;
        _offlineSync = offlineSync;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (HttpContext.Session.GetInt32("UserId").HasValue)
        {
            return RedirectToLocal(returnUrl);
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var user = await _accounts.AuthenticateAsync(model.Username, model.Password);
            if (user is null)
            {
                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View(model);
            }

            await _offlineSync.EnsureSchemaAsync();

            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("UserEmail", user.Email);
            HttpContext.Session.SetString("UserName", user.FullName);
            HttpContext.Session.SetString("UserRole", user.Role);
            HttpContext.Session.SetString("UserSpecialty", user.Specialty);
            HttpContext.Session.SetString("UserLicenseNumber", user.LicenseNumber);
            HttpContext.Session.SetString("UserContactNumber", user.ContactNumber);
            if (!string.IsNullOrWhiteSpace(user.SignatureUrl))
            {
                HttpContext.Session.SetString("UserSignatureUrl", user.SignatureUrl);
            }
        }
        catch
        {
            ModelState.AddModelError(string.Empty, "Login is unavailable. Check the MySQL connection and apply Database/schema.sql.");
            return View(model);
        }

        return RedirectToLocal(model.ReturnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction(nameof(Login));
    }

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }
}

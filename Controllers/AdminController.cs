using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using medrec.Data;
using medrec.Security;
using medrec.Services;
using medrec.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace medrec.Controllers;

[AdminOnly]
public sealed class AdminController : Controller
{
    private readonly EmrRepository _repository;
    private readonly AccountService _accounts;
    private readonly UploadStorage _uploads;
    private readonly PostgresConnectionFactory _postgresConnections;
    private readonly RuntimeSettingsService _runtimeSettings;

    public AdminController(
        EmrRepository repository,
        AccountService accounts,
        UploadStorage uploads,
        PostgresConnectionFactory postgresConnections,
        RuntimeSettingsService runtimeSettings)
    {
        _repository = repository;
        _accounts = accounts;
        _uploads = uploads;
        _postgresConnections = postgresConnections;
        _runtimeSettings = runtimeSettings;
    }

    public async Task<IActionResult> Index()
    {
        return View(await BuildPageAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDoctor([Bind(Prefix = "NewDoctor")] CreateDoctorAccountViewModel form)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageAsync(newDoctor: form));
        }

        try
        {
            await _accounts.CreateDoctorAsync(form.FullName, form.Email, form.Password);
            TempData["Success"] = $"Doctor account for {form.FullName.Trim()} created.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("NewDoctor.Email", ex is InvalidOperationException ? ex.Message : "Account creation failed. Check the PostgreSQL connection.");
            return View("Index", await BuildPageAsync(newDoctor: form));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnarchivePatient(int id)
    {
        try
        {
            await _repository.UnarchivePatientAsync(id);
            TempData["Success"] = "Patient restored to the active patient list.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex is InvalidOperationException ? ex.Message : "Patient could not be restored.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdatePrescriptionLayout([Bind(Prefix = "PrescriptionLayoutForm")] PrintLayoutFormModel form) =>
        SavePrintLayoutAsync(form, "Prescription", "PrescriptionLayoutForm", "prescriptionLayoutModal", "PrescriptionLayoutForm.Logo");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdateDiagnosisLayout([Bind(Prefix = "DiagnosisLayoutForm")] PrintLayoutFormModel form) =>
        SavePrintLayoutAsync(form, "Diagnosis", "DiagnosisLayoutForm", "diagnosisLayoutModal", "DiagnosisLayoutForm.Logo");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestPostgresConnection([Bind(Prefix = "PostgresSettings")] PostgresSettingsFormModel form)
    {
        var normalized = NormalizePostedPostgresSettings(form);
        if (normalized is null)
        {
            return View("Index", await BuildPageAsync(postgresSettings: form));
        }

        var result = await TestPostgresConnectionStringAsync(normalized);
        if (result.Success)
        {
            TempData["Success"] = result.Message;
        }
        else
        {
            TempData["Error"] = result.Message;
        }

        return View("Index", await BuildPageAsync(postgresSettings: form));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePostgresSettings([Bind(Prefix = "PostgresSettings")] PostgresSettingsFormModel form)
    {
        var normalized = NormalizePostedPostgresSettings(form);
        if (normalized is null)
        {
            return View("Index", await BuildPageAsync(postgresSettings: form));
        }

        var result = await TestPostgresConnectionStringAsync(normalized);
        if (!result.Success)
        {
            TempData["Error"] = $"Connection was not saved. {result.Message}";
            return View("Index", await BuildPageAsync(postgresSettings: form));
        }

        await _runtimeSettings.SavePostgresConnectionStringAsync(normalized);
        TempData["Success"] = "Render PostgreSQL connection saved and tested.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InitializePostgresDatabase([Bind(Prefix = "PostgresSettings")] PostgresSettingsFormModel form)
    {
        var normalized = NormalizePostedPostgresSettings(form);
        if (normalized is null)
        {
            return View("Index", await BuildPageAsync(postgresSettings: form));
        }

        var result = await TestPostgresConnectionStringAsync(normalized);
        if (!result.Success)
        {
            TempData["Error"] = $"Database was not initialized. {result.Message}";
            return View("Index", await BuildPageAsync(postgresSettings: form));
        }

        var schemaPath = ResolveSchemaPath("schema.sql");
        if (!System.IO.File.Exists(schemaPath))
        {
            TempData["Error"] = $"Database was not initialized. Schema file was not found at {schemaPath}.";
            return View("Index", await BuildPageAsync(postgresSettings: form));
        }

        try
        {
            await using var connection = new NpgsqlConnection(normalized);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(await System.IO.File.ReadAllTextAsync(schemaPath), connection)
            {
                CommandTimeout = 60
            };
            await command.ExecuteNonQueryAsync();
            await _runtimeSettings.SavePostgresConnectionStringAsync(normalized);
            var tableCount = await CountMedRecTablesAsync(connection);
            TempData["Success"] = $"Database initialized. {tableCount} MedRec tables are available and the connection was saved.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Database initialization failed: {ex.Message}";
            return View("Index", await BuildPageAsync(postgresSettings: form));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestGoogleDriveSettings([Bind(Prefix = "GoogleDriveSettings")] GoogleDriveSettingsFormModel form)
    {
        if (!TryNormalizeGoogleDriveSettings(form))
        {
            return View("Index", await BuildPageAsync(googleDriveSettings: form));
        }

        var result = await TestGoogleDriveSettingsAsync(form);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return View("Index", await BuildPageAsync(googleDriveSettings: form));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGoogleDriveSettings([Bind(Prefix = "GoogleDriveSettings")] GoogleDriveSettingsFormModel form)
    {
        if (!TryNormalizeGoogleDriveSettings(form))
        {
            return View("Index", await BuildPageAsync(googleDriveSettings: form));
        }

        var result = await TestGoogleDriveSettingsAsync(form);
        if (!result.Success)
        {
            TempData["Error"] = $"Google Drive settings were not saved. {result.Message}";
            return View("Index", await BuildPageAsync(googleDriveSettings: form));
        }

        await _runtimeSettings.SaveGoogleDriveSettingsAsync(form);
        TempData["Success"] = "Google Drive settings saved and tested.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadLayoutImage(IFormFile? file)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "Select an image." });
        if (!IsAllowedImage(file)) return BadRequest(new { error = "Upload a JPG, PNG, GIF, or WEBP image." });
        return Json(new { url = await _uploads.SaveAsync(file, "layout-images") });
    }

    private async Task<IActionResult> SavePrintLayoutAsync(PrintLayoutFormModel form, string documentType, string formPrefix, string modalId, string logoKey)
    {
        form.DocumentType = documentType;
        ApplyLayoutDefaults(form, documentType);
        if (string.Equals(documentType, "Diagnosis", StringComparison.OrdinalIgnoreCase))
        {
            RelaxDiagnosisLayoutValidation();
        }
        form.LayoutJson = ReadLayoutJson(formPrefix) ?? form.LayoutJson;
        if (form.Logo is { Length: > 0 } && !IsAllowedImage(form.Logo))
        {
            ModelState.AddModelError(logoKey, "Upload a JPG, PNG, GIF, or WEBP image.");
        }

        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .SelectMany(entry => entry.Value!.Errors.Select(error =>
                    string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? entry.Key
                        : $"{entry.Key}: {error.ErrorMessage}"))
                .Distinct()
                .ToArray();

            TempData["Error"] = errors.Length > 0
                ? $"Layout validation failed: {string.Join(" | ", errors)}"
                : "Check the required layout fields.";
            ViewData["OpenModal"] = modalId;
            return View("Index", await BuildPageAsync(documentType, form));
        }

        try
        {
            var logoUrl = await _uploads.SaveAsync(form.Logo, "logos") ?? form.LogoUrl;
            await _repository.UpdatePrintLayoutAsync(form, documentType, logoUrl);
            TempData["Success"] = $"{documentType} layout saved.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Save failed: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<AdminPageViewModel> BuildPageAsync(
        string? formType = null,
        PrintLayoutFormModel? layoutForm = null,
        CreateDoctorAccountViewModel? newDoctor = null,
        PostgresSettingsFormModel? postgresSettings = null,
        GoogleDriveSettingsFormModel? googleDriveSettings = null)
    {
        var dashboard = await _repository.GetDashboardAsync();
        var prescriptionForm = PrintLayoutFormModel.From(dashboard.PrescriptionLayout);
        var diagnosisForm = PrintLayoutFormModel.From(dashboard.DiagnosisLayout);
        if (layoutForm is not null && formType == "Prescription") prescriptionForm = layoutForm;
        if (layoutForm is not null && formType == "Diagnosis") diagnosisForm = layoutForm;

        return new AdminPageViewModel
        {
            Users = await _accounts.GetUsersAsync(),
            ArchivedPatients = await _repository.GetArchivedPatientsAsync(),
            NewDoctor = newDoctor ?? new(),
            PrescriptionLayout = dashboard.PrescriptionLayout,
            DiagnosisLayout = dashboard.DiagnosisLayout,
            PrescriptionLayoutForm = prescriptionForm,
            DiagnosisLayoutForm = diagnosisForm,
            PostgresSettings = postgresSettings ?? new PostgresSettingsFormModel
            {
                ConnectionString = _postgresConnections.ConnectionString ?? string.Empty
            },
            GoogleDriveSettings = googleDriveSettings ?? new GoogleDriveSettingsFormModel
            {
                ApplicationName = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["GoogleDrive:ApplicationName"] ?? "MedRec",
                FolderId = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["GoogleDrive:FolderId"] ?? string.Empty,
                ServiceAccountJson = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["GoogleDrive:ServiceAccountJson"] ?? string.Empty,
                ServiceAccountJsonBase64 = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["GoogleDrive:ServiceAccountJsonBase64"] ?? string.Empty,
                ServiceAccountJsonPath = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["GoogleDrive:ServiceAccountJsonPath"] ?? string.Empty
            },
            DataNotice = dashboard.DataNotice
        };
    }

    private bool TryNormalizeGoogleDriveSettings(GoogleDriveSettingsFormModel form)
    {
        form.ApplicationName = string.IsNullOrWhiteSpace(form.ApplicationName) ? "MedRec" : form.ApplicationName.Trim();
        form.FolderId = form.FolderId.Trim();
        form.ServiceAccountJson = form.ServiceAccountJson.Trim();
        form.ServiceAccountJsonBase64 = form.ServiceAccountJsonBase64.Trim();
        form.ServiceAccountJsonPath = form.ServiceAccountJsonPath.Trim();

        if (!TryValidateModel(form, nameof(AdminPageViewModel.GoogleDriveSettings)))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(form.ServiceAccountJson)
            && string.IsNullOrWhiteSpace(form.ServiceAccountJsonBase64)
            && string.IsNullOrWhiteSpace(form.ServiceAccountJsonPath))
        {
            ModelState.AddModelError("GoogleDriveSettings.ServiceAccountJson", "Enter service account JSON, base64 JSON, or a JSON file path.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(form.ServiceAccountJsonPath) && !System.IO.File.Exists(form.ServiceAccountJsonPath))
        {
            ModelState.AddModelError("GoogleDriveSettings.ServiceAccountJsonPath", "The service account JSON file path was not found.");
            return false;
        }

        return true;
    }

    private static async Task<(bool Success, string Message)> TestGoogleDriveSettingsAsync(GoogleDriveSettingsFormModel form)
    {
        try
        {
            var credentialJson = ResolveGoogleCredentialJson(form);
            using var credentialStream = new MemoryStream(Encoding.UTF8.GetBytes(credentialJson));
            var credential = CredentialFactory
                .FromStream<ServiceAccountCredential>(credentialStream)
                .ToGoogleCredential()
                .CreateScoped(DriveService.Scope.Drive);

            using var drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = form.ApplicationName
            });

            var request = drive.Files.Get(form.FolderId);
            request.Fields = "id,name,mimeType";
            request.SupportsAllDrives = true;
            var folder = await request.ExecuteAsync();
            if (!string.Equals(folder.MimeType, "application/vnd.google-apps.folder", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Google Drive ID is reachable but is not a folder.");
            }

            return (true, $"Google Drive connection OK. Folder: {folder.Name}.");
        }
        catch (Exception ex)
        {
            return (false, $"Google Drive test failed: {ReadableError(ex)}");
        }
    }

    private static string ResolveGoogleCredentialJson(GoogleDriveSettingsFormModel form)
    {
        if (!string.IsNullOrWhiteSpace(form.ServiceAccountJson))
        {
            return form.ServiceAccountJson;
        }

        if (!string.IsNullOrWhiteSpace(form.ServiceAccountJsonBase64))
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(form.ServiceAccountJsonBase64));
        }

        return System.IO.File.ReadAllText(form.ServiceAccountJsonPath);
    }

    private static string ReadableError(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }

    private string? NormalizePostedPostgresSettings(PostgresSettingsFormModel form)
    {
        if (!TryValidateModel(form, nameof(AdminPageViewModel.PostgresSettings)))
        {
            return null;
        }

        try
        {
            form.ConnectionString = PostgresConnectionFactory.NormalizeConnectionString(form.ConnectionString.Trim());
            return form.ConnectionString;
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("PostgresSettings.ConnectionString", $"Connection string is not valid: {ex.Message}");
            return null;
        }
    }

    private static async Task<(bool Success, string Message)> TestPostgresConnectionStringAsync(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Timeout = 10,
                CommandTimeout = 10
            };

            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            string? database = null;
            await using var command = new NpgsqlCommand("select current_database(), version();", connection);
            await using (var reader = await command.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    database = reader.GetString(0);
                }
            }

            var tableCount = await CountMedRecTablesAsync(connection);
            var schemaMessage = tableCount == 0
                ? " No MedRec tables were found. Use Initialize database tables."
                : $" {tableCount} MedRec tables found.";
            var databaseMessage = string.IsNullOrWhiteSpace(database)
                ? "PostgreSQL connection OK."
                : $"PostgreSQL connection OK. Connected to database '{database}'.";

            return (true, $"{databaseMessage}{schemaMessage}");
        }
        catch (Exception ex)
        {
            return (false, $"PostgreSQL test failed: {ex.Message}");
        }
    }

    private static async Task<int> CountMedRecTablesAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT count(*)
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
              AND table_name = ANY (@tables);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tables", new[]
        {
            "users",
            "patients",
            "clinical_records",
            "lab_results",
            "prescriptions",
            "prescription_items",
            "print_layouts",
            "sync_queue"
        });
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static string ResolveSchemaPath(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Database", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Database", fileName)
        };

        return candidates.FirstOrDefault(System.IO.File.Exists) ?? candidates[0];
    }

    private string? ReadLayoutJson(string prefix)
    {
        var encoded = Request.Form[$"{prefix}.LayoutEncoded"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(encoded)) return Request.Form[$"{prefix}.LayoutJson"].FirstOrDefault();
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(encoded)); }
        catch { return null; }
    }

    private void ApplyLayoutDefaults(PrintLayoutFormModel form, string documentType)
    {
        var normalizedDocumentType = medrec.Models.PrintLayout.NormalizeDocumentType(documentType);
        var userName = HttpContext.Session.GetString("UserName")?.Trim();
        var userLicense = HttpContext.Session.GetString("UserLicenseNumber")?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(form.DocumentTitle))
        {
            form.DocumentTitle = normalizedDocumentType == "Diagnosis" ? "Medical Certificate" : "Prescription";
            ModelState.Remove(nameof(form.DocumentTitle));
        }

        if (string.IsNullOrWhiteSpace(form.ClinicName))
        {
            form.ClinicName = "MedRec Clinic";
            ModelState.Remove(nameof(form.ClinicName));
        }

        if (string.IsNullOrWhiteSpace(form.DoctorName))
        {
            form.DoctorName = string.IsNullOrWhiteSpace(userName) ? "Doctor" : userName;
            ModelState.Remove(nameof(form.DoctorName));
        }

        if (string.IsNullOrWhiteSpace(form.SignatoryName))
        {
            form.SignatoryName = form.DoctorName;
            ModelState.Remove(nameof(form.SignatoryName));
        }

        if (string.IsNullOrWhiteSpace(form.LicenseNumber))
        {
            form.LicenseNumber = userLicense;
            ModelState.Remove(nameof(form.LicenseNumber));
        }

        if (string.IsNullOrWhiteSpace(form.ClinicSchedule))
        {
            form.ClinicSchedule = string.Empty;
            ModelState.Remove(nameof(form.ClinicSchedule));
        }

        if (string.IsNullOrWhiteSpace(form.ClinicAddress))
        {
            form.ClinicAddress = string.Empty;
            ModelState.Remove(nameof(form.ClinicAddress));
        }

        if (string.IsNullOrWhiteSpace(form.LogoPosition))
        {
            form.LogoPosition = "Left";
            ModelState.Remove(nameof(form.LogoPosition));
        }

        if (string.IsNullOrWhiteSpace(form.DetailsAlignment))
        {
            form.DetailsAlignment = "Left";
            ModelState.Remove(nameof(form.DetailsAlignment));
        }
    }

    private void RelaxDiagnosisLayoutValidation()
    {
        var fields = new[]
        {
            nameof(PrintLayoutFormModel.DocumentType),
            nameof(PrintLayoutFormModel.DocumentTitle),
            nameof(PrintLayoutFormModel.ClinicName),
            nameof(PrintLayoutFormModel.ClinicSchedule),
            nameof(PrintLayoutFormModel.ClinicAddress),
            nameof(PrintLayoutFormModel.DoctorName),
            nameof(PrintLayoutFormModel.LicenseNumber),
            nameof(PrintLayoutFormModel.LogoPosition),
            nameof(PrintLayoutFormModel.DetailsAlignment),
            nameof(PrintLayoutFormModel.SignatoryName)
        };

        foreach (var field in fields)
        {
            var keys = ModelState.Keys
                .Where(key => key.Equals(field, StringComparison.OrdinalIgnoreCase)
                    || key.EndsWith($".{field}", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keys)
            {
                ModelState.Remove(key);
            }
        }
    }

    private static bool IsAllowedImage(IFormFile file) =>
        new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" }.Contains(Path.GetExtension(file.FileName), StringComparer.OrdinalIgnoreCase);

}


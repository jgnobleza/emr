using medrec.Models;
using medrec.ViewModels;

namespace medrec.Data;

public sealed class EmrRepository
{
    private readonly MedRecStorageOptions _options;
    private readonly PostgresEmrRepository _postgres;
    private readonly SqliteEmrRepository _sqlite;

    public EmrRepository(IConfiguration configuration, PostgresEmrRepository postgres, SqliteEmrRepository sqlite)
    {
        _options = configuration.GetSection("MedRec").Get<MedRecStorageOptions>() ?? new MedRecStorageOptions();
        _postgres = postgres;
        _sqlite = sqlite;
    }

    private bool UseLocal => _options.UseLocalStorage;

    public Task<DashboardViewModel> GetDashboardAsync() => UseLocal ? _sqlite.GetDashboardAsync() : _postgres.GetDashboardAsync();

    public Task<OfflineSyncSnapshot> GetOfflineSnapshotAsync(AppUser user) => UseLocal ? _sqlite.GetOfflineSnapshotAsync(user) : _postgres.GetOfflineSnapshotAsync(user);

    public Task<int> CreatePatientAsync(PatientFormModel form, string? photoUrl) => UseLocal ? _sqlite.CreatePatientAsync(form, photoUrl) : _postgres.CreatePatientAsync(form, photoUrl);

    public Task UpdatePatientAsync(PatientEditFormModel form, string? photoUrl) => UseLocal ? _sqlite.UpdatePatientAsync(form, photoUrl) : _postgres.UpdatePatientAsync(form, photoUrl);

    public Task ArchivePatientAsync(int id) => UseLocal ? _sqlite.ArchivePatientAsync(id) : _postgres.ArchivePatientAsync(id);

    public Task<IReadOnlyList<Patient>> GetArchivedPatientsAsync() => UseLocal ? _sqlite.GetArchivedPatientsAsync() : _postgres.GetArchivedPatientsAsync();

    public Task UnarchivePatientAsync(int id) => UseLocal ? _sqlite.UnarchivePatientAsync(id) : _postgres.UnarchivePatientAsync(id);

    public Task<int> CreateClinicalRecordAsync(RecordFormModel form) => UseLocal ? _sqlite.CreateClinicalRecordAsync(form) : _postgres.CreateClinicalRecordAsync(form);

    public Task UpdateDiagnosisAsync(DiagnosisFormModel form) => UseLocal ? _sqlite.UpdateDiagnosisAsync(form) : _postgres.UpdateDiagnosisAsync(form);

    public Task UpdateCheckupAsync(CheckupEditFormModel form) => UseLocal ? _sqlite.UpdateCheckupAsync(form) : _postgres.UpdateCheckupAsync(form);

    public Task<int> CreateLabResultAsync(LabResultFormModel form, string fileUrl) => UseLocal ? _sqlite.CreateLabResultAsync(form, fileUrl) : _postgres.CreateLabResultAsync(form, fileUrl);

    public Task AttachLabToCheckUpAsync(LabAttachmentFormModel form) => UseLocal ? _sqlite.AttachLabToCheckUpAsync(form) : _postgres.AttachLabToCheckUpAsync(form);

    public Task<int> CreatePrescriptionAsync(PrescriptionFormModel form) => UseLocal ? _sqlite.CreatePrescriptionAsync(form) : _postgres.CreatePrescriptionAsync(form);

    public Task RegisterPrescriptionPrintAsync(int id) => UseLocal ? _sqlite.RegisterPrescriptionPrintAsync(id) : _postgres.RegisterPrescriptionPrintAsync(id);

    public Task UpdatePrintLayoutAsync(PrintLayoutFormModel form, string documentType, string? logoUrl) => UseLocal ? _sqlite.UpdatePrintLayoutAsync(form, documentType, logoUrl) : _postgres.UpdatePrintLayoutAsync(form, documentType, logoUrl);

    public Task<int> ManualSyncAsync() => UseLocal ? _sqlite.ManualSyncAsync() : _postgres.ManualSyncAsync();

    public Task<string> CleanupRecordDataAsync() => UseLocal ? _sqlite.CleanupRecordDataAsync() : _postgres.CleanupRecordDataAsync();
}

using System.Text.Json;

namespace medrec.Models;

public sealed class OfflineSyncSnapshot
{
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public AppUser User { get; set; } = new();
    public IReadOnlyList<Patient> Patients { get; set; } = [];
    public IReadOnlyList<ClinicalRecord> Records { get; set; } = [];
    public IReadOnlyList<LabResult> LabResults { get; set; } = [];
    public IReadOnlyList<Prescription> Prescriptions { get; set; } = [];
    public PrintLayout PrescriptionLayout { get; set; } = PrintLayout.Default("Prescription");
    public PrintLayout DiagnosisLayout { get; set; } = PrintLayout.Default("Diagnosis");
}

public sealed class OfflineSyncBatch
{
    public string DeviceId { get; set; } = string.Empty;
    public List<OfflineSyncOperation> Operations { get; set; } = [];
}

public sealed class OfflineSyncOperation
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string EntityUid { get; set; } = string.Empty;
    public DateTime? BaseUpdatedAt { get; set; }
    public JsonElement Payload { get; set; }
}

public sealed class OfflineSyncResult
{
    public List<string> AcceptedOperationIds { get; set; } = [];
    public List<OfflineSyncConflict> Conflicts { get; set; } = [];
    public OfflineSyncSnapshot? Snapshot { get; set; }
}

public sealed class OfflineSyncConflict
{
    public string OperationId { get; set; } = string.Empty;
    public string EntityUid { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class OfflineRecordPayload
{
    public string ClientUid { get; set; } = string.Empty;
    public string PatientClientUid { get; set; } = string.Empty;
    public DateTime VisitDate { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public string BloodPressure { get; set; } = string.Empty;
    public string FetalHeartRate { get; set; } = string.Empty;
    public decimal? TemperatureC { get; set; }
    public string ChiefComplaint { get; set; } = string.Empty;
    public string Diagnosis { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class OfflinePrescriptionPayload
{
    public string ClientUid { get; set; } = string.Empty;
    public string PatientClientUid { get; set; } = string.Empty;
    public string? RecordClientUid { get; set; }
    public DateTime IssuedAt { get; set; }
    public string Instructions { get; set; } = string.Empty;
    public List<PrescriptionItem> Items { get; set; } = [];
}

public sealed class OfflineLabPayload
{
    public string ClientUid { get; set; } = string.Empty;
    public string PatientClientUid { get; set; } = string.Empty;
    public string? RecordClientUid { get; set; }
    public string TestName { get; set; } = string.Empty;
    public DateTime RequestedDate { get; set; }
    public DateTime ResultDate { get; set; }
    public string Status { get; set; } = "Uploaded";
    public string FileUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileContentType { get; set; } = "application/pdf";
    public string FileBase64 { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class OfflineProfilePayload
{
    public string FullName { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string SignatureFileName { get; set; } = string.Empty;
    public string SignatureContentType { get; set; } = string.Empty;
    public string SignatureBase64 { get; set; } = string.Empty;
}

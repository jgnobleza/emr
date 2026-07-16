namespace medrec.Models;

public sealed class ClinicalRecord
{
    public int Id { get; set; }
    public string ClientUid { get; set; } = string.Empty;
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientAddress { get; set; } = string.Empty;
    public int PatientAge { get; set; }
    public string PatientSex { get; set; } = string.Empty;
    public DateTime VisitDate { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public string BloodPressure { get; set; } = string.Empty;
    public string FetalHeartRate { get; set; } = string.Empty;
    public decimal? TemperatureC { get; set; }
    public string ChiefComplaint { get; set; } = string.Empty;
    public string Diagnosis { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string SyncStatus { get; set; } = "Synced";
    public DateTime UpdatedAt { get; set; }
}

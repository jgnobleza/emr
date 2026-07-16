namespace medrec.Models;

public sealed class Prescription
{
    public int Id { get; set; }
    public string ClientUid { get; set; } = string.Empty;
    public int PatientId { get; set; }
    public int? ClinicalRecordId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientAddress { get; set; } = string.Empty;
    public int PatientAge { get; set; }
    public string PatientSex { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public string Medication { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string Prescriber { get; set; } = string.Empty;
    public IReadOnlyList<PrescriptionItem> Items { get; set; } = [];
    public DateTime UpdatedAt { get; set; }
}

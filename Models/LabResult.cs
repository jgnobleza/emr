namespace medrec.Models;

public sealed class LabResult
{
    public int Id { get; set; }
    public string ClientUid { get; set; } = string.Empty;
    public int PatientId { get; set; }
    public int? ClinicalRecordId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string TestName { get; set; } = string.Empty;
    public DateTime RequestedDate { get; set; }
    public DateTime ResultDate { get; set; }
    public DateTime? CheckUpDate { get; set; }
    public string CheckUpComplaint { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

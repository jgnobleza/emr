namespace MedRec.Mobile.Models;

public sealed class MobileCheckup
{
    public string ClientUid { get; set; } = Guid.NewGuid().ToString("N");
    public string PatientClientUid { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public DateTime VisitDate { get; set; } = DateTime.Now;
    public string ChiefComplaint { get; set; } = string.Empty;
    public string Diagnosis { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string SyncStatus { get; set; } = "Pending";
}

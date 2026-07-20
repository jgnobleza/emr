namespace MedRec.Mobile.Models;

public sealed class MobilePatient
{
    public string ClientUid { get; set; } = Guid.NewGuid().ToString("N");
    public string FullName { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Sex { get; set; } = "Female";
    public string ContactNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string SyncStatus { get; set; } = "Pending";
}

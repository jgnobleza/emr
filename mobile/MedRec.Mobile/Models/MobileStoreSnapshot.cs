namespace MedRec.Mobile.Models;

public sealed class MobileStoreSnapshot
{
    public List<MobilePatient> Patients { get; set; } = [];
    public List<MobileCheckup> Checkups { get; set; } = [];
    public DateTime? LastSyncAt { get; set; }
}

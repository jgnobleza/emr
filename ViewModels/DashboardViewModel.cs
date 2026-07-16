using medrec.Models;

namespace medrec.ViewModels;

public sealed class DashboardViewModel
{
    public IReadOnlyList<Patient> Patients { get; set; } = [];
    public IReadOnlyList<ClinicalRecord> RecentRecords { get; set; } = [];
    public IReadOnlyList<LabResult> LabResults { get; set; } = [];
    public IReadOnlyList<Prescription> Prescriptions { get; set; } = [];
    public IReadOnlyList<SyncItem> SyncQueue { get; set; } = [];
    public PrintLayout PrescriptionLayout { get; set; } = PrintLayout.Default("Prescription");
    public PrintLayout DiagnosisLayout { get; set; } = PrintLayout.Default("Diagnosis");
    public bool DatabaseConfigured { get; set; }
    public string? DataNotice { get; set; }
    public string LastSyncLabel { get; set; } = "Today, 6:00 AM";

    public int PendingSyncCount => SyncQueue.Count(item => item.Status != "Synced");
    public int TodayVisitCount => RecentRecords.Count(record => record.VisitDate.Date == DateTime.Today);
    public int ActivePrescriptionCount => Prescriptions.Count;
}

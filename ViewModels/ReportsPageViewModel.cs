using medrec.Models;

namespace medrec.ViewModels;

public sealed class ReportsPageViewModel
{
    public IReadOnlyList<ClinicalRecord> RecentRecords { get; set; } = [];
    public IReadOnlyList<LabResult> LabResults { get; set; } = [];
    public IReadOnlyList<Prescription> Prescriptions { get; set; } = [];
    public PrintLayout PrescriptionLayout { get; set; } = PrintLayout.Default("Prescription");
    public PrintLayout DiagnosisLayout { get; set; } = PrintLayout.Default("Diagnosis");
    public PrintLayoutFormModel PrescriptionLayoutForm { get; set; } = new();
    public PrintLayoutFormModel DiagnosisLayoutForm { get; set; } = new() { DocumentType = "Diagnosis", DocumentTitle = "Medical Certificate" };
    public string? DataNotice { get; set; }
}

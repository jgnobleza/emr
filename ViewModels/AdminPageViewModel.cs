using medrec.Models;

namespace medrec.ViewModels;

public sealed class AdminPageViewModel
{
    public IReadOnlyList<AppUser> Users { get; set; } = [];
    public IReadOnlyList<Patient> ArchivedPatients { get; set; } = [];
    public CreateDoctorAccountViewModel NewDoctor { get; set; } = new();
    public PrintLayout PrescriptionLayout { get; set; } = PrintLayout.Default("Prescription");
    public PrintLayout DiagnosisLayout { get; set; } = PrintLayout.Default("Diagnosis");
    public PrintLayoutFormModel PrescriptionLayoutForm { get; set; } = new();
    public PrintLayoutFormModel DiagnosisLayoutForm { get; set; } = new() { DocumentType = "Diagnosis" };
    public string? DataNotice { get; set; }
}

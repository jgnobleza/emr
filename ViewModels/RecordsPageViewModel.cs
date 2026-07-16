using medrec.Models;

namespace medrec.ViewModels;

public sealed class RecordsPageViewModel
{
    public IReadOnlyList<Patient> Patients { get; set; } = [];
    public IReadOnlyList<ClinicalRecord> Records { get; set; } = [];
    public IReadOnlyList<LabResult> Labs { get; set; } = [];
    public Patient? SelectedPatient { get; set; }
    public ClinicalRecord? SelectedRecord { get; set; }
    public IReadOnlyList<ClinicalRecord> PatientRecords { get; set; } = [];
    public IReadOnlyList<LabResult> PatientLabs { get; set; } = [];
    public PrintLayout DiagnosisLayout { get; set; } = PrintLayout.Default("Diagnosis");
    public RecordFormModel NewRecord { get; set; } = new();
    public LabResultFormModel NewLab { get; set; } = new();
    public LabAttachmentFormModel LabAttachment { get; set; } = new();
    public DiagnosisFormModel Diagnosis { get; set; } = new();
    public CheckupEditFormModel CheckupEdit { get; set; } = new();
    public IReadOnlyList<string> ComplaintSuggestions { get; set; } = [];
    public string? DataNotice { get; set; }
}

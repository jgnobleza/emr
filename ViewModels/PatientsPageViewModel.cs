using medrec.Models;

namespace medrec.ViewModels;

public sealed class PatientsPageViewModel
{
    public IReadOnlyList<Patient> Patients { get; set; } = [];
    public PatientFormModel NewPatient { get; set; } = new();
    public PatientEditFormModel EditPatient { get; set; } = new();
    public string? DataNotice { get; set; }
}

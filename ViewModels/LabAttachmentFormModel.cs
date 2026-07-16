using System.ComponentModel.DataAnnotations;

namespace medrec.ViewModels;

public sealed class LabAttachmentFormModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Select a lab.")]
    public int LabId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Select a patient.")]
    public int PatientId { get; set; }

    [Required(ErrorMessage = "Select check up.")]
    public int? ClinicalRecordId { get; set; }

    [Required]
    public DateTime RequestedDate { get; set; } = DateTime.Now;
}

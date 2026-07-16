using System.ComponentModel.DataAnnotations;

namespace medrec.ViewModels;

public sealed class RecordFormModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Select a patient.")]
    public int PatientId { get; set; }

    [Required]
    public DateTime VisitDate { get; set; } = DateTime.Now;

    [Required, StringLength(255)]
    public string ChiefComplaint { get; set; } = string.Empty;

    [Range(1, 300)]
    public decimal? HeightCm { get; set; }

    [Range(1, 500)]
    public decimal? WeightKg { get; set; }

    [StringLength(40)]
    public string BloodPressure { get; set; } = string.Empty;

    [StringLength(40)]
    public string FetalHeartRate { get; set; } = string.Empty;

    [Range(25, 45)]
    public decimal? TemperatureC { get; set; }

    [StringLength(255)]
    public string Diagnosis { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    [StringLength(160)]
    public string DoctorName { get; set; } = string.Empty;
}

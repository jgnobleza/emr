using System.ComponentModel.DataAnnotations;

namespace medrec.ViewModels;

public sealed class DiagnosisFormModel
{
    [Range(1, int.MaxValue)]
    public int RecordId { get; set; }

    [Required, StringLength(255)]
    public string Diagnosis { get; set; } = string.Empty;

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

    [Required]
    public string Notes { get; set; } = string.Empty;
}

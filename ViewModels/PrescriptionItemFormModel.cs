using System.ComponentModel.DataAnnotations;

namespace medrec.ViewModels;

public sealed class PrescriptionItemFormModel
{
    [StringLength(180)]
    public string Medication { get; set; } = string.Empty;

    [StringLength(120)]
    public string Dosage { get; set; } = string.Empty;

    [StringLength(120)]
    public string Frequency { get; set; } = string.Empty;

    [StringLength(120)]
    public string Duration { get; set; } = string.Empty;

    public bool HasAnyValue =>
        !string.IsNullOrWhiteSpace(Medication)
        || !string.IsNullOrWhiteSpace(Dosage)
        || !string.IsNullOrWhiteSpace(Frequency)
        || !string.IsNullOrWhiteSpace(Duration);

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Medication)
        && !string.IsNullOrWhiteSpace(Dosage)
        && !string.IsNullOrWhiteSpace(Frequency)
        && !string.IsNullOrWhiteSpace(Duration);
}

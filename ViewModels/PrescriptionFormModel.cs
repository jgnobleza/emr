using System.ComponentModel.DataAnnotations;

namespace medrec.ViewModels;

public sealed class PrescriptionFormModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Select a patient.")]
    public int PatientId { get; set; }

    public int? ClinicalRecordId { get; set; }

    [StringLength(180)]
    public string Medication { get; set; } = string.Empty;

    [StringLength(120)]
    public string Dosage { get; set; } = string.Empty;

    [StringLength(120)]
    public string Frequency { get; set; } = string.Empty;

    [StringLength(120)]
    public string Duration { get; set; } = string.Empty;

    public List<PrescriptionItemFormModel> Items { get; set; } = [new()];

    public string? Instructions { get; set; }

    [StringLength(160)]
    public string Prescriber { get; set; } = string.Empty;

    public IReadOnlyList<PrescriptionItemFormModel> NormalizedItems()
    {
        var items = Items
            .Where(item => item.IsComplete)
            .Select(item => new PrescriptionItemFormModel
            {
                Medication = item.Medication.Trim(),
                Dosage = item.Dosage.Trim(),
                Frequency = item.Frequency.Trim(),
                Duration = item.Duration.Trim()
            })
            .ToList();

        if (items.Count == 0
            && !string.IsNullOrWhiteSpace(Medication)
            && !string.IsNullOrWhiteSpace(Dosage)
            && !string.IsNullOrWhiteSpace(Frequency)
            && !string.IsNullOrWhiteSpace(Duration))
        {
            items.Add(new PrescriptionItemFormModel
            {
                Medication = Medication.Trim(),
                Dosage = Dosage.Trim(),
                Frequency = Frequency.Trim(),
                Duration = Duration.Trim()
            });
        }

        return items;
    }

    public bool HasIncompleteDrugRows()
    {
        return Items.Any(item => item.HasAnyValue && !item.IsComplete);
    }
}

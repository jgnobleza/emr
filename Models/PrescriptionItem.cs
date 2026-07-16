namespace medrec.Models;

public sealed class PrescriptionItem
{
    public int Id { get; set; }
    public int PrescriptionId { get; set; }
    public string Medication { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

using medrec.Models;

namespace medrec.ViewModels;

public sealed class PrintDocumentViewModel
{
    public string? ElementId { get; set; }
    public string CssClass { get; set; } = string.Empty;
    public PrintLayout Layout { get; set; } = PrintLayout.Default();
    public Prescription? Prescription { get; set; }
    public ClinicalRecord? Record { get; set; }
    public Patient? Patient { get; set; }
}

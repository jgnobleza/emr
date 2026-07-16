using medrec.Models;

namespace medrec.ViewModels;

public sealed class PrintLayoutEditorViewModel
{
    public string ModalId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Controller { get; set; } = "Reports";
    public string Prefix { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public PrintLayout Layout { get; set; } = PrintLayout.Default();
    public PrintLayoutFormModel Form { get; set; } = new();
}

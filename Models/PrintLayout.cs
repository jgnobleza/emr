namespace medrec.Models;

public sealed class PrintLayout
{
    public int Id { get; set; } = 1;
    public string DocumentType { get; set; } = "Prescription";
    public string DocumentTitle { get; set; } = "Prescription";
    public string ClinicName { get; set; } = "MedRec Clinic";
    public string DoctorName { get; set; } = "Dr. Cruz";
    public string LicenseNumber { get; set; } = string.Empty;
    public string ClinicSchedule { get; set; } = string.Empty;
    public string ClinicAddress { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string LogoPosition { get; set; } = "Left";
    public string DetailsAlignment { get; set; } = "Left";
    public string SignatoryName { get; set; } = "Dr. Cruz";
    public string SignatoryTitle { get; set; } = "OB-Gyne";
    public IReadOnlyList<PrintLayoutBlock> Blocks { get; set; } = DefaultBlocks("Prescription");
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public string LogoPositionClass => CssValue(LogoPosition);
    public string DetailsAlignmentClass => CssValue(DetailsAlignment);

    public static PrintLayout Default(string documentType = "Prescription")
    {
        return new PrintLayout
        {
            Id = LayoutId(documentType),
            DocumentType = NormalizeDocumentType(documentType),
            DocumentTitle = NormalizeDocumentType(documentType) == "Diagnosis" ? "Medical Certificate" : "Prescription",
            Blocks = DefaultBlocks(documentType)
        };
    }

    public static int LayoutId(string documentType)
    {
        return NormalizeDocumentType(documentType) == "Diagnosis" ? 2 : 1;
    }

    public static string NormalizeDocumentType(string documentType)
    {
        return documentType.Equals("Diagnosis", StringComparison.OrdinalIgnoreCase)
            ? "Diagnosis"
            : "Prescription";
    }

    public static IReadOnlyList<PrintLayoutBlock> DefaultBlocks(string documentType)
    {
        var isDiagnosis = NormalizeDocumentType(documentType) == "Diagnosis";

        return
        [
            new() { Key = "logo", Type = "Field", Label = "Logo", X = 14, Y = 12, Width = 28, Height = 24, FontSize = 11, IsVisible = true },
            new() { Key = "clinic", Type = "Field", Label = "Clinic Details", X = 46, Y = 12, Width = 150, Height = 28, FontSize = 12, IsVisible = true },
            new() { Key = "title", Type = "Field", Label = "Title", X = 14, Y = 46, Width = 182, Height = 16, FontSize = 18, FontWeight = "Bold", IsVisible = true },
            new() { Key = "patient", Type = "Field", Label = "Patient Details", X = 14, Y = 68, Width = 182, Height = 30, FontSize = 11, IsVisible = true },
            new() { Key = "body", Type = "Field", Label = isDiagnosis ? "Diagnosis" : "Prescription Details", X = 14, Y = 106, Width = 182, Height = isDiagnosis ? 70 : 58, FontSize = 12, IsVisible = true },
            new() { Key = "notes", Type = "Field", Label = isDiagnosis ? "Notes" : "Instructions", X = 14, Y = isDiagnosis ? 184 : 172, Width = 182, Height = 46, FontSize = 11, IsVisible = true },
            new() { Key = "signatureImage", Type = "Field", Label = "Doctor Signature Image", X = 133, Y = 220, Width = 48, Height = 18, FontSize = 11, TextAlign = "Center", IsVisible = true },
            new() { Key = "signature", Type = "Field", Label = "Signature Details", X = 118, Y = 240, Width = 78, Height = 28, FontSize = 11, TextAlign = "Center", IsVisible = true }
        ];
    }

    private static string CssValue(string value)
    {
        return value.Equals("Center", StringComparison.OrdinalIgnoreCase)
            ? "center"
            : value.Equals("Right", StringComparison.OrdinalIgnoreCase)
                ? "right"
                : "left";
    }
}

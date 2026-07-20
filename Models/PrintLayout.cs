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
        if (!isDiagnosis)
        {
            return
            [
                new() { Key = "patient", Type = "Field", Label = "Patient Details", X = 14, Y = 49.94m, Width = 182, Height = 19.89m, FontSize = 17, IsVisible = true },
                new() { Key = "body", Type = "Field", Label = "Prescription Details", X = 14.41m, Y = 94.32m, Width = 182, Height = 136.17m, FontSize = 18, IsVisible = true },
                new() { Key = "notes", Type = "Field", Label = "Instructions", X = 14.82m, Y = 232.74m, Width = 182, Height = 25.48m, FontSize = 18, IsVisible = true },
                new() { Key = "text_1781865696064", Type = "Text", Label = "Text 1", Text = "FILIPINAS B. ILAGAN, M.D., FPOGS, FPSUOG", X = 12.1m, Y = 4.1m, Width = 184.66m, Height = 10.92m, FontSize = 26, TextAlign = "Center", FontWeight = "Bold", IsVisible = true },
                new() { Key = "text_1781865739116", Type = "Text", Label = "Text 2", Text = "Obstetrician-Gynecologist/OB-GYNE Ultrasound Subspecialist", X = 12.55m, Y = 15.63m, Width = 185, Height = 8.31m, FontSize = 17, TextAlign = "Center", IsVisible = true },
                new() { Key = "line_1783262731599", Type = "Line", Label = "Line 1", X = 0.41m, Y = 43.17m, Width = 209.59m, Height = 4, FontSize = 12, LineWidth = 2, IsVisible = true },
                new() { Key = "line_1783262757063", Type = "Line", Label = "Line 2", X = 0, Y = 72.31m, Width = 210, Height = 4, FontSize = 12, LineWidth = 2, IsVisible = true },
                new() { Key = "text_1783262810606", Type = "Text", Label = "Text 3", Text = "Rx", X = 13.74m, Y = 76.41m, Width = 26.65m, Height = 16.66m, FontSize = 28, FontWeight = "Bold", IsVisible = true },
                new() { Key = "text_1783267140626", Type = "Text", Label = "Text 4", Text = "ACE Medical Center Baliuag\nGround Floor Women's Health Center\nWednesday and Friday : 2pm to 5pm", X = 12.1m, Y = 25.52m, Width = 61.95m, Height = 15.02m, FontSize = 13, IsVisible = true },
                new() { Key = "text_1783267209717", Type = "Text", Label = "Text 5", Text = "OB-GYNE & Children's Clinic - Ground Floor JJSS Commercial Stall\n781 B.S. Aquino Avenue, Bagong Nayon, Baliwag, Bulacan\nAcross New Frontier Subdivision\nMon-Wed-Fri : 8am to 1pm", X = 89.25m, Y = 25.11m, Width = 97.24m, Height = 14.61m, FontSize = 12, IsVisible = true },
                new() { Key = "signatureImage", Type = "Field", Label = "Doctor Signature Image", X = 141.21m, Y = 257.35m, Width = 48, Height = 18, FontSize = 11, TextAlign = "Center", IsVisible = true },
                new() { Key = "signature", Type = "Field", Label = "Signature Details", X = 126.21m, Y = 274.06m, Width = 75.58m, Height = 18.83m, FontSize = 11, TextAlign = "Center", IsVisible = true }
            ];
        }

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

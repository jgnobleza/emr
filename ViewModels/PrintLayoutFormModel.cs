using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using medrec.Models;
using Microsoft.AspNetCore.Http;

namespace medrec.ViewModels;

public sealed class PrintLayoutFormModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Required, StringLength(40)]
    public string DocumentType { get; set; } = "Prescription";

    [Required, StringLength(120)]
    [Display(Name = "Document Title")]
    public string DocumentTitle { get; set; } = "Prescription";

    [Required, StringLength(180)]
    [Display(Name = "Clinic Name")]
    public string ClinicName { get; set; } = "MedRec Clinic";

    [Required, StringLength(160)]
    [Display(Name = "Doctor Name")]
    public string DoctorName { get; set; } = "Dr. Cruz";

    [StringLength(80)]
    [Display(Name = "License No.")]
    public string LicenseNumber { get; set; } = string.Empty;

    [StringLength(255)]
    [Display(Name = "Clinic Schedule")]
    public string ClinicSchedule { get; set; } = string.Empty;

    [StringLength(255)]
    [Display(Name = "Clinic Address")]
    public string ClinicAddress { get; set; } = string.Empty;

    [Display(Name = "Logo")]
    public IFormFile? Logo { get; set; }

    [StringLength(500)]
    [Display(Name = "Logo URL")]
    public string? LogoUrl { get; set; }

    [Required, StringLength(20)]
    [Display(Name = "Logo Position")]
    public string LogoPosition { get; set; } = "Left";

    [Required, StringLength(20)]
    [Display(Name = "Details Alignment")]
    public string DetailsAlignment { get; set; } = "Left";

    [Required, StringLength(160)]
    [Display(Name = "Signatory")]
    public string SignatoryName { get; set; } = "Dr. Cruz";

    [StringLength(120)]
    [Display(Name = "Signatory Title")]
    public string SignatoryTitle { get; set; } = "OB-Gyne";

    public string LayoutJson { get; set; } = string.Empty;

    public string LayoutEncoded { get; set; } = string.Empty;

    public IReadOnlyList<PrintLayoutBlock> Blocks => ParseBlocks(LayoutJson, DocumentType);

    public static PrintLayoutFormModel From(PrintLayout layout)
    {
        return new PrintLayoutFormModel
        {
            DocumentType = layout.DocumentType,
            DocumentTitle = layout.DocumentTitle,
            ClinicName = layout.ClinicName,
            DoctorName = layout.DoctorName,
            LicenseNumber = layout.LicenseNumber,
            ClinicSchedule = layout.ClinicSchedule,
            ClinicAddress = layout.ClinicAddress,
            LogoUrl = layout.LogoUrl,
            LogoPosition = layout.LogoPosition,
            DetailsAlignment = layout.DetailsAlignment,
            SignatoryName = layout.SignatoryName,
            SignatoryTitle = layout.SignatoryTitle,
            LayoutJson = SerializeBlocks(layout.Blocks)
        };
    }

    public static string SerializeBlocks(IReadOnlyList<PrintLayoutBlock> blocks)
    {
        return JsonSerializer.Serialize(blocks, JsonOptions);
    }

    public static IReadOnlyList<PrintLayoutBlock> ParseBlocks(string? json, string documentType)
    {
        var defaults = PrintLayout.DefaultBlocks(documentType);

        if (string.IsNullOrWhiteSpace(json))
        {
            return defaults;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<PrintLayoutBlock>>(json, JsonOptions) ?? [];
            var defaultByKey = defaults.ToDictionary(block => block.Key, StringComparer.OrdinalIgnoreCase);
            var blocks = new List<PrintLayoutBlock>();

            foreach (var block in parsed.Where(block => !string.IsNullOrWhiteSpace(block.Key)))
            {
                block.Key = block.Key.Trim();

                if (defaultByKey.TryGetValue(block.Key, out var defaultBlock))
                {
                    block.Key = defaultBlock.Key;
                    block.Label = defaultBlock.Label;
                    block.Type = "Field";
                }

                blocks.Add(NormalizeBlock(block));
            }

            return blocks;
        }
        catch
        {
            return defaults;
        }
    }

    private static PrintLayoutBlock NormalizeBlock(PrintLayoutBlock block)
    {
        block.Type = NormalizeType(block.Type);
        block.Label = string.IsNullOrWhiteSpace(block.Label)
            ? block.Type
            : block.Label.Trim();
        block.Text = block.Text?.Trim() ?? string.Empty;
        block.ImageUrl = string.IsNullOrWhiteSpace(block.ImageUrl) ? null : block.ImageUrl.Trim();
        block.X = Clamp(block.X, 0, 205);
        block.Y = Clamp(block.Y, 0, 292);
        block.Width = Clamp(block.Width, block.Type == "Line" ? 4 : 8, 210 - block.X);
        block.Height = Clamp(block.Height, block.Type == "Line" ? 2 : 8, 297 - block.Y);
        block.FontSize = Math.Clamp(block.FontSize, 8, 28);
        block.LineWidth = Math.Clamp(block.LineWidth, 1, 12);
        block.TextAlign = NormalizeAlignment(block.TextAlign);
        block.FontWeight = block.FontWeight.Equals("Bold", StringComparison.OrdinalIgnoreCase) ? "Bold" : "Normal";
        return block;
    }

    private static string NormalizeType(string? value)
    {
        if (value?.Equals("Text", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Text";
        }

        if (value?.Equals("Line", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Line";
        }

        if (value?.Equals("Image", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Image";
        }

        return "Field";
    }

    private static string NormalizeAlignment(string? value)
    {
        if (value?.Equals("Center", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Center";
        }

        if (value?.Equals("Right", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Right";
        }

        return "Left";
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Min(Math.Max(value, min), max);
    }
}

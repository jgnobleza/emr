using System.ComponentModel.DataAnnotations;

namespace medrec.ViewModels;

public sealed class GoogleDriveSettingsFormModel
{
    [Display(Name = "Application name")]
    public string ApplicationName { get; set; } = "MedRec";

    [Display(Name = "Google Drive folder ID")]
    [Required(ErrorMessage = "Enter the Google Drive folder ID.")]
    public string FolderId { get; set; } = string.Empty;

    [Display(Name = "Service account JSON")]
    public string ServiceAccountJson { get; set; } = string.Empty;

    [Display(Name = "Service account JSON base64")]
    public string ServiceAccountJsonBase64 { get; set; } = string.Empty;

    [Display(Name = "Service account JSON file path")]
    public string ServiceAccountJsonPath { get; set; } = string.Empty;
}

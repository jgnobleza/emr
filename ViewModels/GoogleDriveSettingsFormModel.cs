using System.ComponentModel.DataAnnotations;

namespace medrec.ViewModels;

public sealed class GoogleDriveSettingsFormModel
{
    [Display(Name = "Application name")]
    public string ApplicationName { get; set; } = "MedRec";

    [Display(Name = "Authentication mode")]
    public string AuthMode { get; set; } = "ServiceAccount";

    [Display(Name = "Google Drive folder ID")]
    [Required(ErrorMessage = "Enter the Google Drive folder ID.")]
    public string FolderId { get; set; } = string.Empty;

    [Display(Name = "Service account JSON")]
    public string ServiceAccountJson { get; set; } = string.Empty;

    [Display(Name = "Service account JSON base64")]
    public string ServiceAccountJsonBase64 { get; set; } = string.Empty;

    [Display(Name = "Service account JSON file path")]
    public string ServiceAccountJsonPath { get; set; } = string.Empty;

    [Display(Name = "OAuth client JSON")]
    public string OAuthClientJson { get; set; } = string.Empty;

    [Display(Name = "OAuth client JSON base64")]
    public string OAuthClientJsonBase64 { get; set; } = string.Empty;

    [Display(Name = "OAuth client JSON file path")]
    public string OAuthClientJsonPath { get; set; } = string.Empty;

    public bool UseOAuth => AuthMode.Equals("OAuth", StringComparison.OrdinalIgnoreCase);
}

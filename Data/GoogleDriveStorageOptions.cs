namespace medrec.Data;

public sealed class GoogleDriveStorageOptions
{
    public string ApplicationName { get; set; } = "MedRec";
    public string FolderId { get; set; } = string.Empty;
    public string ServiceAccountJson { get; set; } = string.Empty;
    public string ServiceAccountJsonBase64 { get; set; } = string.Empty;
    public string ServiceAccountJsonPath { get; set; } = string.Empty;
}

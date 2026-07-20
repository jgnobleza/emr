namespace medrec.Data;

public sealed class MedRecStorageOptions
{
    public string StorageMode { get; set; } = "Cloud";
    public string LocalDataPath { get; set; } = string.Empty;
    public string FileStorageProvider { get; set; } = "Local";

    public bool UseLocalStorage => StorageMode.Equals("Local", StringComparison.OrdinalIgnoreCase);
    public bool UseGoogleDriveStorage => FileStorageProvider.Equals("GoogleDrive", StringComparison.OrdinalIgnoreCase);
}

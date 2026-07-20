namespace MedRec.Mobile.Models;

public sealed class MobileSyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime? SyncedAt { get; set; }
}

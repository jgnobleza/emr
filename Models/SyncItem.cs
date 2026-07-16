namespace medrec.Models;

public sealed class SyncItem
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public DateTime? SyncedAt { get; set; }
    public string Status { get; set; } = string.Empty;
}

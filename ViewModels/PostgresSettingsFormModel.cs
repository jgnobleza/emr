using System.ComponentModel.DataAnnotations;

namespace medrec.ViewModels;

public sealed class PostgresSettingsFormModel
{
    [Display(Name = "Render PostgreSQL connection string")]
    [Required(ErrorMessage = "Paste the Render PostgreSQL URL or Npgsql connection string.")]
    public string ConnectionString { get; set; } = string.Empty;
}

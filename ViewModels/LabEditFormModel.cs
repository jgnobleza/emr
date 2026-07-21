using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace medrec.ViewModels;

public sealed class LabEditFormModel
{
    [Range(1, int.MaxValue)]
    public int LabId { get; set; }

    [Range(1, int.MaxValue)]
    public int PatientId { get; set; }

    [Required(ErrorMessage = "Select requested check up.")]
    public int? ClinicalRecordId { get; set; }

    [Required, StringLength(180)]
    public string TestName { get; set; } = string.Empty;

    [Required]
    public DateTime RequestedDate { get; set; } = DateTime.Now;

    [Required]
    public DateTime ResultDate { get; set; } = DateTime.Now;

    [StringLength(500)]
    public string? FileUrl { get; set; }

    public IFormFile? File { get; set; }

    public string? Notes { get; set; }
}

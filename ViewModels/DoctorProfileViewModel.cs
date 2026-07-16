using System.ComponentModel.DataAnnotations;

namespace medrec.ViewModels;

public sealed class DoctorProfileViewModel
{
    [Required, StringLength(160)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required, StringLength(160)]
    public string Specialty { get; set; } = string.Empty;

    [Required, StringLength(80)]
    [Display(Name = "License number")]
    public string LicenseNumber { get; set; } = string.Empty;

    [StringLength(80)]
    [Display(Name = "Contact number")]
    public string ContactNumber { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string? SignatureUrl { get; set; }

    [Display(Name = "Signature image")]
    public IFormFile? Signature { get; set; }
}

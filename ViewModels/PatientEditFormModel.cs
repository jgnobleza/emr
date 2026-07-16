using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace medrec.ViewModels;

public sealed class PatientEditFormModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Required, StringLength(180)]
    [Display(Name = "Patient name")]
    public string FullName { get; set; } = string.Empty;

    [Range(0, 130)]
    public int Age { get; set; }

    [StringLength(255)]
    public string? Address { get; set; }

    [StringLength(20)]
    public string? Sex { get; set; } = "Female";

    [StringLength(80)]
    public string? CivilStatus { get; set; }

    [StringLength(40)]
    [Display(Name = "Contact No.")]
    public string? ContactNumber { get; set; }

    [StringLength(120)]
    public string? Occupation { get; set; }

    [StringLength(160)]
    public string? Company { get; set; }

    [EmailAddress, StringLength(190)]
    [Display(Name = "Email Address")]
    public string? Email { get; set; }

    [StringLength(180)]
    [Display(Name = "Husband/Partner")]
    public string? PartnerName { get; set; }

    [StringLength(40)]
    [Display(Name = "Husband/Partner Contact No.")]
    public string? PartnerContactNumber { get; set; }

    [StringLength(180)]
    [Display(Name = "Referred By")]
    public string? ReferredBy { get; set; }

    [Range(0, 80)]
    [Display(Name = "Age of Menarche")]
    public int? AgeOfMenarche { get; set; }

    [Range(0, 130)]
    [Display(Name = "If Menopausal, age at menopause")]
    public int? MenopauseAge { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "PMP")]
    public DateOnly? PreviousMenstrualPeriod { get; set; }

    [Range(0, 365)]
    [Display(Name = "Every (days)")]
    public int? PeriodCycleDays { get; set; }

    [Range(0, 60)]
    [Display(Name = "Lasting (days)")]
    public int? PeriodDurationDays { get; set; }

    [StringLength(80)]
    [Display(Name = "Amount")]
    public string? MenstrualAmount { get; set; }

    [StringLength(20)]
    [Display(Name = "Menstrual pattern")]
    public string MenstrualPattern { get; set; } = "Regular";

    [Display(Name = "Sexually Active")]
    public bool? SexuallyActive { get; set; }

    [StringLength(180)]
    [Display(Name = "Methods of Contraception used")]
    public string? ContraceptionMethod { get; set; }

    [Range(0, 300)]
    [Display(Name = "Height")]
    public decimal? HeightCm { get; set; }

    [Range(0, 500)]
    [Display(Name = "Weight")]
    public decimal? WeightKg { get; set; }

    [StringLength(40)]
    [Display(Name = "BP")]
    public string? BloodPressure { get; set; }

    [StringLength(40)]
    [Display(Name = "FHT")]
    public string? FetalHeartTone { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Last Menstrual Period")]
    public DateOnly? LastMenstrualPeriod { get; set; }

    [Display(Name = "Patient Image")]
    public IFormFile? Photo { get; set; }

    [StringLength(500)]
    [Display(Name = "Image URL")]
    public string? PhotoUrl { get; set; }
}

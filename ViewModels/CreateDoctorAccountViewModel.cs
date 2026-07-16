using System.ComponentModel.DataAnnotations;

namespace medrec.ViewModels;

public sealed class CreateDoctorAccountViewModel
{
    [Required, StringLength(160)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required, StringLength(190)]
    [Display(Name = "Username or email")]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(120, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(Password))]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

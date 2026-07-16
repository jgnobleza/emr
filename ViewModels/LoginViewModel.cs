using System.ComponentModel.DataAnnotations;

namespace medrec.ViewModels;

public sealed class LoginViewModel
{
    [Required, StringLength(80)]
    [Display(Name = "Username or email")]
    public string Username { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(120)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

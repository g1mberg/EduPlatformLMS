using System.ComponentModel.DataAnnotations;

namespace dreams.Models.Account;

public class TwoFactorLoginViewModel
{
    [Required(ErrorMessage = "Введите 6-значный код из приложения")]
    [StringLength(7, MinimumLength = 6)]
    [DataType(DataType.Text)]
    [Display(Name = "Код")]
    public string Code { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class TwoFactorSetupViewModel
{
    public string SharedKey { get; set; } = string.Empty;
    public string AuthenticatorUri { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите код из приложения")]
    [StringLength(7, MinimumLength = 6)]
    [Display(Name = "Код подтверждения")]
    public string Code { get; set; } = string.Empty;
}

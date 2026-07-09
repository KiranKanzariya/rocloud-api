using FluentValidation;

namespace ROCloud.Application.Common.Validation;

/// <summary>
/// Reusable FluentValidation password policy (guide §10.2). Composed into command
/// validators via <c>RuleFor(x =&gt; x.Password).Password()</c>.
/// NOTE: the full HaveIBeenPwned top-10k blocklist is deferred; a small obvious-password
/// set is used for now.
/// </summary>
public static class PasswordRules
{
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password123", "12345678", "123456789",
        "qwerty123", "letmein123", "admin1234", "welcome123", "iloveyou1",
        "changeme1", "abcd1234", "qwertyuiop", "1q2w3e4r5t"
    };

    public static IRuleBuilderOptions<T, string> Password<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.")
            .Matches("[\\W_]").WithMessage("Password must contain a special character.")
            .Must(p => string.IsNullOrEmpty(p) || !CommonPasswords.Contains(p))
            .WithMessage("Password is too common.");
}

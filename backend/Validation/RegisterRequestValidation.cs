using System.ComponentModel.DataAnnotations;

namespace Org.OpenAPITools.Models;

public partial class RegisterRequest : IValidatableObject
{
    private static readonly EmailAddressAttribute EmailValidator = new();
    private static readonly PhoneAttribute PhoneValidator = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Email) && !EmailValidator.IsValid(Email))
        {
            yield return new ValidationResult("邮箱格式不正确。", [nameof(Email)]);
        }

        if (!string.IsNullOrWhiteSpace(Phone) && !PhoneValidator.IsValid(Phone))
        {
            yield return new ValidationResult("手机号格式不正确。", [nameof(Phone)]);
        }
    }
}

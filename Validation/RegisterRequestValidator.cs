using FluentValidation;
using MeetAndGreet.API.Models.Requests;

namespace MeetAndGreet.API.Validation
{
    public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
    {
        public RegisterRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Имя пользователя обязательно.")
                .Length(3, 20).WithMessage("Имя пользователя должно быть от 3 до 20 символов.")
                .Matches(@"^[a-zA-Z0-9_]+$").WithMessage("Имя пользователя может содержать только буквы, цифры и знаки подчеркивания.");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Пароль обязателен.")
                .MinimumLength(6).WithMessage("Пароль должен содержать не менее 6 символов.");

            RuleFor(x => x.Avatar)
                .NotNull().WithMessage("Аватар обязателен.")
                .SetValidator(new AvatarConfigValidator());
        }
    }
}
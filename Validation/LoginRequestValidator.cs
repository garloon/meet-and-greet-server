using FluentValidation;
using MeetAndGreet.API.Models.Requests;

namespace MeetAndGreet.API.Validation
{
    public class LoginRequestValidator : AbstractValidator<LoginRequest>
    {
        public LoginRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Имя пользователя обязательно.")
                .Length(3, 20).WithMessage("Имя пользователя должно быть от 3 до 20 символов.");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Пароль обязателен.")
                .MinimumLength(6).WithMessage("Пароль должен содержать не менее 6 символов.");
        }
    }
}
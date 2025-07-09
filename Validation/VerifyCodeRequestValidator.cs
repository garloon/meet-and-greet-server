using FluentValidation;
using MeetAndGreet.API.Models.Requests;

namespace MeetAndGreet.API.Validation
{
    public class VerifyCodeRequestValidator : AbstractValidator<VerifyCodeRequest>
    {
        public VerifyCodeRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Имя пользователя обязательно.")
                .Length(3, 20).WithMessage("Имя пользователя должно быть от 3 до 20 символов.");

            RuleFor(x => x.Code)
                .NotEmpty().WithMessage("Код верификации обязателен.")
                .Length(6).WithMessage("Код верификации должен содержать 6 символов.");
        }
    }
}
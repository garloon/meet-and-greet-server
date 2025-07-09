using FluentValidation;
using MeetAndGreet.API.Models;

namespace MeetAndGreet.API.Validation
{
    public class AvatarConfigValidator : AbstractValidator<AvatarConfig>
    {
        public AvatarConfigValidator()
        {
            RuleFor(x => x.Gender)
                .NotEmpty().WithMessage("Пол аватара обязателен.")
                .Must(x => x == "male" || x == "female").WithMessage("Пол аватара должен быть 'male' или 'female'.");

            RuleFor(x => x.Color)
                .NotEmpty().WithMessage("Цвет аватара обязателен.")
                .Matches(@"^#([0-9a-fA-F]{3}){1,2}$").WithMessage("Цвет аватара должен быть в HEX-формате.");
        }
    }
}
using System.ComponentModel.DataAnnotations;

namespace MeetAndGreet.API.Models
{
    public class RegisterRequest
    {
        [Required]
        [StringLength(20, MinimumLength = 3)]
        [RegularExpression(@"^[a-zA-Z0-9_]+$")]
        public string Name { get; set; }

        [Required]
        [MinLength(6)]
        public string Password { get; set; }
    }
}

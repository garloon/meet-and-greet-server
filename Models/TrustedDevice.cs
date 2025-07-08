using System.ComponentModel.DataAnnotations;

namespace MeetAndGreet.API.Models
{
    public class TrustedDevice
    {
        [Required]
        [StringLength(64, MinimumLength = 16)]
        [RegularExpression(@"^[a-zA-Z0-9+/=]+$")]
        public string Id { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string FingerprintMetadata { get; set; }
        public Guid UserId { get; set; }
    }
}

namespace MeetAndGreet.API.Models
{
    public class RefreshToken
    {
        public Guid Id { get; set; }
        public string Token { get; set; }
        public Guid UserId { get; set; }
        public DateTime Expires { get; set; }
        public bool IsUsed { get; set; }

        public virtual User User { get; set; }
    }
}

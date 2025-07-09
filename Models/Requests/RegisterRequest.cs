namespace MeetAndGreet.API.Models.Requests
{
    public class RegisterRequest
    {
        public string Name { get; set; }
        public string Password { get; set; }
        public AvatarConfig Avatar { get; set; }
    }
}

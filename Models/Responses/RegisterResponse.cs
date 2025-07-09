namespace MeetAndGreet.API.Models.Responses
{
    public class RegisterResponse
    {
        public string Token { get; set; }
        public string RefreshToken { get; set; }
        public string UserId { get; set; }
    }
}

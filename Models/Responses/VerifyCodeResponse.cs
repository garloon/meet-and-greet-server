namespace MeetAndGreet.API.Models.Responses
{
    public class VerifyCodeResponse
    {
        public string Token { get; set; }
        public string RefreshToken { get; set; }
        public string UserId { get; set; }
    }
}

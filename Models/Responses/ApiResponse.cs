namespace MeetAndGreet.API.Models.Responses
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; } = true;
        public T? Data { get; set; }
        public ErrorResponse? Error { get; set; }
    }
}

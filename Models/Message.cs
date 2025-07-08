namespace MeetAndGreet.API.Models
{
    public class Message
    {
        public Guid Id { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public Guid? UserId { get; set; }
        public string UserName { get; set; }
        public virtual User User { get; set; }
        public Guid ChannelId { get; set; }
        public virtual Channel Channel { get; set; }
    }
}

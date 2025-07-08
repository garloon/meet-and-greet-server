namespace MeetAndGreet.API.Models
{
    public class Channel
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsPublic { get; set; }
        public virtual List<Message> Messages { get; set; } = new List<Message>();
    }
}

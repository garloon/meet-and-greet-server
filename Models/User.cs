namespace MeetAndGreet.API.Models
{
    public class User
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string PasswordHash { get; set; }
        public string CurrentCode { get; set; } =  string.Empty;
        public DateTime CodeExpiry { get; set; }
        public virtual List<TrustedDevice> TrustedDevices { get; set; } = new List<TrustedDevice>();
    }
}

using System.Security.Cryptography;
using System.Text;

namespace MeetAndGreet.API.Services
{
    public class DeviceFingerprintService
    {
        private readonly IConfiguration _config;

        public DeviceFingerprintService(IConfiguration config)
        {
            _config = config;
        }

        public string GetDeviceFingerprint(HttpContext context)
        {
            var userAgent = context.Request.Headers["User-Agent"].ToString();
            var acceptLanguage = context.Request.Headers["Accept-Language"].ToString();
            var salt = _config["Security:DeviceFingerprintSalt"];

            if (string.IsNullOrEmpty(salt))
                throw new Exception("Device fingerprint salt is not configured!");

            var combinedString = $"{userAgent}:{acceptLanguage}:{salt}";

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combinedString));

            return Convert.ToBase64String(hashBytes, 0, 16);
        }
    }
}

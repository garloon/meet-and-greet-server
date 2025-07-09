using Microsoft.Extensions.Logging;

namespace MeetAndGreet.API.Services
{
    public class DeviceFingerprintService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<DeviceFingerprintService> _logger;

        public DeviceFingerprintService(IConfiguration config, ILogger<DeviceFingerprintService> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("DeviceFingerprintService constructed.");
        }

        public string GetDeviceFingerprint(HttpContext context)
        {
            try
            {
                var userAgent = context.Request.Headers["User-Agent"].ToString();
                var acceptLanguage = context.Request.Headers["Accept-Language"].ToString();
                var screenResolution = context.Request.Headers["X-Device-ScreenResolution"].ToString();
                var timeZone = context.Request.Headers["X-Device-TimeZone"].ToString();
                var ipAddress = context.Connection.RemoteIpAddress?.ToString();

                var salt = _config["Security:DeviceFingerprintSalt"] 
                    ?? throw new InvalidOperationException("Device fingerprint salt is not configured!");

                if (string.IsNullOrEmpty(salt))
                    throw new Exception("Device fingerprint salt is not configured!");

                // Build the combined string
                var combinedString = $"{userAgent}:{acceptLanguage}:{screenResolution}:{timeZone}:{ipAddress}:{salt}";

                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combinedString));

                return Convert.ToBase64String(hashBytes, 0, 16);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating device fingerprint");
                return string.Empty;
            }
        }
    }
}
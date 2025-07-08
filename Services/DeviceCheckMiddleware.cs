using MeetAndGreet.API.Models;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace MeetAndGreet.API.Services
{
    public class DeviceCheckMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly DeviceFingerprintService _fingerprintService;
        private readonly ILogger<DeviceCheckMiddleware> _logger;

        public DeviceCheckMiddleware(RequestDelegate next, DeviceFingerprintService fingerprintService, ILogger<DeviceCheckMiddleware> logger)
        {
            _next = next;
            _fingerprintService = fingerprintService;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, AuthService auth)
        {
            if (context.Request.Path.StartsWithSegments("/chatHub") ||
                context.Request.Path.StartsWithSegments("/api/auth"))
            {
                await _next(context);
                return;
            }

            var deviceId = _fingerprintService.GetDeviceFingerprint(context);
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId != null && !auth.IsDeviceTrusted(new User { Id = Guid.Parse(userId) }, deviceId))
            {
                _logger.LogWarning($"Подозрительный доступ: UserId={userId}, DeviceId={deviceId}");
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Требуется подтверждение устройства");
                return;
            }

            await _next(context);
        }
    }
}

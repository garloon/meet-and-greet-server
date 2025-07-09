using MeetAndGreet.API.Models;
using System.Security.Claims;

namespace MeetAndGreet.API.Services
{
    public class DeviceCheckMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly DeviceFingerprintService _fingerprintService;
        private readonly ILogger<DeviceCheckMiddleware> _logger;
        private readonly RedisService _redisService;

        public DeviceCheckMiddleware(RequestDelegate next, DeviceFingerprintService fingerprintService, ILogger<DeviceCheckMiddleware> logger, RedisService redisService)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _logger.LogInformation("DeviceCheckMiddleware constructed.");
        }

        public async Task InvokeAsync(HttpContext context)
        {
            _logger.LogInformation("DeviceCheckMiddleware invoked.");

            if (context.Request.Path.StartsWithSegments("/chatHub") ||
                context.Request.Path.StartsWithSegments("/api/auth"))
            {
                _logger.LogDebug("Skipping DeviceCheckMiddleware for auth/hub path: {Path}", context.Request.Path);
                await _next(context);
                return;
            }
            try
            {

                var deviceId = _fingerprintService.GetDeviceFingerprint(context);
                var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

                var authService = context.RequestServices.GetRequiredService<AuthService>();

                if (userId == null)
                {
                    _logger.LogWarning("User ID is missing in claims.");
                    await _next(context);
                    return;
                }
                if (string.IsNullOrEmpty(deviceId))
                {
                    _logger.LogWarning("Device fingerprint is empty for user {UserId}", userId);
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync("Требуется подтверждение устройства");
                    return;
                }

                var cacheKey = $"device:{userId}:{deviceId}:trusted";

                var isTrusted = await _redisService.GetValueAsync(cacheKey);
                if (isTrusted == "true")
                {
                    _logger.LogDebug("Device trusted (from cache): UserId={UserId}, DeviceId={DeviceId}", userId, deviceId);
                    await _next(context);
                    return;
                }

                Guid userIdGuid;

                if (!Guid.TryParse(userId, out userIdGuid))
                {
                    _logger.LogWarning("Invalid user ID: {UserId}", userId);
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync("Требуется подтверждение устройства");
                    return;
                }

                if (!authService.IsDeviceTrusted(new User { Id = userIdGuid }, deviceId))
                {
                    _logger.LogWarning("Подозрительный доступ: UserId={UserId}, DeviceId={DeviceId}", userId, deviceId);
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync("Требуется подтверждение устройства");
                    return;
                }
                
                _logger.LogInformation("Marking Device as trusted: UserId={UserId}, DeviceId={DeviceId}", userId, deviceId);
                await _redisService.SetValueAsync(cacheKey, "true");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DeviceCheckMiddleware");
            }

            _logger.LogInformation("Finished  DeviceCheckMiddleware.");
            await _next(context);
        }
    }
}
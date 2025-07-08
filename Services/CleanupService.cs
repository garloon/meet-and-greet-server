using MeetAndGreet.API.Data;
using MeetAndGreet.API.Hubs;
using Microsoft.EntityFrameworkCore;

namespace MeetAndGreet.API.Services
{
    public class CleanupService : BackgroundService
    {
        private readonly RedisService _redisService;
        private readonly IServiceProvider _services;
        private readonly ILogger<CleanupService> _logger;

        public CleanupService(
            RedisService redisService,
            IServiceProvider services,
            ILogger<CleanupService> logger)
        {
            _redisService = redisService;
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _redisService.RunCleanupAsync();
                await CleanupMessagesAsync();
                await CleanupDevicesAsync();
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }

        private async Task CleanupMessagesAsync()
        {
            using (var scope = _services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                try
                {
                    var cutoffDate = DateTime.UtcNow.AddMonths(-1);
                    var oldMessages = dbContext.Messages
                        .Where(m => m.Timestamp < cutoffDate);

                    dbContext.Messages.RemoveRange(oldMessages);
                    await dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка очистки старых сообщений");
                }
            }
        }

        private async Task CleanupDevicesAsync()
        {
            using (var scope = _services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var expired = await db.TrustedDevices
                    .Where(d => d.ExpiryDate < DateTime.UtcNow)
                    .ToListAsync();

                db.TrustedDevices.RemoveRange(expired);
                await db.SaveChangesAsync();
            }
        }
    }
}

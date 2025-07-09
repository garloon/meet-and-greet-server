using MeetAndGreet.API.Data;
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
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("CleanupService constructed.");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CleanupService is starting.");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _redisService.RunCleanupAsync();
                    await CleanupMessagesAsync(stoppingToken);
                    await CleanupDevicesAsync(stoppingToken);
                    await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    _logger.LogInformation("Cleanup task cancelled.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during cleanup cycle.");
                }
            }
            _logger.LogInformation("CleanupService is stopping.");
        }

        private async Task CleanupMessagesAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Cleaning up old messages.");
            using (var scope = _services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                try
                {
                    var cutoffDate = DateTime.UtcNow.AddMonths(-1);
                    var oldMessages = dbContext.Messages
                        .Where(m => m.Timestamp < cutoffDate);

                    dbContext.Messages.RemoveRange(oldMessages);
                    await dbContext.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("Old messages cleanup completed.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка очистки старых сообщений");
                }
            }
        }

        private async Task CleanupDevicesAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Cleaning up expired devices.");
            using (var scope = _services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                try
                {
                    var expired = await db.TrustedDevices
                        .Where(d => d.ExpiryDate < DateTime.UtcNow)
                        .ToListAsync(stoppingToken);

                    db.TrustedDevices.RemoveRange(expired);
                    await db.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("Expired devices cleanup completed.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка очистки старых устройств");
                }
            }
        }
    }
}
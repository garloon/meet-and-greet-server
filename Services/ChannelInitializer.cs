using MeetAndGreet.API.Data;
using MeetAndGreet.API.Models;
using Microsoft.EntityFrameworkCore;

namespace MeetAndGreet.API.Services
{
    public class ChannelInitializer
    {
        private readonly ApplicationDbContext _db;
        private readonly RussianCityService _cityService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ChannelInitializer> _logger;

        public ChannelInitializer(ApplicationDbContext db, RussianCityService cityService, IConfiguration configuration, ILogger<ChannelInitializer> logger)
        {
            _db = db;
            _cityService = cityService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            await CreateGuestChannelIfNotExistsAsync();
            await SyncCityChannels();
        }
        
        private async Task CreateGuestChannelIfNotExistsAsync()
        {
            var guestChannelName = _configuration["GuestChannel:Name"] ?? "Гостевой чат";
            var guestChannelDescription = _configuration["GuestChannel:Description"] ?? "Чат для временного гостевого доступа";
            
            if (!await _db.Channels.AnyAsync(c => c.Name == guestChannelName))
            {
                _db.Channels.Add(new Channel
                {
                    Id = Guid.NewGuid(),
                    Name = guestChannelName,
                    Description = guestChannelDescription,
                    IsPublic = true
                });
                await _db.SaveChangesAsync();
                _logger.LogInformation("Created guest channel '{GuestChannelName}'.", guestChannelName);
            }
            else
            {
                _logger.LogInformation("Guest channel '{GuestChannelName}' already exists.", guestChannelName);
            }
        }

        private async Task SyncCityChannels()
        {
            var cities = await _cityService.GetCitiesAsync();
            var existingChannels = await _db.Channels.ToListAsync();

            foreach (var city in cities)
            {
                if (!existingChannels.Any(c => c.Name == city.Name))
                {
                    _db.Channels.Add(new Channel
                    {
                        Id = Guid.NewGuid(),
                        Name = city.Name,
                        Description = $"Чат города {city.Name} ({city.Region})",
                        IsPublic = true
                    });
                }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Synchronized city channels.");
        }
    }
}

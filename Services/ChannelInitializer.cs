using MeetAndGreet.API.Data;
using MeetAndGreet.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging; // Add this

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
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _cityService = cityService ?? throw new ArgumentNullException(nameof(cityService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("ChannelInitializer constructed.");
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("Initializing channels.");
            try
            {
                await CreateGuestChannelIfNotExistsAsync();
                await SyncCityChannels();
                _logger.LogInformation("Channel initialization completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during channel initialization.");
            }
        }

        private async Task CreateGuestChannelIfNotExistsAsync()
        {
            var guestChannelName = _configuration["GuestChannel:Name"] ?? "Гостевой чат";
            var guestChannelDescription = _configuration["GuestChannel:Description"] ?? "Чат для временного гостевого доступа";

            if (!await _db.Channels.AnyAsync(c => c.Name == guestChannelName))
            {
                _logger.LogInformation("Creating guest channel '{GuestChannelName}'.", guestChannelName);
                _db.Channels.Add(new Channel
                {
                    Id = Guid.NewGuid(),
                    Name = guestChannelName,
                    Description = guestChannelDescription,
                    IsPublic = true
                });

                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogError(ex, "Error saving changes to database.");
                    throw;
                }

                _logger.LogInformation("Created guest channel '{GuestChannelName}'.", guestChannelName);
            }
            else
            {
                _logger.LogInformation("Guest channel '{GuestChannelName}' already exists.", guestChannelName);
            }
        }

        private async Task SyncCityChannels()
        {
            _logger.LogInformation("Syncing city channels.");
            try
            {
                var cities = await _cityService.GetCitiesAsync();
                var existingChannels = await _db.Channels.ToListAsync();

                foreach (var city in cities)
                {
                    if (!existingChannels.Any(c => c.Name == city.Name))
                    {
                        _logger.LogInformation("Creating city channel '{CityName}'.", city.Name);
                        _db.Channels.Add(new Channel
                        {
                            Id = Guid.NewGuid(),
                            Name = city.Name,
                            Description = $"Чат города {city.Name} ({city.Region})",
                            IsPublic = true
                        });
                    }
                }
                
                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogError(ex, "Error saving changes to database.");
                    throw;
                }

                _logger.LogInformation("Synchronized city channels.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing city channels.");
            }
        }
    }
}
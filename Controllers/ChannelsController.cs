using MeetAndGreet.API.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace MeetAndGreet.API.Controllers
{
    [ApiController]
    [Route("api/channels")]
    public class ChannelsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ChannelsController> _logger; // Внедряем ILogger

        public ChannelsController(ApplicationDbContext context, ILogger<ChannelsController> logger) // Внедряем в конструктор
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("ChannelsController constructed.");
        }

        [HttpGet]
        public async Task<IActionResult> GetChannels()
        {
            _logger.LogInformation("GetChannels endpoint called.");
            try
            {
                var channels = await _context.Channels
                    .Where(c => c.Name != "новички")
                    .Select(c => new
                    {
                        c.Id,
                        c.Name,
                        c.Description
                    })
                    .ToListAsync();

                _logger.LogInformation("Retrieved {ChannelCount} channels.", channels.Count);
                return Ok(channels);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during GetChannels");
                return StatusCode(500, "Internal server error");
            }
            finally
            {
                _logger.LogInformation("GetChannels endpoint finished.");
            }
        }

        [HttpGet("guestChannel")]
        public async Task<IActionResult> GetGuestChannelAsync()
        {
            _logger.LogInformation("GetGuestChannelAsync endpoint called.");
            try
            {
                var guestChannel = await _context.Channels
                    .Where(c => c.Name == "Гостевой чат")
                    .Select(c => new
                    {
                        c.Id,
                        c.Name,
                        c.Description
                    }).SingleOrDefaultAsync();

                if (guestChannel == null)
                {
                    _logger.LogWarning("Guest channel not found.");
                }

                return Ok(guestChannel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during GetGuestChannelAsync");
                return StatusCode(500, "Internal server error");
            }
            finally
            {
                _logger.LogInformation("GetGuestChannelAsync endpoint finished.");
            }
        }
    }
}
using MeetAndGreet.API.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeetAndGreet.API.Controllers
{
    [ApiController]
    [Route("api/channels")]
    public class ChannelsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ChannelsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetChannels()
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

            return Ok(channels);
        }

        [HttpGet("guestChannel")]
        public async Task<IActionResult> GetGuestChannelAsync()
        {
            var guestChannel = await _context.Channels
                .Where(c => c.Name == "Гостевой чат")
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Description
                }).SingleOrDefaultAsync();

            return Ok(guestChannel);
        }
    }
}

using MeetAndGreet.API.Data;
using MeetAndGreet.API.Models;
using Microsoft.EntityFrameworkCore;

namespace MeetAndGreet.API.Services
{
    public class AuthService
    {
        private readonly ApplicationDbContext _dbContext;

        public AuthService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<string> GenerateAndSaveCode(Guid userId)
        {
            var user = await _dbContext.Users.FindAsync(userId);
            user.CurrentCode = new Random().Next(100000, 999999).ToString();
            user.CodeExpiry = DateTime.UtcNow.AddMinutes(5);
            await _dbContext.SaveChangesAsync();
            return user.CurrentCode;
        }

        public async Task<bool> VerifyCode(Guid userId, string code)
        {
            var user = await _dbContext.Users.FindAsync(userId);
            return user?.CurrentCode == code && user.CodeExpiry > DateTime.UtcNow;
        }

        public async Task AddTrustedDevice(Guid userId, string deviceId)
        {
            if (await _dbContext.TrustedDevices.AnyAsync(d => d.Id == deviceId && d.UserId == userId))
                return;

            _dbContext.TrustedDevices.Add(new TrustedDevice
            {
                Id = deviceId,
                ExpiryDate = DateTime.UtcNow.AddDays(30),
                FingerprintMetadata = deviceId,
                UserId = userId
            });

            await _dbContext.SaveChangesAsync();
        }

        public bool IsDeviceTrusted(User user, string deviceId)
        {
            return user.TrustedDevices?.Any(d =>
                d.Id == deviceId &&
                d.ExpiryDate > DateTime.UtcNow) ?? false;
        }
    }
}

// AuthService.cs
using MeetAndGreet.API.Data;
using MeetAndGreet.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MeetAndGreet.API.Services
{
    public class AuthService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<AuthService> _logger;

        public AuthService(ApplicationDbContext dbContext, ILogger<AuthService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("AuthService constructed.");
        }

        public async Task<string> GenerateAndSaveCode(Guid userId)
        {
            _logger.LogInformation("Generating and saving code for user {UserId}.", userId);
            try
            {
                var user = await _dbContext.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("User not found for code generation: {UserId}", userId);
                    return null;
                }

                user.CurrentCode = new Random().Next(100000, 999999).ToString();
                user.CodeExpiry = DateTime.UtcNow.AddMinutes(5);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Generated and saved code for user {UserId}.", userId);
                return user.CurrentCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating and saving code for user {UserId}.", userId);
                throw;
            }
        }

        public async Task<bool> VerifyCode(Guid userId, string code)
        {
            _logger.LogInformation("Verifying code for user {UserId}.", userId);
            try
            {
                var user = await _dbContext.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("User not found during code verification: {UserId}", userId);
                    return false;
                }

                bool isValid = user.CurrentCode == code && user.CodeExpiry > DateTime.UtcNow;
                _logger.LogInformation("Code verification result for user {UserId}: {IsValid}", userId, isValid);
                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying code for user {UserId}.", userId);
                throw;
            }
        }

        public async Task AddTrustedDevice(Guid userId, string deviceId)
        {
            _logger.LogInformation("Adding trusted device {DeviceId} for user {UserId}.", deviceId, userId);
            try
            {
                if (await _dbContext.TrustedDevices.AnyAsync(d => d.Id == deviceId && d.UserId == userId))
                {
                    _logger.LogWarning("Trusted device {DeviceId} already exists for user {UserId}.", deviceId, userId);
                    return;
                }

                _dbContext.TrustedDevices.Add(new TrustedDevice
                {
                    Id = deviceId,
                    ExpiryDate = DateTime.UtcNow.AddDays(30),
                    FingerprintMetadata = deviceId,
                    UserId = userId
                });

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Trusted device {DeviceId} added successfully for user {UserId}.", deviceId, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding trusted device {DeviceId} for user {UserId}.", deviceId, userId);
                throw;
            }
        }
        public async Task SaveRefreshToken(Guid userId, string refreshToken)
        {
            _logger.LogInformation("Saving refresh token for user {UserId}.", userId);
            try
            {
                await _dbContext.RefreshTokens.AddAsync(new RefreshToken
                {
                    Token = refreshToken,
                    UserId = userId,
                    Expires = DateTime.UtcNow.AddDays(7)
                });
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Refresh token saved successfully for user {UserId}.", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving refresh token for user {UserId}.", userId);
                throw;
            }
        }

        public bool IsDeviceTrusted(User user, string deviceId)
        {
            return user.TrustedDevices?.Any(d =>
                d.Id == deviceId &&
                d.ExpiryDate > DateTime.UtcNow) ?? false;
        }
    }
}
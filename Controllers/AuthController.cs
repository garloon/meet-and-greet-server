using MeetAndGreet.API.Data;
using MeetAndGreet.API.Models;
using MeetAndGreet.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace MeetAndGreet.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _auth;
        private readonly DeviceFingerprintService _fingerprintService;
        private readonly TokenService _tokenService;
        private readonly ApplicationDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContext;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            AuthService auth,
            DeviceFingerprintService fingerprintService,
            TokenService tokenService,
            ApplicationDbContext dbContext,
            IHttpContextAccessor httpContext,
            IConfiguration config,
            ILogger<AuthController> logger)
        {
            _auth = auth;
            _fingerprintService = fingerprintService;
            _tokenService = tokenService;
            _dbContext = dbContext;
            _httpContext = httpContext;
            _config = config;
            _logger = logger;
        }

        [HttpGet("token")]
        [AllowAnonymous]
        public IActionResult GetToken()
        {
            var issuer = _config["Jwt:Issuer"];
            var audience = _config["Jwt:Audience"];
            var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"]);
            var signingCredentials = new SigningCredentials(
                        new SymmetricSecurityKey(key),
                        SecurityAlgorithms.HmacSha512Signature
                    );

            var subject = new ClaimsIdentity(new[]
            {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "testuser"),
        });

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = subject,
                Expires = DateTime.UtcNow.AddMinutes(10),
                Issuer = issuer,
                Audience = audience,
                SigningCredentials = signingCredentials
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var stringToken = tokenHandler.WriteToken(token);

            return Ok(new { token = stringToken });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] Models.LoginRequest request)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Name == request.Name);
            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Unauthorized();

            var deviceId = _fingerprintService.GetDeviceFingerprint(_httpContext.HttpContext);
            var isTrustedDevice = _auth.IsDeviceTrusted(user, deviceId);

            if (!isTrustedDevice)
            {
                var code = await _auth.GenerateAndSaveCode(user.Id);
                return Ok(new { RequiresCode = true });
            }

            var token = _tokenService.GenerateToken(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Name)
            });

            var refreshToken = _tokenService.GenerateRefreshToken();
            await _tokenService.SaveRefreshToken(user.Id, refreshToken);

            return Ok(new
            {
                Token = token,
                RefreshToken = refreshToken,
                UserId = user.Id.ToString()
            });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Псевдоним обязателен");

            if (request.Password.Length < 6)
                return BadRequest("Пароль слишком короткий");

            if (await _dbContext.Users.AnyAsync(u => u.Name == request.Name))
                return BadRequest("Псевдоним занят");

            var user = new User
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();
            
            var token = _tokenService.GenerateToken(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Name)
            });

            var refreshToken = _tokenService.GenerateRefreshToken();
            await _tokenService.SaveRefreshToken(user.Id, refreshToken);

            return Ok(new
            {
                Token = token,
                RefreshToken = refreshToken,
                UserId = user.Id.ToString()
            });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var deviceId = _fingerprintService.GetDeviceFingerprint(_httpContext.HttpContext);
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var device = await _dbContext.TrustedDevices
                .FirstOrDefaultAsync(d => d.UserId == userId && d.Id == deviceId);

            if (device != null)
            {
                _dbContext.TrustedDevices.Remove(device);
                await _dbContext.SaveChangesAsync();
            }

            return Ok();
        }

        [HttpPost("verify-code")]
        public async Task<IActionResult> VerifyCode([FromBody] VerifyCodeRequest request)
        {
            try
            {
                var user = await _dbContext.Users
                    .Include(u => u.TrustedDevices)
                    .FirstOrDefaultAsync(u => u.Name == request.Name);

                if (user == null)
                    return BadRequest(new { success = false, message = "Пользователь не найден" });

                if (string.IsNullOrEmpty(user.CurrentCode) ||
                    user.CurrentCode != request.Code ||
                    user.CodeExpiry < DateTime.UtcNow)
                {
                    return BadRequest(new { success = false, message = "Неверный или просроченный код" });
                }

                var deviceId = _fingerprintService.GetDeviceFingerprint(_httpContext.HttpContext);

                if (!user.TrustedDevices.Any(d => d.Id == deviceId))
                {
                    await _auth.AddTrustedDevice(user.Id, deviceId);
                }

                var token = _tokenService.GenerateToken(new[]
                {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim("IsVerified", "true")
        });

                var refreshToken = _tokenService.GenerateRefreshToken();
                await _tokenService.SaveRefreshToken(user.Id, refreshToken);
                
                user.CurrentCode = "";
                await _dbContext.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    token = token,
                    refreshToken = refreshToken,
                    userId = user.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при верификации кода");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Внутренняя ошибка сервера"
                });
            }
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
        {
            try
            {
                var principal = _tokenService.ValidateExpiredToken(request.AccessToken);
                var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier));
                
                var storedToken = await _dbContext.RefreshTokens
                    .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken &&
                                            rt.UserId == userId &&
                                            rt.Expires > DateTime.UtcNow &&
                                            !rt.IsUsed);

                if (storedToken == null)
                    return Unauthorized(new { message = "Invalid refresh token" });
                
                var newToken = _tokenService.GenerateToken(principal.Claims);
                var newRefreshToken = _tokenService.GenerateRefreshToken();
                
                storedToken.IsUsed = true;
                await _dbContext.SaveChangesAsync();
                
                await _tokenService.SaveRefreshToken(userId, newRefreshToken);

                return Ok(new
                {
                    AccessToken = newToken,
                    RefreshToken = newRefreshToken
                });
            }
            catch (SecurityTokenException ex)
            {
                return Unauthorized(new { message = "Invalid token", details = ex.Message });
            }
        }

        private string GenerateRandomCode()
            => new Random().Next(100000, 999999).ToString();
    }
}

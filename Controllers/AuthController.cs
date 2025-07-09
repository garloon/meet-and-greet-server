using FluentValidation;
using MeetAndGreet.API.Data;
using MeetAndGreet.API.Models;
using MeetAndGreet.API.Models.Requests;
using MeetAndGreet.API.Models.Responses;
using MeetAndGreet.API.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

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
        private readonly IValidator<RegisterRequest> _registerValidator;
        private readonly IValidator<LoginRequest> _loginValidator;
        private readonly IValidator<VerifyCodeRequest> _verifyCodeValidator;
        private readonly IAntiforgery _antiforgery; // Add this

        public AuthController(
            AuthService auth,
            DeviceFingerprintService fingerprintService,
            TokenService tokenService,
            ApplicationDbContext dbContext,
            IHttpContextAccessor httpContext,
            IConfiguration config,
            ILogger<AuthController> logger,
            IValidator<RegisterRequest> registerValidator,
            IValidator<LoginRequest> loginValidator,
            IValidator<VerifyCodeRequest> verifyCodeValidator,
            IAntiforgery antiforgery) // Inject IAntiforgery
        {
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _httpContext = httpContext ?? throw new ArgumentNullException(nameof(httpContext));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _registerValidator = registerValidator ?? throw new ArgumentNullException(nameof(registerValidator));
            _loginValidator = loginValidator ?? throw new ArgumentNullException(nameof(loginValidator));
            _verifyCodeValidator = verifyCodeValidator ?? throw new ArgumentNullException(nameof(verifyCodeValidator));
            _antiforgery = antiforgery ?? throw new ArgumentNullException(nameof(antiforgery)); // Initialize the antiforgery service
            _logger.LogInformation("AuthController constructed.");
        }

        [HttpGet("token")]
        [AllowAnonymous]
        public IActionResult GetToken()
        {
            _logger.LogInformation("GetToken endpoint called.");
            try
            {
                var issuer = _config["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer not configured");
                var audience = _config["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience not configured");
                var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key not configured"));

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

                return Ok(new ApiResponse<string> { Success = true, Data = stringToken });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Configuration error during token generation");
                return StatusCode(500, new ErrorResponse { Error = "InternalServerError", Message = "Configuration issue." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating test token");
                return StatusCode(500, new ErrorResponse { Error = "InternalServerError", Message = "An error occurred while generating the token." });
            }
            finally
            {
                _logger.LogInformation("GetToken endpoint finished.");
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request == null)
            {
                _logger.LogError("Login request is null.");
                return BadRequest(new ErrorResponse { Error = "BadRequest", Message = "Invalid request." });
            }

            var validationResult = await _loginValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList();
                _logger.LogWarning("Login validation failed for user: {UserName}. Errors: {Errors}", request.Name, string.Join(", ", errors));
                return BadRequest(new ApiResponse<LoginResponse> { Success = false, Error = new ErrorResponse { Error = "ValidationError", Message = "Validation failed.", Details = string.Join(", ", errors) } });
            }

            _logger.LogInformation("Login attempt for user: {UserName}", request.Name);

            try
            {
                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Name == request.Name);
                if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                {
                    _logger.LogWarning("Invalid login attempt for user: {UserName}", request.Name);
                    return Unauthorized(new ErrorResponse { Error = "Unauthorized", Message = "Invalid credentials." });
                }

                var deviceId = _fingerprintService.GetDeviceFingerprint(_httpContext.HttpContext);
                var isTrustedDevice = _auth.IsDeviceTrusted(user, deviceId);

                if (!isTrustedDevice)
                {
                    _logger.LogInformation("User {UserName} requires code verification on this device.", request.Name);
                    var code = await _auth.GenerateAndSaveCode(user.Id);
                    return Ok(new ApiResponse<RequiresCodeResponse> { Success = true, Data = new RequiresCodeResponse { RequiresCode = true } });
                }

                var token = _tokenService.GenerateToken(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim("AvatarConfig", user.AvatarConfig)
                });

                var refreshToken = _tokenService.GenerateRefreshToken();
                await _auth.SaveRefreshToken(user.Id, refreshToken);

                // Generate and set the CSRF token
                var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
                HttpContext.Response.Cookies.Append("X-CSRF-TOKEN", tokens.RequestToken!,
                    new CookieOptions()
                    {
                        HttpOnly = false, // Allow JS access. Very important for SPA.
                        Secure = true, // In production - HTTPS only
                        SameSite = SameSiteMode.Strict // Protect against CSRF
                    });

                _logger.LogInformation("User {UserName} logged in successfully.", request.Name);
                return Ok(new ApiResponse<LoginResponse>
                {
                    Success = true,
                    Data = new LoginResponse
                    {
                        Token = token,
                        RefreshToken = refreshToken,
                        UserId = user.Id.ToString()
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for user {UserName}", request.Name);
                return StatusCode(500, new ErrorResponse { Error = "InternalServerError", Message = "An unexpected error occurred." });
            }
            finally
            {
                _logger.LogInformation("Login endpoint finished.");
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (request == null)
            {
                _logger.LogError("Register request is null.");
                return BadRequest(new ErrorResponse { Error = "BadRequest", Message = "Invalid request." });
            }

            var validationResult = await _registerValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList();
                _logger.LogWarning("Registration validation failed for user: {UserName}. Errors: {Errors}", request.Name, string.Join(", ", errors));
                return BadRequest(new ApiResponse<RegisterResponse> { Success = false, Error = new ErrorResponse { Error = "ValidationError", Message = "Validation failed.", Details = string.Join(", ", errors) } });
            }

            _logger.LogInformation("Registration attempt for user: {UserName}", request.Name);

            try
            {
                if (await _dbContext.Users.AnyAsync(u => u.Name == request.Name))
                {
                    _logger.LogWarning("Username already exists: {UserName}", request.Name);
                    return BadRequest(new ApiResponse<RegisterResponse> { Success = false, Error = new ErrorResponse { Error = "ValidationError", Message = "Username already exists." } });
                }

                var user = new User
                {
                    Id = Guid.NewGuid(),
                    Name = request.Name.Trim(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                    AvatarConfig = JsonSerializer.Serialize(request.Avatar)
                };

                _dbContext.Users.Add(user);
                await _dbContext.SaveChangesAsync();

                var deviceId = _fingerprintService.GetDeviceFingerprint(_httpContext.HttpContext);
                var isTrustedDevice = _auth.IsDeviceTrusted(user, deviceId);

                if (!isTrustedDevice)
                {
                    _logger.LogInformation("User {UserName} requires code verification on this device.", request.Name);
                    var code = await _auth.GenerateAndSaveCode(user.Id);
                    return Ok(new ApiResponse<RequiresCodeResponse> { Success = true, Data = new RequiresCodeResponse { RequiresCode = true } });
                }

                var token = _tokenService.GenerateToken(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Name)
                });

                var refreshToken = _tokenService.GenerateRefreshToken();
                await _auth.SaveRefreshToken(user.Id, refreshToken);
                // Generate and set the CSRF token
                var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
                HttpContext.Response.Cookies.Append("X-CSRF-TOKEN", tokens.RequestToken!,
                   new CookieOptions()
                   {
                       HttpOnly = false, // Allow JS access. Very important for SPA.
                       Secure = true, // In production - HTTPS only
                       SameSite = SameSiteMode.Strict // Protect against CSRF
                   });

                _logger.LogInformation("User {UserName} registered successfully.", request.Name);
                return Ok(new ApiResponse<LoginResponse>
                {
                    Success = true,
                    Data = new LoginResponse
                    {
                        Token = token,
                        RefreshToken = refreshToken,
                        UserId = user.Id.ToString()
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for user {UserName}", request.Name);
                return StatusCode(500, new ErrorResponse { Error = "InternalServerError", Message = "An unexpected error occurred." });
            }
            finally
            {
                _logger.LogInformation("Register endpoint finished.");
            }
        }

        [HttpPost("logout")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            _logger.LogInformation("Logout endpoint called.");

            try
            {
                var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
                {
                    _logger.LogWarning("Invalid user ID in claim: {UserIdClaim}", userIdClaim);
                    return BadRequest(new ErrorResponse { Error = "BadRequest", Message = "Invalid user ID." });
                }

                var deviceId = _fingerprintService.GetDeviceFingerprint(_httpContext.HttpContext);

                var device = await _dbContext.TrustedDevices
                    .FirstOrDefaultAsync(d => d.UserId == userId && d.Id == deviceId);

                if (device != null)
                {
                    _dbContext.TrustedDevices.Remove(device);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("Device {DeviceId} logged out for user {UserId}", deviceId, userId);
                }
                else
                {
                    _logger.LogWarning("Device {DeviceId} not found for user {UserId}", deviceId, userId);
                }

                return Ok(new ApiResponse<object> { Success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                return StatusCode(500, new ErrorResponse { Error = "InternalServerError", Message = "An unexpected error occurred." });
            }
            finally
            {
                _logger.LogInformation("Logout endpoint finished.");
            }
        }

        [HttpPost("verify-code")]
        public async Task<IActionResult> VerifyCode([FromBody] VerifyCodeRequest request)
        {
            if (request == null)
            {
                _logger.LogError("VerifyCode request is null.");
                return BadRequest(new ErrorResponse { Error = "BadRequest", Message = "Invalid request." });
            }

            var validationResult = await _verifyCodeValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList();
                _logger.LogWarning("VerifyCode validation failed for user: {UserName}. Errors: {Errors}", request.Name, string.Join(", ", errors));
                return BadRequest(new ApiResponse<VerifyCodeResponse> { Success = false, Error = new ErrorResponse { Error = "ValidationError", Message = "Validation failed.", Details = string.Join(", ", errors) } });
            }

            _logger.LogInformation("VerifyCode attempt for user: {UserName}", request.Name);

            try
            {
                var user = await _dbContext.Users
                    .Include(u => u.TrustedDevices)
                    .FirstOrDefaultAsync(u => u.Name == request.Name);

                if (user == null)
                {
                    _logger.LogWarning("User not found: {UserName}", request.Name);
                    return BadRequest(new ApiResponse<VerifyCodeResponse> { Success = false, Error = new ErrorResponse { Error = "NotFound", Message = "User not found." } });
                }

                if (string.IsNullOrEmpty(user.CurrentCode) ||
                    user.CurrentCode != request.Code ||
                    user.CodeExpiry < DateTime.UtcNow)
                {
                    _logger.LogWarning("Invalid or expired code for user: {UserName}", request.Name);
                    return BadRequest(new ApiResponse<VerifyCodeResponse> { Success = false, Error = new ErrorResponse { Error = "ValidationError", Message = "Invalid or expired code." } });
                }

                var deviceId = _fingerprintService.GetDeviceFingerprint(_httpContext.HttpContext);

                if (!user.TrustedDevices.Any(d => d.Id == deviceId))
                {
                    await _auth.AddTrustedDevice(user.Id, deviceId);
                    _logger.LogInformation("Added trusted device {DeviceId} for user {UserName}", deviceId, request.Name);
                }

                var token = _tokenService.GenerateToken(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim("IsVerified", "true")
                });

                var refreshToken = _tokenService.GenerateRefreshToken();
                await _auth.SaveRefreshToken(user.Id, refreshToken);

                user.CurrentCode = "";
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("User {UserName} verified successfully.", request.Name);
                return Ok(new ApiResponse<VerifyCodeResponse>
                {
                    Success = true,
                    Data = new VerifyCodeResponse
                    {
                        Token = token,
                        RefreshToken = refreshToken,
                        UserId = user.Id.ToString()
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during verify code for user {UserName}", request.Name);
                return StatusCode(500, new ErrorResponse { Error = "InternalServerError", Message = "An unexpected error occurred." });
            }
            finally
            {
                _logger.LogInformation("VerifyCode endpoint finished.");
            }
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
        {
            if (request == null)
            {
                _logger.LogError("Refresh request is null.");
                return BadRequest(new ErrorResponse { Error = "BadRequest", Message = "Invalid request." });
            }

            _logger.LogInformation("Refresh attempt with refresh token: {RefreshToken}", request.RefreshToken);

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
                {
                    _logger.LogWarning("Invalid refresh token: {RefreshToken}", request.RefreshToken);
                    return Unauthorized(new ErrorResponse { Error = "Unauthorized", Message = "Invalid refresh token" });
                }

                var newToken = _tokenService.GenerateToken(principal.Claims);
                var newRefreshToken = _tokenService.GenerateRefreshToken();

                storedToken.IsUsed = true;
                await _dbContext.SaveChangesAsync();

                await _auth.SaveRefreshToken(userId, newRefreshToken);

                _logger.LogInformation("Token refreshed successfully for user {UserId}.", userId);
                return Ok(new ApiResponse<RefreshTokenResponse>
                {
                    Success = true,
                    Data = new RefreshTokenResponse
                    {
                        AccessToken = newToken,
                        RefreshToken = newRefreshToken
                    }
                });
            }
            catch (SecurityTokenException ex)
            {
                _logger.LogError(ex, "Invalid token during refresh: {ErrorMessage}", ex.Message);
                return Unauthorized(new ErrorResponse { Error = "Unauthorized", Message = "Invalid token", Details = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during refresh with token: {RefreshToken}", request.RefreshToken);
                return StatusCode(500, new ErrorResponse { Error = "InternalServerError", Message = "An unexpected error occurred." });
            }
            finally
            {
                _logger.LogInformation("Refresh endpoint finished.");
            }
        }
    }
}
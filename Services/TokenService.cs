using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace MeetAndGreet.API.Services
{
    public class TokenService
    {
        private readonly IConfiguration _config;
        private readonly string _jwtSecret;
        private readonly ILogger<TokenService> _logger;

        public TokenService(IConfiguration config, ILogger<TokenService> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jwtSecret = Environment.GetEnvironmentVariable("Jwt__Secret")
                         ?? throw new InvalidOperationException("Jwt__Secret environment variable not set.");
            _logger.LogInformation("TokenService constructed.");
        }

        private SigningCredentials GetSigningCredentials()
        {
            _logger.LogDebug("Creating signing credentials.");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret));
            return new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        }

        private SecurityTokenDescriptor CreateTokenDescriptor(IEnumerable<Claim> claims)
        {
            _logger.LogDebug("Creating token descriptor.");
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(15),
                Issuer = _config["Jwt:Issuer"],
                Audience = _config["Jwt:Audience"],
                SigningCredentials = GetSigningCredentials()
            };
            return tokenDescriptor;
        }

        public ClaimsPrincipal ValidateExpiredToken(string token)
        {
            _logger.LogInformation("Validating expired token.");

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = false,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = _config["Jwt:Issuer"],
                    ValidAudience = _config["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret))
                };

                var principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken securityToken);
                _logger.LogInformation("Token validation successful for user: {UserId}.", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                return principal;
            }
            catch (SecurityTokenException ex)
            {
                _logger.LogError(ex, "Invalid token: {ErrorMessage}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating token");
                throw;
            }
        }

        public string GenerateToken(IEnumerable<Claim> claims)
        {
            _logger.LogInformation("Generating JWT token.");
            try
            {
                var token = new JwtSecurityTokenHandler().CreateToken(CreateTokenDescriptor(claims)); // Use the helper method
                var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
                _logger.LogInformation("JWT Token generated");
                return tokenString;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating token.");
                throw;
            }
        }


        public string GenerateRefreshToken()
        {
            _logger.LogInformation("Generating refresh token.");
            var randomNumber = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }
    }
}
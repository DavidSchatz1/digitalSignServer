using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace DigitalSignServer.controller
{
    [ApiController]
    [Route("api/[controller]")]
    public class SessionController : ControllerBase
    {
        private readonly IConfiguration _config;
        public SessionController(IConfiguration config) => _config = config;

        [HttpGet("me")]
        public IActionResult Me()
        {
            // 1) שליפת ה־JWT מהקוקי
            if (!Request.Cookies.TryGetValue("jwt", out var token) || string.IsNullOrWhiteSpace(token))
                return Unauthorized();

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"]);

            try
            {
                // 2) ולידציית הטוקן (אותם פרמטרים כמו ב־Program.cs)
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),

                    ValidateIssuer = true,
                    ValidIssuer = _config["Jwt:Issuer"],

                    ValidateAudience = true,
                    ValidAudience = _config["Jwt:Audience"],

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,

                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.Name
                }, out var _);

                // 3) הוצאת ה־Claims המבוקשים
                var email = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
                var role = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(role))
                    return Unauthorized();

                return Ok(new { email, role });
            }
            catch (SecurityTokenException)
            {
                return Unauthorized();
            }
            //תמיד מחזיר תשובה שלילית כנראה. לברר למה
        }
    }
}

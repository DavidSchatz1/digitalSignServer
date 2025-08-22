using DigitalSignServer.services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DigitalSignServer.controller
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        public AuthController(IAuthService authService) => _authService = authService;

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var token = await _authService.AuthenticateAsync(request.Username, request.Password);
            if (token == null) return Unauthorized(new { message = "wrong password or mail" });

            Response.Cookies.Append("jwt", token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,               
                SameSite = SameSiteMode.None, 
                Expires = DateTime.UtcNow.AddHours(2),
                Path = "/"
            });
            return Ok();
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            // למחיקה חייבים להתאים את המאפיינים (Path/SameSite/Secure/Domain אם הוגדר)
            Response.Cookies.Delete("jwt", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,                 // תואם ליצירה (ב־DEV אפשר false)
                SameSite = SameSiteMode.None,
                Path = "/"
            });
            return Ok();
        }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
}


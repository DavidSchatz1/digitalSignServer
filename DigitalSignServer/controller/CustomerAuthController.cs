using DigitalSignServer.models;
using DigitalSignServer.services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DigitalSignServer.controller
{
    [Route("api/[controller]")]
    [ApiController]
    public class CustomerAuthController : ControllerBase
    {
        private readonly ICustomerAuthService _authService;
        public CustomerAuthController(ICustomerAuthService authService) => _authService = authService;

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] CustomerLoginRequest request)
        {
            var token = await _authService.AuthenticateAsync(request.Email, request.Password);
            if (token == null) return Unauthorized("Invalid credentials");

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
            Response.Cookies.Delete("jwt", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/"
            });
            return Ok();
        }
    }

    public class CustomerLoginRequest
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
    }
}


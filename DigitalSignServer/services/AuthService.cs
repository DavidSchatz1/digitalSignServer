
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DigitalSignServer.Reposetories;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;
using DigitalSignServer.models;

namespace DigitalSignServer.services
{
    public interface IAuthService
    {
        Task<string?> AuthenticateAsync(string username, string password);
    }

    public class AuthService : IAuthService
    {
        private readonly ILawyerRepository _lawyerRepo;
        private readonly IConfiguration _config;
        private readonly PasswordHasher<Lawyer> _hasher;

        public AuthService(ILawyerRepository lawyerRepo, IConfiguration config)
        {
            _lawyerRepo = lawyerRepo;
            _config = config;
            _hasher = new PasswordHasher<Lawyer>();
        }

        public async Task<string?> AuthenticateAsync(string username, string password)
        {
            var lawyer = await _lawyerRepo.GetByUsernameAsync(username);
            if (lawyer == null) return null;

            var result = _hasher.VerifyHashedPassword(lawyer, lawyer.PasswordHash, password);
            if (result != PasswordVerificationResult.Success)
                return null;

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: new[] { new Claim(ClaimTypes.Name, lawyer.Email), new Claim(ClaimTypes.Role, "Admin") }, //make sure the claim role works
                expires: DateTime.Now.AddHours(2),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}

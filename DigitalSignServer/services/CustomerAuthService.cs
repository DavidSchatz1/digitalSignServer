using DigitalSignServer.models;
using DigitalSignServer.Reposetories;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace DigitalSignServer.services
{
    public interface ICustomerAuthService
    {
        Task<string?> AuthenticateAsync(string email, string password);
    }

    public class CustomerAuthService : ICustomerAuthService
    {
        private readonly ICustomerAuthRepository _customerRepo;
        private readonly IConfiguration _config;
        private readonly PasswordHasher<Customer> _hasher;

        public CustomerAuthService(ICustomerAuthRepository customerRepo, IConfiguration config)
        {
            _customerRepo = customerRepo;
            _config = config;
            _hasher = new PasswordHasher<Customer>();
        }

        public async Task<string?> AuthenticateAsync(string email, string password)
        {
            var customer = await _customerRepo.GetByEmailAsync(email);
            if (customer == null) return null;

            if (_hasher.VerifyHashedPassword(customer, customer.Password, password) != PasswordVerificationResult.Success)
                return null;

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, customer.Email),
                new Claim(ClaimTypes.Role, "Customer"),
                new Claim("CustomerId", customer.Id.ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

    }
}

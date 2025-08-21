using DigitalSignServer.context;
using DigitalSignServer.models;
using Microsoft.EntityFrameworkCore;

namespace DigitalSignServer.Reposetories
{
    public class CustomerAuthRepository : ICustomerAuthRepository
    {
        private readonly AppDbContext _context;

        public CustomerAuthRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<Customer?> GetByEmailAsync(string email)
        {
            return await _context.customers.FirstOrDefaultAsync(c => c.Email == email);
        }
    }

}

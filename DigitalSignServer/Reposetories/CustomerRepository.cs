using DigitalSignServer.context;
using DigitalSignServer.models;
using Microsoft.EntityFrameworkCore;

namespace DigitalSignServer.Reposetories
{
    public class CustomerRepository : ICustomerRepository
    {
        private readonly AppDbContext _context;

        public CustomerRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<Customer?> GetByEmailAsync(string email)
        {
            return await _context.customers.FirstOrDefaultAsync(c => c.Email == email);
        }

        public async Task<Customer> AddAsync(Customer customer)
        {
            Console.Write(customer);
            _context.customers.Add(customer);
            await _context.SaveChangesAsync();
            return customer;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var customer = await _context.customers.FindAsync(id);
            if (customer == null) return false;

            _context.customers.Remove(customer);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<CustomerDTO>> GetAllAsync()
        {
            return await _context.customers
                .Select(c => new CustomerDTO
                {
                    Id = c.Id,
                    Email = c.Email,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                })
                .ToListAsync();
        }

    }
}

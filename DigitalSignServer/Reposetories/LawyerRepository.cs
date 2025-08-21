using DigitalSignServer.context;
using DigitalSignServer.models;
using Microsoft.EntityFrameworkCore;

namespace DigitalSignServer.Reposetories
{
    public interface ILawyerRepository
    {
        Task<Lawyer?> GetByUsernameAsync(string username);
    }

    public class LawyerRepository : ILawyerRepository
    {
        private readonly AppDbContext _context;

        public LawyerRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<Lawyer?> GetByUsernameAsync(string email)
        {
            return await _context.Lawyers.FirstOrDefaultAsync(l => l.Email == email);
        }
    }

}

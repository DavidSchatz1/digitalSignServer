using DigitalSignServer.models;

namespace DigitalSignServer.Reposetories
{
    public interface ICustomerRepository
    {
        Task<Customer?> GetByEmailAsync(string email);
        Task<Customer> AddAsync(Customer customer);
        Task<bool> DeleteAsync(Guid id);

        Task<List<CustomerDTO>> GetAllAsync();
    }
}

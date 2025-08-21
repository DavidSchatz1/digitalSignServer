using DigitalSignServer.models;

namespace DigitalSignServer.Reposetories
{
    public interface ICustomerAuthRepository
    {
        Task<Customer?> GetByEmailAsync(string email);
    }

}

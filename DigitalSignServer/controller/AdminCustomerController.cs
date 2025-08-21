using DigitalSignServer.models;
using DigitalSignServer.Reposetories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DigitalSignServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class AdminCustomerController : ControllerBase
    {
        private readonly ICustomerRepository _customerRepo;
        private readonly PasswordHasher<Customer> _hasher;

        public AdminCustomerController(ICustomerRepository customerRepo)
        {
            _customerRepo = customerRepo;
            _hasher = new PasswordHasher<Customer>();
        }

        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] Customer customer)
        {
            if (await _customerRepo.GetByEmailAsync(customer.Email) != null)
                return BadRequest("Customer with this email already exists");

            customer.Password = _hasher.HashPassword(customer, customer.Password);
            customer.CreatedAt = DateTime.UtcNow;
            customer.UpdatedAt = DateTime.UtcNow;

            var created = await _customerRepo.AddAsync(customer);
            return Ok(new { created.Id, created.Email });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var success = await _customerRepo.DeleteAsync(id);
            if (!success) return NotFound("Customer not found");

            return Ok("Customer deleted");
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var customers = await _customerRepo.GetAllAsync();
            return Ok(customers);
        }

    }
}

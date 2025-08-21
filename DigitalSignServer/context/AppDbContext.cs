using DigitalSignServer.models;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace DigitalSignServer.context
{
    public class AppDbContext: DbContext
    {
      
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Lawyer> Lawyers { get; set; }

        public DbSet<Customer> customers { get; set; }


    }
}

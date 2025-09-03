using DigitalSignServer.models;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using DigitalSignServer.Models;

namespace DigitalSignServer.context
{
    public class AppDbContext: DbContext
    {
      
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Lawyer> Lawyers { get; set; }

        public DbSet<Customer> customers { get; set; }

        public DbSet<Template> Templates => Set<Template>();
        public DbSet<TemplateField> TemplateFields => Set<TemplateField>();

        public DbSet<TemplateInstance> TemplateInstances => Set<TemplateInstance>();
        public DbSet<SignatureInvite> SignatureInvites { get; set; } = default!;
        public DbSet<SignatureDelivery> SignatureDeliveries { get; set; } = default!;

        public DbSet<TemplateSignatureAnchor> TemplateSignatureAnchors { get; set; } = null!;
        public DbSet<TemplateInstanceSignatureSlot> TemplateInstanceSignatureSlots { get; set; } = null!;

        public DbSet<SignatureAuditEvent> SignatureAuditEvents => Set<SignatureAuditEvent>();




        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);


            modelBuilder.Entity<Template>(e =>
            {
                e.HasIndex(x => x.CustomerId);
                e.Property(x => x.OriginalFileName).HasMaxLength(255);
                e.Property(x => x.S3Key).HasMaxLength(1024);
                e.Property(x => x.MimeType).HasMaxLength(255);
                e.HasMany(x => x.Fields)
                .WithOne(f => f.Template)
                .HasForeignKey(f => f.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
            });


            modelBuilder.Entity<TemplateField>(e =>
            {
                e.HasIndex(x => x.TemplateId);
                e.Property(x => x.Key).HasMaxLength(128);
                e.Property(x => x.Type).HasMaxLength(32);
                e.Property(x => x.Label).HasMaxLength(256);
            });

        }
    }
}

using Microsoft.EntityFrameworkCore;
using ParcelAPI.Models;

namespace ParcelAPI.Data
{
    public class ParcelContext : DbContext
    {
        public ParcelContext(DbContextOptions<ParcelContext> options)
            : base(options)
        {
        }

        public DbSet<Client> Clients { get; set; }
        public DbSet<MpesaStkStatus> MpesaStkStatuses { get; set; }
        public DbSet<EtimsSettings> EtimsSettings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Client configuration - maps to existing Clients table
            modelBuilder.Entity<Client>(entity =>
            {
                entity.ToTable("Clients");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ClientCode).HasColumnName("Client Code").HasMaxLength(50).IsRequired();
                entity.Property(e => e.ClientName).HasColumnName("Client Name").HasMaxLength(100);
                entity.Property(e => e.LogPath).HasColumnName("Log Path");
                entity.HasIndex(e => e.ClientCode).IsUnique();
            });

            modelBuilder.Entity<MpesaStkStatus>(entity =>
            {
                entity.ToTable("MpesaStkStatuses");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.CheckoutRequestId).HasMaxLength(50).IsRequired();
                entity.Property(e => e.MerchantRequestId).HasMaxLength(50);
                entity.Property(e => e.ResultDescription).HasMaxLength(250);
                entity.Property(e => e.Amount).HasPrecision(18, 2);
                entity.Property(e => e.MpesaReceiptNumber).HasMaxLength(50);
                entity.Property(e => e.PhoneNumber).HasMaxLength(20);
                entity.Property(e => e.Reference).HasMaxLength(50);
                entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
                entity.HasIndex(e => e.CheckoutRequestId).IsUnique();
            });

            modelBuilder.Entity<EtimsSettings>(entity =>
            {
                entity.ToTable("EtimsSettings");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ClientCode).HasMaxLength(50).IsRequired();
                entity.Property(e => e.TinPin).HasMaxLength(20).IsRequired();
                entity.Property(e => e.BranchId).HasMaxLength(10);
                entity.Property(e => e.DeviceSerialNo).HasMaxLength(100);
                entity.Property(e => e.ApiUsername).HasMaxLength(100);
                entity.Property(e => e.ApiPassword).HasMaxLength(200);
                entity.Property(e => e.Environment).HasMaxLength(20);
                entity.HasIndex(e => e.ClientCode);
            });


        }
    }
}
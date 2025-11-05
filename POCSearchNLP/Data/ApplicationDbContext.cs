using Microsoft.EntityFrameworkCore;
using POCSearchNLP.Models;

namespace POCSearchNLP.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Make> Makes { get; set; }
        public DbSet<Model> Models { get; set; }
        public DbSet<PartsInfo> PartsInfo { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure unique constraints
            modelBuilder.Entity<Make>()
                .HasIndex(m => m.Name)
                .IsUnique();

            modelBuilder.Entity<Model>()
                .HasIndex(m => new { m.MakeID, m.Name, m.YearFrom, m.YearTo })
                .IsUnique();

            modelBuilder.Entity<PartsInfo>()
                .HasIndex(p => new { p.ModelID, p.PartNumber })
                .IsUnique();

            // Configure relationships
            modelBuilder.Entity<Model>()
                .HasOne(m => m.Make)
                .WithMany(make => make.Models)
                .HasForeignKey(m => m.MakeID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PartsInfo>()
                .HasOne(p => p.Model)
                .WithMany(model => model.PartsInfo)
                .HasForeignKey(p => p.ModelID)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
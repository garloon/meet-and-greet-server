using MeetAndGreet.API.Models;
using Microsoft.EntityFrameworkCore;

namespace MeetAndGreet.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {

        }

        public DbSet<User> Users { get; set; }
        public DbSet<Channel> Channels { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<Avatar> Avatars { get; set; }
        public DbSet<TrustedDevice> TrustedDevices { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Message>(entity =>
            {
                entity.HasIndex(m => m.Timestamp);
                entity.Property(m => m.Content).HasMaxLength(100);
            });
            
            base.OnModelCreating(modelBuilder);
        }
    }
}

using FileConversionTool.Models;
using Microsoft.EntityFrameworkCore;

namespace FileConversionTool.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ResourceDownload> ResourceDownloads { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ResourceDownload>(entity =>
        {
            entity.HasKey(e => e.ID);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.FilePath).IsRequired();
        });
    }
}

using Microsoft.EntityFrameworkCore;

namespace MedorSklad.Data;

public class WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : DbContext(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockItem> StockItems => Set<StockItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200);
            e.HasIndex(c => c.Name).IsUnique();
            e.HasMany(c => c.Categories)
                .WithMany(c => c.Clients)
                .UsingEntity(j => j.ToTable("ClientCategories"));
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200);
            e.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200);
            e.HasIndex(p => p.Name).IsUnique();
            e.HasMany(p => p.Categories)
                .WithMany(c => c.Products)
                .UsingEntity(j => j.ToTable("ProductCategories"));
        });

        modelBuilder.Entity<StockItem>(e =>
        {
            e.HasKey(s => new { s.ClientId, s.ProductId });
            e.HasOne(s => s.Client).WithMany(c => c.StockItems).HasForeignKey(s => s.ClientId);
            e.HasOne(s => s.Product).WithMany().HasForeignKey(s => s.ProductId);
            // Dnešní zápis (ExecuteUpdate) hlídá verzi sám ve WHERE; token chrání případný budoucí zápis přes SaveChanges.
            e.Property(s => s.Version).IsConcurrencyToken();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_StockItems_Quantity", "Quantity >= 0");
                t.HasCheckConstraint("CK_StockItems_Quantity_Max", "Quantity <= 1000000");
            });
        });
    }
}

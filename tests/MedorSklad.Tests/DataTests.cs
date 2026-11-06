using MedorSklad.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MedorSklad.Tests;

/// <summary>Testy migrace a seedu: stejná konfigurace jako v Program.cs, jen nad databází v paměti.</summary>
public sealed class DataTests
{
    /// <summary>Počet skladových řádků v seedu, uvedený i v README.</summary>
    private const int ExpectedStockRows = 120;

    private static WarehouseDbContext SeededContext(SqliteConnection connection)
    {
        var db = new WarehouseDbContext(new DbContextOptionsBuilder<WarehouseDbContext>()
            .UseSqlite(connection)
            .UseSeeding((ctx, _) => DbSeeder.Seed((WarehouseDbContext)ctx))
            .Options);
        db.Database.Migrate(); // spustí i UseSeeding
        return db;
    }

    private static SqliteConnection OpenMemoryConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    [Fact]
    public void Model_has_no_pending_changes()
    {
        using var db = new WarehouseDbContext(new DbContextOptionsBuilder<WarehouseDbContext>().UseSqlite("Data Source=:memory:").Options);

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Seeding_twice_does_not_duplicate()
    {
        using var connection = OpenMemoryConnection();
        using var first = SeededContext(connection);
        var counts = (first.Clients.Count(), first.Products.Count(), first.StockItems.Count());

        // Druhé spuštění aplikace nad stejnou databází.
        using var second = SeededContext(connection);

        Assert.Equal(counts, (second.Clients.Count(), second.Products.Count(), second.StockItems.Count()));
    }

    [Fact]
    public void Seed_is_deterministic()
    {
        using var connectionA = OpenMemoryConnection();
        using var connectionB = OpenMemoryConnection();
        using var a = SeededContext(connectionA);
        using var b = SeededContext(connectionB);

        var stockA = a.StockItems.Select(s => new { s.ClientId, s.ProductId, s.Quantity }).ToList();
        var stockB = b.StockItems.Select(s => new { s.ClientId, s.ProductId, s.Quantity }).ToList();

        Assert.Equal(ExpectedStockRows, stockA.Count);
        Assert.Equal(stockA.OrderBy(s => s.ClientId).ThenBy(s => s.ProductId), stockB.OrderBy(s => s.ClientId).ThenBy(s => s.ProductId));
        Assert.Contains(stockA, s => s.Quantity == 0);
    }

    [Fact]
    public void Every_assortment_pair_has_a_stock_row()
    {
        using var connection = OpenMemoryConnection();
        using var db = SeededContext(connection);

        var assortment = db.Clients
            .SelectMany(c => c.Categories.SelectMany(cat => cat.Products), (c, p) => new { ClientId = c.Id, ProductId = p.Id })
            .Distinct()
            .ToHashSet();
        var stock = db.StockItems.Select(s => new { s.ClientId, s.ProductId }).ToHashSet();

        Assert.Equal(assortment, stock);
    }
}

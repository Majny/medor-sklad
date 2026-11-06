using MedorSklad.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MedorSklad.Tests;

/// <summary>
/// SQLite databáze v paměti, která žije, dokud je otevřené spojení. Schéma vzniká skutečnou
/// migrací, testovací data si každý test staví sám (bez UseSeeding).
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestDb()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.Migrate();
    }

    /// <summary>Nový kontext nad stejnou databází, například pro simulaci druhého uživatele.</summary>
    public WarehouseDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WarehouseDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}

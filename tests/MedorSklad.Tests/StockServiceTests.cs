using MedorSklad.Contracts;
using MedorSklad.Data;
using MedorSklad.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MedorSklad.Tests;

/// <summary>Testy nad skutečnou SQLite databází v paměti s malou, ručně postavenou sadou dat.</summary>
public sealed class StockServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WarehouseDbContext _db;
    private readonly StockService _service;

    // Kategorie: Elektro, Kancelář, Hračky
    // Produkty:  Myš (Elektro + Kancelář), Papír (Kancelář), Baterie (Elektro + Hračky), Medvěd (Hračky)
    // Klienti:   Alfa (Elektro + Kancelář), Beta (Hračky)
    private readonly Category _elektro = new() { Name = "Elektro" };
    private readonly Category _kancelar = new() { Name = "Kancelář" };
    private readonly Category _hracky = new() { Name = "Hračky" };
    private readonly Product _mys;
    private readonly Product _papir;
    private readonly Product _baterie;
    private readonly Product _medved;
    private readonly Client _alfa;
    private readonly Client _beta;

    public StockServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new WarehouseDbContext(new DbContextOptionsBuilder<WarehouseDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _mys = new Product { Name = "Myš", Categories = [_elektro, _kancelar] };
        _papir = new Product { Name = "Papír", Categories = [_kancelar] };
        _baterie = new Product { Name = "Baterie", Categories = [_elektro, _hracky] };
        _medved = new Product { Name = "Medvěd", Categories = [_hracky] };
        _alfa = new Client { Name = "Alfa", Categories = [_elektro, _kancelar] };
        _beta = new Client { Name = "Beta", Categories = [_hracky] };

        _db.AddRange(_mys, _papir, _baterie, _medved, _alfa, _beta);
        _db.StockItems.AddRange(
            new StockItem { Client = _alfa, Product = _mys, Quantity = 5 },
            new StockItem { Client = _alfa, Product = _baterie, Quantity = 7 },
            new StockItem { Client = _beta, Product = _baterie, Quantity = 3 });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        _service = new StockService(_db);
    }

    private static StockFilter Filter(int[]? clients = null, int[]? categories = null, int[]? products = null) =>
        new(clients ?? [], categories ?? [], products ?? []);

    [Fact]
    public async Task Grid_contains_only_products_from_categories_assigned_to_client()
    {
        var rows = await _service.GetStockAsync(Filter());

        Assert.Equal(
            ["Alfa|Baterie", "Alfa|Myš", "Alfa|Papír", "Beta|Baterie", "Beta|Medvěd"],
            rows.Select(r => $"{r.ClientName}|{r.ProductName}"));
    }

    [Fact]
    public async Task Product_in_two_client_categories_is_one_row_listing_both_categories()
    {
        var rows = await _service.GetStockAsync(Filter(clients: [_alfa.Id]));

        var mys = Assert.Single(rows, r => r.ProductId == _mys.Id);
        Assert.Equal(["Elektro", "Kancelář"], mys.Categories);
        Assert.Equal(5, mys.Quantity);
    }

    [Fact]
    public async Task Categories_column_shows_only_categories_the_client_handles()
    {
        var rows = await _service.GetStockAsync(Filter(products: [_baterie.Id]));

        Assert.Equal(["Elektro"], rows.Single(r => r.ClientId == _alfa.Id).Categories);
        Assert.Equal(["Hračky"], rows.Single(r => r.ClientId == _beta.Id).Categories);
    }

    [Fact]
    public async Task Each_client_has_its_own_stock_for_shared_product()
    {
        var rows = await _service.GetStockAsync(Filter(products: [_baterie.Id]));

        Assert.Equal(7, rows.Single(r => r.ClientId == _alfa.Id).Quantity);
        Assert.Equal(3, rows.Single(r => r.ClientId == _beta.Id).Quantity);
    }

    [Fact]
    public async Task Missing_stock_record_is_shown_as_zero()
    {
        var rows = await _service.GetStockAsync(Filter(products: [_papir.Id]));

        Assert.Equal(0, Assert.Single(rows).Quantity);
    }

    [Fact]
    public async Task Category_filter_keeps_products_from_selected_category()
    {
        var rows = await _service.GetStockAsync(Filter(categories: [_kancelar.Id]));

        Assert.Equal(["Alfa|Myš", "Alfa|Papír"], rows.Select(r => $"{r.ClientName}|{r.ProductName}"));
    }

    [Fact]
    public async Task Filters_are_combined()
    {
        var rows = await _service.GetStockAsync(Filter(clients: [_beta.Id], categories: [_hracky.Id], products: [_medved.Id]));

        Assert.Equal("Medvěd", Assert.Single(rows).ProductName);
    }

    [Fact]
    public async Task Update_changes_existing_quantity()
    {
        var result = await _service.UpdateQuantityAsync(_alfa.Id, _mys.Id, 42);

        Assert.Equal(UpdateStockResult.Updated, result);
        Assert.Equal(42, (await _db.StockItems.FindAsync(_alfa.Id, _mys.Id))!.Quantity);
    }

    [Fact]
    public async Task Update_creates_missing_stock_record()
    {
        var result = await _service.UpdateQuantityAsync(_alfa.Id, _papir.Id, 10);

        Assert.Equal(UpdateStockResult.Updated, result);
        var row = Assert.Single(await _service.GetStockAsync(Filter(clients: [_alfa.Id], products: [_papir.Id])));
        Assert.Equal(10, row.Quantity);
    }

    [Fact]
    public async Task Update_rejects_product_outside_client_assortment()
    {
        var result = await _service.UpdateQuantityAsync(_beta.Id, _papir.Id, 10);

        Assert.Equal(UpdateStockResult.ProductNotInClientAssortment, result);
        Assert.Null(await _db.StockItems.FindAsync(_beta.Id, _papir.Id));
    }

    [Fact]
    public async Task Update_reports_unknown_client_or_product()
    {
        Assert.Equal(UpdateStockResult.ClientOrProductNotFound, await _service.UpdateQuantityAsync(999, _mys.Id, 1));
        Assert.Equal(UpdateStockResult.ClientOrProductNotFound, await _service.UpdateQuantityAsync(_alfa.Id, 999, 1));
    }

    [Fact]
    public async Task Update_rejects_negative_quantity()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.UpdateQuantityAsync(_alfa.Id, _mys.Id, -1));
    }

    [Fact]
    public void Seeder_fills_empty_database_and_is_idempotent()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new WarehouseDbContext(new DbContextOptionsBuilder<WarehouseDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();

        DbSeeder.Seed(db);
        var stockCount = db.StockItems.Count();
        DbSeeder.Seed(db);

        Assert.True(db.Clients.Any());
        Assert.True(stockCount > 0);
        Assert.Equal(stockCount, db.StockItems.Count());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}

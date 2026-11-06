using MedorSklad.Contracts;
using MedorSklad.Data;
using MedorSklad.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedorSklad.Tests;

/// <summary>Testy doménové logiky nad skutečnou SQLite databází v paměti s malou, ručně postavenou sadou dat.</summary>
public sealed class StockServiceTests : IDisposable
{
    private readonly TestDb _testDb = new();
    private readonly WarehouseDbContext _db;
    private readonly StockService _service;

    // Kategorie: Elektro, Kancelář, Hračky
    // Produkty:  Myš (Elektro + Kancelář), Papír (Kancelář), Baterie (Elektro + Hračky), Medvěd (Hračky)
    // Klienti:   Alfa (Elektro + Kancelář), Beta (Hračky)
    // Sklad:     řádek pro každou dvojici ze sortimentu, Alfa/Papír je vyprodaný.
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
        _db = _testDb.CreateContext();

        _mys = new Product { Name = "Myš", Categories = [_elektro, _kancelar] };
        _papir = new Product { Name = "Papír", Categories = [_kancelar] };
        _baterie = new Product { Name = "Baterie", Categories = [_elektro, _hracky] };
        _medved = new Product { Name = "Medvěd", Categories = [_hracky] };
        _alfa = new Client { Name = "Alfa", Categories = [_elektro, _kancelar] };
        _beta = new Client { Name = "Beta", Categories = [_hracky] };

        _db.AddRange(_mys, _papir, _baterie, _medved, _alfa, _beta);
        _db.StockItems.AddRange(
            new StockItem { Client = _alfa, Product = _mys, Quantity = 5 },
            new StockItem { Client = _alfa, Product = _papir, Quantity = 0 },
            new StockItem { Client = _alfa, Product = _baterie, Quantity = 7 },
            new StockItem { Client = _beta, Product = _baterie, Quantity = 3 },
            new StockItem { Client = _beta, Product = _medved, Quantity = 4 });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        _service = CreateService(_db);
    }

    private static StockService CreateService(WarehouseDbContext db) => new(db, NullLogger<StockService>.Instance);

    private static StockFilter Filter(int[]? clients = null, int[]? categories = null, int[]? products = null, bool onlyOutOfStock = false) =>
        new(clients ?? [], categories ?? [], products ?? [], onlyOutOfStock);

    private static IEnumerable<string> Labels(IEnumerable<StockRow> rows) => rows.Select(r => $"{r.ClientName}|{r.ProductName}");

    [Fact]
    public async Task Grid_contains_only_products_from_categories_assigned_to_client()
    {
        var rows = await _service.GetStockAsync(Filter());

        Assert.Equal(["Alfa|Baterie", "Alfa|Myš", "Alfa|Papír", "Beta|Baterie", "Beta|Medvěd"], Labels(rows));
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
    public async Task Seeded_zero_quantity_is_shown_as_zero()
    {
        var rows = await _service.GetStockAsync(Filter(products: [_papir.Id]));

        Assert.Equal(0, Assert.Single(rows).Quantity);
    }

    [Fact]
    public async Task Category_filter_keeps_products_from_selected_category()
    {
        var rows = await _service.GetStockAsync(Filter(categories: [_kancelar.Id]));

        Assert.Equal(["Alfa|Myš", "Alfa|Papír"], Labels(rows));
    }

    [Fact]
    public async Task Category_filter_with_two_selected_categories_returns_multi_category_product_once()
    {
        var rows = await _service.GetStockAsync(Filter(categories: [_elektro.Id, _kancelar.Id]));

        Assert.Equal(["Alfa|Baterie", "Alfa|Myš", "Alfa|Papír"], Labels(rows));
    }

    [Fact]
    public async Task Filters_are_combined()
    {
        var rows = await _service.GetStockAsync(Filter(clients: [_beta.Id], categories: [_hracky.Id], products: [_medved.Id]));

        Assert.Equal("Medvěd", Assert.Single(rows).ProductName);
    }

    [Fact]
    public async Task Only_out_of_stock_filter_returns_zero_rows()
    {
        var rows = await _service.GetStockAsync(Filter(onlyOutOfStock: true));

        Assert.Equal(["Alfa|Papír"], Labels(rows));
    }

    [Fact]
    public async Task Rows_are_sorted_in_czech_order()
    {
        // Č patří za C, Ch za H a Š za S; binární řazení by dalo jiné pořadí.
        var potraviny = new Category { Name = "Potraviny" };
        var gama = new Client { Name = "Gama", Categories = [potraviny] };
        foreach (var name in new[] { "Šunka", "Chléb", "Sůl", "Čaj", "Hrnek", "Cukr" })
            gama.StockItems.Add(new StockItem { Product = new Product { Name = name, Categories = [potraviny] } });
        _db.Add(gama);
        await _db.SaveChangesAsync();

        var rows = await _service.GetStockAsync(Filter(clients: [gama.Id]));

        Assert.Equal(["Cukr", "Čaj", "Hrnek", "Chléb", "Sůl", "Šunka"], rows.Select(r => r.ProductName));
    }

    [Fact]
    public async Task Rows_of_category_removed_from_client_are_hidden_but_kept()
    {
        var alfa = await _db.Clients.Include(c => c.Categories).SingleAsync(c => c.Id == _alfa.Id);
        alfa.Categories.RemoveAll(c => c.Id == _kancelar.Id);
        await _db.SaveChangesAsync();

        var rows = await _service.GetStockAsync(Filter(clients: [_alfa.Id]));

        // Myš zůstává díky kategorii Elektro, Papír zmizí z gridu, ale jeho řádek v DB zůstane.
        Assert.Equal(["Alfa|Baterie", "Alfa|Myš"], Labels(rows));
        Assert.Equal(["Elektro"], rows.Single(r => r.ProductId == _mys.Id).Categories);
        Assert.NotNull(await _db.StockItems.FindAsync(_alfa.Id, _papir.Id));
    }

    [Fact]
    public async Task Filter_options_include_client_and_product_category_ids()
    {
        var options = await _service.GetFilterOptionsAsync();

        Assert.Equal(["Alfa", "Beta"], options.Clients.Select(c => c.Name));
        Assert.Equal(["Elektro", "Hračky", "Kancelář"], options.Categories.Select(c => c.Name));
        Assert.Equivalent(new[] { _elektro.Id, _kancelar.Id }, options.Clients.Single(c => c.Id == _alfa.Id).CategoryIds);
        Assert.Equivalent(new[] { _elektro.Id, _hracky.Id }, options.Products.Single(p => p.Id == _baterie.Id).CategoryIds);
    }

    [Fact]
    public async Task Update_saves_new_quantity_to_database()
    {
        var result = await _service.UpdateQuantityAsync(_alfa.Id, _mys.Id, 42, expectedVersion: 0);

        Assert.IsType<UpdateStockResult.Updated>(result);
        Assert.Equal(42, (await _db.StockItems.AsNoTracking().SingleAsync(s => s.ClientId == _alfa.Id && s.ProductId == _mys.Id)).Quantity);
    }

    [Fact]
    public async Task Update_with_current_version_increments_version_and_returns_row()
    {
        var result = await _service.UpdateQuantityAsync(_alfa.Id, _mys.Id, 42, expectedVersion: 0);

        var row = Assert.IsType<UpdateStockResult.Updated>(result).Row;
        Assert.Equal(42, row.Quantity);
        Assert.Equal(1, row.Version);
        Assert.Equal(["Elektro", "Kancelář"], row.Categories);
    }

    [Fact]
    public async Task Update_with_stale_version_returns_conflict_with_current_values()
    {
        // Dva uživatelé s vlastním kontextem viděli stejnou verzi 0.
        using var dbA = _testDb.CreateContext();
        using var dbB = _testDb.CreateContext();

        await CreateService(dbA).UpdateQuantityAsync(_alfa.Id, _mys.Id, 10, expectedVersion: 0);
        var result = await CreateService(dbB).UpdateQuantityAsync(_alfa.Id, _mys.Id, 20, expectedVersion: 0);

        Assert.Equal(new UpdateStockResult.Conflict(CurrentQuantity: 10, CurrentVersion: 1), result);
        Assert.Equal(10, (await _service.GetStockAsync(Filter(clients: [_alfa.Id], products: [_mys.Id]))).Single().Quantity);
    }

    [Fact]
    public async Task Update_rejects_product_outside_client_assortment()
    {
        var result = await _service.UpdateQuantityAsync(_beta.Id, _papir.Id, 10, expectedVersion: 0);

        Assert.IsType<UpdateStockResult.NotInAssortment>(result);
        Assert.Null(await _db.StockItems.FindAsync(_beta.Id, _papir.Id));
    }

    [Fact]
    public async Task Update_reports_unknown_client_or_product()
    {
        Assert.IsType<UpdateStockResult.NotFound>(await _service.UpdateQuantityAsync(999, _mys.Id, 1, expectedVersion: 0));
        Assert.IsType<UpdateStockResult.NotFound>(await _service.UpdateQuantityAsync(_alfa.Id, 999, 1, expectedVersion: 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void Check_constraint_rejects_quantity_out_of_range_written_directly(int quantity)
    {
        // Poslední pojistka v databázi pro zápisy mimo API.
        Assert.Throws<SqliteException>(() => _db.Database.ExecuteSql(
            $"UPDATE StockItems SET Quantity = {quantity} WHERE ClientId = {_alfa.Id} AND ProductId = {_mys.Id}"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _testDb.Dispose();
    }
}

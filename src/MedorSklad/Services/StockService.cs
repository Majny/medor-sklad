using System.Globalization;
using MedorSklad.Contracts;
using MedorSklad.Data;
using Microsoft.EntityFrameworkCore;

namespace MedorSklad.Services;

/// <summary>
/// Doménová logika skladu. Sortiment klienta tvoří všechny produkty z kategorií,
/// které má klient přidělené. Řádek gridu = jeden produkt na skladu jednoho klienta.
/// </summary>
public class StockService(WarehouseDbContext db, ILogger<StockService> logger)
{
    /// <summary>Řazení podle české abecedy nezávisle na jazyku serveru (SQLite umí jen binární řazení).</summary>
    private static readonly StringComparer CzechOrder = StringComparer.Create(new CultureInfo("cs-CZ"), ignoreCase: true);

    public async Task<FilterOptions> GetFilterOptionsAsync(CancellationToken ct = default)
    {
        var clients = await db.Clients
            .Select(c => new ClientOption(c.Id, c.Name, c.Categories.Select(x => x.Id).ToList()))
            .ToListAsync(ct);
        var categories = await db.Categories
            .Select(c => new LookupItem(c.Id, c.Name))
            .ToListAsync(ct);
        var products = await db.Products
            .Select(p => new ProductOption(p.Id, p.Name, p.Categories.Select(x => x.Id).ToList()))
            .ToListAsync(ct);

        return new FilterOptions(
            clients.OrderBy(c => c.Name, CzechOrder).ToList(),
            categories.OrderBy(c => c.Name, CzechOrder).ToList(),
            products.OrderBy(p => p.Name, CzechOrder).ToList());
    }

    public async Task<IReadOnlyList<StockRow>> GetStockAsync(StockFilter filter, CancellationToken ct = default)
    {
        // Jen řádky, jejichž produkt je v sortimentu klienta. Řádky kategorie, kterou klient
        // ztratil, v databázi zůstávají, ale v gridu se nezobrazí.
        var query = db.StockItems.Where(s => s.Client.Categories.Any(cat => cat.Products.Any(p => p.Id == s.ProductId)));

        if (filter.ClientIds.Count > 0)
            query = query.Where(s => filter.ClientIds.Contains(s.ClientId));
        if (filter.ProductIds.Count > 0)
            query = query.Where(s => filter.ProductIds.Contains(s.ProductId));

        // Řádek zůstane, pokud produkt patří aspoň do jedné vybrané kategorie, kterou klient má.
        if (filter.CategoryIds.Count > 0)
            query = query.Where(s => s.Product.Categories.Any(cat =>
                filter.CategoryIds.Contains(cat.Id) && cat.Clients.Any(cl => cl.Id == s.ClientId)));
        if (filter.OnlyOutOfStock)
            query = query.Where(s => s.Quantity == 0);

        // Projekce do DTO se nesleduje, proto tu není AsNoTracking. Categories je zatím prázdné, doplní se níže.
        var rows = await query
            .Select(s => new StockRow(s.ClientId, s.Client.Name, s.ProductId, s.Product.Name, new List<string>(), s.Quantity, s.Version))
            .ToListAsync(ct);

        // Kategorie řádků načteme druhým dotazem. Vnořenou kolekci závislou na klientovi řádku
        // by EF přeložil jen přes SQL APPLY, který SQLite nepodporuje.
        var clientIds = rows.Select(r => r.ClientId).Distinct().ToList();
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var categoryNames = (await db.Clients
                .Where(c => clientIds.Contains(c.Id))
                .SelectMany(c => c.Categories, (c, cat) => new { ClientId = c.Id, Category = cat })
                .SelectMany(x => x.Category.Products.Where(p => productIds.Contains(p.Id)),
                    (x, p) => new { x.ClientId, ProductId = p.Id, x.Category.Name })
                .ToListAsync(ct))
            .ToLookup(t => (t.ClientId, t.ProductId), t => t.Name);

        // České řazení až v paměti, řádků je málo.
        return rows
            .Select(r => r with { Categories = categoryNames[(r.ClientId, r.ProductId)].Order(CzechOrder).ToList() })
            .OrderBy(r => r.ClientName, CzechOrder)
            .ThenBy(r => r.ProductName, CzechOrder)
            .ToList();
    }

    public async Task<UpdateStockResult> UpdateQuantityAsync(
        int clientId, int productId, int quantity, int expectedVersion, CancellationToken ct = default)
    {
        var clientExists = await db.Clients.AnyAsync(c => c.Id == clientId, ct);
        var productExists = await db.Products.AnyAsync(p => p.Id == productId, ct);
        if (!clientExists || !productExists)
            return new UpdateStockResult.NotFound();

        var inAssortment = await db.Clients
            .Where(c => c.Id == clientId)
            .SelectMany(c => c.Categories)
            .AnyAsync(cat => cat.Products.Any(p => p.Id == productId), ct);
        if (!inAssortment)
            return new UpdateStockResult.NotInAssortment();

        // Jeden SQL příkaz UPDATE ... WHERE Version = @expected. ExecuteUpdate obchází change tracker
        // i jeho concurrency tokeny, proto je verze přímo v podmínce. Příkaz je atomický bez transakce
        // a nikdy nevkládá nový řádek, takže souběžné požadavky nemůžou narazit na duplicitní klíč.
        var updated = await db.StockItems
            .Where(s => s.ClientId == clientId && s.ProductId == productId && s.Version == expectedVersion)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Quantity, quantity)
                .SetProperty(s => s.Version, s => s.Version + 1), ct);

        if (updated == 1)
        {
            var row = (await GetStockAsync(new StockFilter([clientId], [], [productId]), ct)).Single();
            logger.LogInformation("Stock {ClientId}/{ProductId} set to {Quantity} (v{Version})",
                clientId, productId, row.Quantity, row.Version);
            return new UpdateStockResult.Updated(row);
        }

        var current = await db.StockItems
            .Where(s => s.ClientId == clientId && s.ProductId == productId)
            .Select(s => new { s.Quantity, s.Version })
            .FirstOrDefaultAsync(ct);

        // Chybějící řádek by porušil invariant seedu (řádek pro celý sortiment). Bez administrace
        // klientů a produktů nemůže nastat, takže ho hlásíme jako neexistující.
        if (current is null)
            return new UpdateStockResult.NotFound();

        logger.LogWarning("Stock {ClientId}/{ProductId} conflict: expected v{Expected}, current v{Current}",
            clientId, productId, expectedVersion, current.Version);
        return new UpdateStockResult.Conflict(current.Quantity, current.Version);
    }
}

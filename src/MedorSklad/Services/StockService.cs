using System.Globalization;
using MedorSklad.Contracts;
using MedorSklad.Data;
using Microsoft.EntityFrameworkCore;

namespace MedorSklad.Services;

public enum UpdateStockResult
{
    Updated,
    ClientOrProductNotFound,
    ProductNotInClientAssortment,
}

/// <summary>
/// Doménová logika skladu. Sortiment klienta tvoří všechny produkty z kategorií,
/// které má klient přidělené. Řádek gridu = jeden produkt na skladu jednoho klienta.
/// </summary>
public class StockService(WarehouseDbContext db)
{
    /// <summary>Řazení podle české abecedy nezávisle na jazyku serveru (SQLite umí jen binární řazení).</summary>
    private static readonly StringComparer CzechOrder = StringComparer.Create(new CultureInfo("cs-CZ"), ignoreCase: true);

    public async Task<FilterOptions> GetFilterOptionsAsync(CancellationToken ct = default)
    {
        var clients = await db.Clients.Select(c => new LookupItem(c.Id, c.Name)).ToListAsync(ct);
        var categories = await db.Categories.Select(c => new LookupItem(c.Id, c.Name)).ToListAsync(ct);
        var products = await db.Products.Select(p => new LookupItem(p.Id, p.Name)).ToListAsync(ct);
        return new FilterOptions(SortByName(clients), SortByName(categories), SortByName(products));
    }

    private static List<LookupItem> SortByName(IEnumerable<LookupItem> items) =>
        items.OrderBy(i => i.Name, CzechOrder).ToList();

    public async Task<IReadOnlyList<StockRow>> GetStockAsync(StockFilter filter, CancellationToken ct = default)
    {
        var clientIds = filter.ClientIds;
        var categoryIds = filter.CategoryIds;
        var productIds = filter.ProductIds;

        // Všechny trojice (klient, kategorie klienta, produkt v té kategorii) i s počtem kusů.
        var assortment = db.Clients.AsNoTracking()
            .SelectMany(client => client.Categories, (client, category) => new { client, category })
            .SelectMany(x => x.category.Products, (x, product) => new
            {
                ClientId = x.client.Id,
                ClientName = x.client.Name,
                CategoryName = x.category.Name,
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = db.StockItems
                    .Where(s => s.ClientId == x.client.Id && s.ProductId == product.Id)
                    .Select(s => (int?)s.Quantity)
                    .FirstOrDefault() ?? 0,
            });

        if (clientIds.Count > 0)
            assortment = assortment.Where(a => clientIds.Contains(a.ClientId));
        if (productIds.Count > 0)
            assortment = assortment.Where(a => productIds.Contains(a.ProductId));

        // Filtr kategorií vybírá produkty: řádek zůstane, pokud produkt u klienta spadá
        // aspoň do jedné vybrané kategorie. Ve sloupci kategorií se ale ukážou všechny
        // kategorie produktu, kterými se klient zabývá, aby řádek nebyl zavádějící.
        if (categoryIds.Count > 0)
            assortment = assortment.Where(a => db.Clients.Any(c =>
                c.Id == a.ClientId &&
                c.Categories.Any(cat => categoryIds.Contains(cat.Id) && cat.Products.Any(p => p.Id == a.ProductId))));

        var triples = await assortment.ToListAsync(ct);

        // Seskupení na řádky gridu (klient × produkt) a české řazení až v paměti.
        return triples
            .GroupBy(t => new { t.ClientId, t.ClientName, t.ProductId, t.ProductName, t.Quantity })
            .Select(g => new StockRow(
                g.Key.ClientId,
                g.Key.ClientName,
                g.Key.ProductId,
                g.Key.ProductName,
                g.Select(t => t.CategoryName).Distinct().Order(CzechOrder).ToList(),
                g.Key.Quantity))
            .OrderBy(r => r.ClientName, CzechOrder)
            .ThenBy(r => r.ProductName, CzechOrder)
            .ToList();
    }

    public async Task<UpdateStockResult> UpdateQuantityAsync(int clientId, int productId, int quantity, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);

        var clientExists = await db.Clients.AnyAsync(c => c.Id == clientId, ct);
        var productExists = await db.Products.AnyAsync(p => p.Id == productId, ct);
        if (!clientExists || !productExists)
            return UpdateStockResult.ClientOrProductNotFound;

        var inAssortment = await db.Clients
            .Where(c => c.Id == clientId)
            .SelectMany(c => c.Categories)
            .AnyAsync(cat => cat.Products.Any(p => p.Id == productId), ct);
        if (!inAssortment)
            return UpdateStockResult.ProductNotInClientAssortment;

        var item = await db.StockItems.FindAsync([clientId, productId], ct);
        if (item is null)
            db.StockItems.Add(new StockItem { ClientId = clientId, ProductId = productId, Quantity = quantity });
        else
            item.Quantity = quantity;

        await db.SaveChangesAsync(ct);
        return UpdateStockResult.Updated;
    }
}

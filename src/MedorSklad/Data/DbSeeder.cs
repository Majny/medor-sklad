using Microsoft.EntityFrameworkCore;

namespace MedorSklad.Data;

/// <summary>
/// Naplní prázdnou databázi testovacími daty. Data jsou deterministická (pevné semínko),
/// aby šla snadno ověřit. Volá se z UseSeeding / UseAsyncSeeding po aplikaci migrací.
/// </summary>
/// <remarks>
/// Invariant: pro každou dvojici klient × produkt ze sortimentu klienta existuje právě jeden
/// řádek StockItem. Produkt ve dvou kategoriích klienta je pořád jedna dvojice.
/// </remarks>
public static class DbSeeder
{
    public static void Seed(WarehouseDbContext db)
    {
        if (db.Clients.Any())
            return;

        db.Clients.AddRange(BuildData());
        db.SaveChanges();
    }

    public static async Task SeedAsync(WarehouseDbContext db, CancellationToken ct = default)
    {
        if (await db.Clients.AnyAsync(ct))
            return;

        db.Clients.AddRange(BuildData());
        await db.SaveChangesAsync(ct);
    }

    private static List<Client> BuildData()
    {
        var c = new Dictionary<string, Category>();
        foreach (var name in new[] { "Elektronika", "Kancelářské potřeby", "Drogerie", "Potraviny", "Hračky", "Sport", "Domácnost" })
            c[name] = new Category { Name = name };

        Product P(string name, params string[] categories) =>
            new() { Name = name, Categories = categories.Select(x => c[x]).ToList() };

        var products = new List<Product>
        {
            P("USB-C kabel 1 m", "Elektronika"),
            P("Bezdrátová myš", "Elektronika", "Kancelářské potřeby"),
            P("Baterie AA (4 ks)", "Elektronika", "Hračky"),
            P("Powerbanka 10 000 mAh", "Elektronika", "Sport"),
            P("Sluchátka do uší", "Elektronika", "Sport"),
            P("Rychlovarná konvice", "Elektronika", "Domácnost"),
            P("Kancelářský papír A4", "Kancelářské potřeby"),
            P("Kuličkové pero modré", "Kancelářské potřeby"),
            P("Pořadač A4", "Kancelářské potřeby"),
            P("Lepicí páska", "Kancelářské potřeby"),
            P("Sešit A5", "Kancelářské potřeby"),
            P("Řezací podložka", "Kancelářské potřeby", "Domácnost"),
            P("Hrnek s logem", "Kancelářské potřeby", "Domácnost"),
            P("Papírové kapesníky", "Drogerie", "Kancelářské potřeby"),
            P("Tekuté mýdlo 500 ml", "Drogerie"),
            P("Zubní pasta", "Drogerie"),
            P("Šampon", "Drogerie"),
            P("Opalovací krém", "Drogerie", "Sport"),
            P("Houbička na nádobí", "Drogerie", "Domácnost"),
            P("Instantní káva 200 g", "Potraviny", "Kancelářské potřeby"),
            P("Čaj zelený", "Potraviny"),
            P("Čokoláda hořká", "Potraviny"),
            P("Med květový", "Potraviny"),
            P("Cukr kostky", "Potraviny", "Kancelářské potřeby"),
            P("Ovesné vločky", "Potraviny", "Sport"),
            P("Proteinová tyčinka", "Potraviny", "Sport"),
            P("Minerální voda 1,5 l", "Potraviny", "Sport"),
            P("Stavebnice 500 dílků", "Hračky"),
            P("Plyšový medvěd", "Hračky"),
            P("Puzzle 1000 dílků", "Hračky"),
            P("Pastelky (12 ks)", "Hračky", "Kancelářské potřeby"),
            P("Švihadlo", "Sport", "Hračky"),
            P("Fitness podložka", "Sport"),
            P("Chladicí taška", "Sport", "Domácnost"),
            P("Žehlicí prkno", "Domácnost"),
        };

        Client C(string name, params string[] categories) =>
            new() { Name = name, Categories = categories.Select(x => c[x]).ToList() };

        var clients = new List<Client>
        {
            C("Kancelář Plus s.r.o.", "Kancelářské potřeby", "Elektronika"),
            C("Lékárna U Zlatého hada", "Drogerie", "Potraviny"),
            C("Hračkárna Kostička", "Hračky", "Elektronika"),
            C("FitZone Palmovka", "Sport", "Potraviny"),
            C("Večerka Na Rohu", "Potraviny", "Drogerie", "Kancelářské potřeby", "Domácnost"),
            C("Čajovna Šálek", "Potraviny"),
            C("Chata pod Sněžkou", "Domácnost", "Sport", "Potraviny"),
            C("Železářství Cívka", "Domácnost", "Elektronika"),
        };

        // Skladový řádek pro každou dvojici ze sortimentu. Zhruba každý osmý je vyprodaný,
        // aby bylo na čem ukázat zvýraznění nulových stavů.
        var random = new Random(42);
        foreach (var client in clients)
        {
            var assortment = products.Where(p => p.Categories.Any(client.Categories.Contains));
            foreach (var product in assortment)
            {
                var quantity = random.Next(8) == 0 ? 0 : random.Next(0, 200);
                client.StockItems.Add(new StockItem { Product = product, Quantity = quantity, Version = 0 });
            }
        }

        return clients;
    }
}

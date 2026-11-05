namespace MedorSklad.Data;

/// <summary>Naplní prázdnou databázi testovacími daty. Data jsou deterministická, aby šla snadno ověřit.</summary>
public static class DbSeeder
{
    public static void Seed(WarehouseDbContext db)
    {
        if (db.Clients.Any())
            return;

        var c = new Dictionary<string, Category>();
        foreach (var name in new[] { "Elektronika", "Kancelářské potřeby", "Drogerie", "Potraviny", "Hračky", "Sport" })
            c[name] = new Category { Name = name };

        Product P(string name, params string[] categories) =>
            new() { Name = name, Categories = categories.Select(x => c[x]).ToList() };

        var products = new List<Product>
        {
            P("USB-C kabel 1 m", "Elektronika"),
            P("Bezdrátová myš", "Elektronika", "Kancelářské potřeby"),
            P("Baterie AA (4 ks)", "Elektronika", "Hračky"),
            P("Powerbanka 10 000 mAh", "Elektronika", "Sport"),
            P("Kancelářský papír A4", "Kancelářské potřeby"),
            P("Kuličkové pero modré", "Kancelářské potřeby"),
            P("Pořadač A4", "Kancelářské potřeby"),
            P("Papírové kapesníky", "Drogerie", "Kancelářské potřeby"),
            P("Tekuté mýdlo 500 ml", "Drogerie"),
            P("Zubní pasta", "Drogerie"),
            P("Instantní káva 200 g", "Potraviny", "Kancelářské potřeby"),
            P("Proteinová tyčinka", "Potraviny", "Sport"),
            P("Minerální voda 1,5 l", "Potraviny", "Sport"),
            P("Stavebnice 500 dílků", "Hračky"),
            P("Plyšový medvěd", "Hračky"),
            P("Švihadlo", "Sport", "Hračky"),
            P("Fitness podložka", "Sport"),
        };

        Client C(string name, params string[] categories) =>
            new() { Name = name, Categories = categories.Select(x => c[x]).ToList() };

        var clients = new List<Client>
        {
            C("Kancelář Plus s.r.o.", "Kancelářské potřeby", "Elektronika"),
            C("Lékárna U Zlatého hada", "Drogerie", "Potraviny"),
            C("Hračkárna Kostička", "Hračky", "Elektronika"),
            C("FitZone Palmovka", "Sport", "Potraviny"),
            C("Večerka Na Rohu", "Potraviny", "Drogerie", "Kancelářské potřeby"),
        };

        db.Categories.AddRange(c.Values);
        db.Products.AddRange(products);
        db.Clients.AddRange(clients);

        // Každý klient má sklad jen s produkty z kategorií, kterými se zabývá.
        var random = new Random(42);
        foreach (var client in clients)
        {
            var assortment = client.Categories.SelectMany(cat => cat.Products).Distinct();
            foreach (var product in assortment)
                db.StockItems.Add(new StockItem { Client = client, Product = product, Quantity = random.Next(0, 250) });
        }

        db.SaveChanges();
    }
}

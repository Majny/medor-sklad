namespace MedorSklad.Data;

/// <summary>Klient, který má vlastní sklad a stará se o vybrané kategorie produktů.</summary>
public class Client
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public List<Category> Categories { get; set; } = [];
    public List<StockItem> StockItems { get; set; } = [];
}

/// <summary>Kategorie produktů. Produkt může patřit do více kategorií.</summary>
public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public List<Product> Products { get; set; } = [];
    public List<Client> Clients { get; set; } = [];
}

/// <summary>Produkt, společný katalog pro všechny klienty.</summary>
public class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public List<Category> Categories { get; set; } = [];
}

/// <summary>Počet kusů produktu na skladu konkrétního klienta.</summary>
public class StockItem
{
    public int ClientId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }

    /// <summary>
    /// Verze řádku pro optimistickou souběžnost. Zvyšuje ji aplikace při každé změně,
    /// protože SQLite nemá rowversion. Na MS SQL by to byl sloupec s atributem [Timestamp].
    /// </summary>
    public int Version { get; set; }

    public Client Client { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

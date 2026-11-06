using System.ComponentModel.DataAnnotations;

namespace MedorSklad.Contracts;

/// <summary>Položka výběru ve filtru (kategorie).</summary>
public record LookupItem(int Id, string Name);

/// <summary>Klient ve filtru i s kategoriemi, které má přidělené (pro kaskádový filtr).</summary>
public record ClientOption(int Id, string Name, IReadOnlyList<int> CategoryIds);

/// <summary>Produkt ve filtru i s kategoriemi, do kterých patří (pro kaskádový filtr).</summary>
public record ProductOption(int Id, string Name, IReadOnlyList<int> CategoryIds);

/// <summary>Možnosti pro menu s filtrem, seřazené podle české abecedy.</summary>
public record FilterOptions(
    IReadOnlyList<ClientOption> Clients,
    IReadOnlyList<LookupItem> Categories,
    IReadOnlyList<ProductOption> Products);

/// <summary>
/// Filtr gridu. Prázdný seznam znamená "bez omezení" pro danou dimenzi, skupiny se kombinují přes AND.
/// </summary>
public record StockFilter(
    IReadOnlyCollection<int> ClientIds,
    IReadOnlyCollection<int> CategoryIds,
    IReadOnlyCollection<int> ProductIds,
    bool OnlyOutOfStock = false);

/// <summary>
/// Jeden řádek gridu: produkt na skladu konkrétního klienta. Categories obsahuje jen kategorie
/// produktu, kterými se klient zabývá. Version klient posílá zpět při změně (optimistická souběžnost).
/// </summary>
public record StockRow(
    int ClientId,
    string ClientName,
    int ProductId,
    string ProductName,
    IReadOnlyList<string> Categories,
    int Quantity,
    int Version);

/// <summary>
/// Nový počet kusů a verze řádku, kterou klient naposledy viděl.
/// Pole jsou nullable, aby chybějící hodnota skončila chybou 400 a ne tichou nulou.
/// </summary>
public record UpdateQuantityRequest(
    [Required(ErrorMessage = "Chybí počet kusů.")]
    [Range(0, 1_000_000, ErrorMessage = "Počet kusů musí být celé číslo od 0 do 1 000 000.")]
    int? Quantity,
    [Required(ErrorMessage = "Chybí verze řádku.")]
    [Range(0, int.MaxValue, ErrorMessage = "Neplatná verze řádku.")]
    int? Version);

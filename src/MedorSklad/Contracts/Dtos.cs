using System.ComponentModel.DataAnnotations;

namespace MedorSklad.Contracts;

/// <summary>Položka výběru ve filtru (klient, kategorie nebo produkt).</summary>
public record LookupItem(int Id, string Name);

/// <summary>Možnosti pro menu s filtrem.</summary>
public record FilterOptions(
    IReadOnlyList<LookupItem> Clients,
    IReadOnlyList<LookupItem> Categories,
    IReadOnlyList<LookupItem> Products);

/// <summary>
/// Filtr gridu. Prázdný seznam znamená "bez omezení" pro danou dimenzi.
/// </summary>
public record StockFilter(
    IReadOnlyCollection<int> ClientIds,
    IReadOnlyCollection<int> CategoryIds,
    IReadOnlyCollection<int> ProductIds);

/// <summary>Jeden řádek gridu: produkt na skladu konkrétního klienta.</summary>
/// <param name="Categories">Kategorie produktu, kterými se daný klient zabývá.</param>
public record StockRow(
    int ClientId,
    string ClientName,
    int ProductId,
    string ProductName,
    IReadOnlyList<string> Categories,
    int Quantity);

/// <summary>Nový počet kusů. Nullable, aby chybějící pole skončilo chybou 400 a ne tichou nulou.</summary>
public record UpdateQuantityRequest(
    [Required(ErrorMessage = "Chybí počet kusů.")]
    [Range(0, int.MaxValue, ErrorMessage = "Počet kusů musí být celé nezáporné číslo.")]
    int? Quantity);

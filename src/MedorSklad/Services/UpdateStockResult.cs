using MedorSklad.Contracts;

namespace MedorSklad.Services;

/// <summary>Výsledek změny počtu kusů. Controller z něj odvodí HTTP odpověď.</summary>
public abstract record UpdateStockResult
{
    /// <summary>Uloženo, vrací aktuální řádek s novou verzí.</summary>
    public sealed record Updated(StockRow Row) : UpdateStockResult;

    /// <summary>Klient nebo produkt neexistuje.</summary>
    public sealed record NotFound : UpdateStockResult;

    /// <summary>Produkt nepatří do žádné kategorie klienta.</summary>
    public sealed record NotInAssortment : UpdateStockResult;

    /// <summary>Řádek mezitím změnil někdo jiný, posíláme jeho aktuální stav.</summary>
    public sealed record Conflict(int CurrentQuantity, int CurrentVersion) : UpdateStockResult;
}

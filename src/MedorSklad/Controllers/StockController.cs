using MedorSklad.Contracts;
using MedorSklad.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedorSklad.Controllers;

[ApiController]
[Route("api/stock")]
public class StockController(StockService stockService) : ControllerBase
{
    /// <summary>
    /// Řádky gridu. Příklad: GET /api/stock?clientIds=1&amp;clientIds=3&amp;categoryIds=2
    /// Nevyplněný parametr = bez omezení.
    /// </summary>
    [HttpGet]
    public Task<IReadOnlyList<StockRow>> Get(
        [FromQuery] int[]? clientIds,
        [FromQuery] int[]? categoryIds,
        [FromQuery] int[]? productIds,
        CancellationToken ct)
    {
        var filter = new StockFilter(clientIds ?? [], categoryIds ?? [], productIds ?? []);
        return stockService.GetStockAsync(filter, ct);
    }

    /// <summary>
    /// Nastaví počet kusů produktu na skladu klienta.
    /// Validaci těla (povinné, nezáporné) řeší [ApiController] přes DataAnnotations na DTO.
    /// </summary>
    [HttpPut("{clientId:int}/{productId:int}")]
    public async Task<IActionResult> UpdateQuantity(int clientId, int productId, UpdateQuantityRequest request, CancellationToken ct)
    {
        var result = await stockService.UpdateQuantityAsync(clientId, productId, request.Quantity!.Value, ct);
        return result switch
        {
            UpdateStockResult.Updated => NoContent(),
            UpdateStockResult.ClientOrProductNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                detail: "Klient nebo produkt neexistuje."),
            UpdateStockResult.ProductNotInClientAssortment => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Produkt nepatří do žádné kategorie, kterou má klient přidělenou."),
            _ => throw new InvalidOperationException($"Neznámý výsledek {result}."),
        };
    }
}

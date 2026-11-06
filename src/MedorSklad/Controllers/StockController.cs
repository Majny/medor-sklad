using MedorSklad.Contracts;
using MedorSklad.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedorSklad.Controllers;

[ApiController]
[Route("api/stock")]
public class StockController(StockService stockService) : ControllerBase
{
    /// <summary>
    /// Řádky gridu. Příklad: GET /api/stock?clientIds=1&amp;clientIds=3&amp;categoryIds=2&amp;onlyOutOfStock=true
    /// Nevyplněný parametr znamená bez omezení, skupiny se kombinují přes AND.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<StockRow>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<StockRow>> Get(
        [FromQuery] int[]? clientIds,
        [FromQuery] int[]? categoryIds,
        [FromQuery] int[]? productIds,
        CancellationToken ct,
        [FromQuery] bool onlyOutOfStock = false)
    {
        var filter = new StockFilter(clientIds ?? [], categoryIds ?? [], productIds ?? [], onlyOutOfStock);
        return stockService.GetStockAsync(filter, ct);
    }

    /// <summary>
    /// Nastaví počet kusů produktu na skladu klienta. Tělo nese i verzi řádku, kterou klient
    /// naposledy viděl; pokud se mezitím změnila, vrací 409 s aktuální hodnotou.
    /// Validaci těla řeší [ApiController] přes DataAnnotations na DTO.
    /// </summary>
    [HttpPut("{clientId:int}/{productId:int}")]
    [ProducesResponseType<StockRow>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [RequestSizeLimit(16_384)] // tělo je jen {quantity, version}, větší požadavek skončí 413
    public async Task<ActionResult<StockRow>> UpdateQuantity(
        int clientId, int productId, UpdateQuantityRequest request, CancellationToken ct)
    {
        var result = await stockService.UpdateQuantityAsync(
            clientId, productId, request.Quantity!.Value, request.Version!.Value, ct);

        return result switch
        {
            UpdateStockResult.Updated u => Ok(u.Row),
            UpdateStockResult.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Nenalezeno",
                detail: "Klient nebo produkt neexistuje."),
            UpdateStockResult.NotInAssortment => Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Produkt mimo sortiment",
                detail: "Produkt nepatří do žádné kategorie, kterou má klient přidělenou."),
            UpdateStockResult.Conflict c => ConflictProblem(c),
            _ => throw new InvalidOperationException($"Neznámý výsledek {result}."),
        };
    }

    /// <summary>409 jako ProblemDetails s aktuálním stavem řádku, aby klient mohl nabídnout řešení.</summary>
    private ObjectResult ConflictProblem(UpdateStockResult.Conflict conflict)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(
            HttpContext,
            StatusCodes.Status409Conflict,
            title: "Konflikt souběžné úpravy",
            detail: "Počet kusů mezitím změnil někdo jiný.");
        problem.Extensions["currentQuantity"] = conflict.CurrentQuantity;
        problem.Extensions["currentVersion"] = conflict.CurrentVersion;
        return Conflict(problem);
    }
}

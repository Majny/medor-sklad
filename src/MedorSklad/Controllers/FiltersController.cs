using MedorSklad.Contracts;
using MedorSklad.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedorSklad.Controllers;

[ApiController]
[Route("api/filters")]
public class FiltersController(StockService stockService) : ControllerBase
{
    /// <summary>Klienti, kategorie a produkty pro menu s filtrem, i s vazbami na kategorie.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<FilterOptions>> Get(CancellationToken ct) =>
        await stockService.GetFilterOptionsAsync(ct);
}

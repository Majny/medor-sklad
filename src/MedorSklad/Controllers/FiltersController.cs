using MedorSklad.Contracts;
using MedorSklad.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedorSklad.Controllers;

[ApiController]
[Route("api/filters")]
public class FiltersController(StockService stockService) : ControllerBase
{
    /// <summary>Klienti, kategorie a produkty pro menu s filtrem.</summary>
    [HttpGet]
    public Task<FilterOptions> Get(CancellationToken ct) => stockService.GetFilterOptionsAsync(ct);
}

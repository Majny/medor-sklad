using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedorSklad.Controllers;

/// <summary>
/// Odpověď 400 pro [ApiController] s českými hláškami. Chyby z DataAnnotations na DTO česky jsou,
/// ale nečitelné JSON (desetinné číslo, text, prázdné tělo) dostane od frameworku anglickou hlášku
/// s interním názvem typu a navíc obecnou chybu parametru "request". Ty tady nahradíme.
/// </summary>
public static class ValidationProblemResponse
{
    private const string BodyError = "Tělo požadavku musí být JSON, například {\"quantity\": 12, \"version\": 3}.";

    public static IActionResult Create(ActionContext context)
    {
        var errors = new ModelStateDictionary();
        foreach (var (key, entry) in context.ModelState)
        {
            if (entry.Errors.Count == 0 || key == "request")
                continue; // chyba parametru jako celku je jen důsledkem chyby těla
            switch (key)
            {
                case "Quantity" or "Version":
                    foreach (var error in entry.Errors) errors.AddModelError(key, error.ErrorMessage);
                    break;
                case "$.quantity":
                    errors.AddModelError("Quantity", "Počet kusů musí být celé číslo od 0 do 1 000 000.");
                    break;
                case "$.version":
                    errors.AddModelError("Version", "Neplatná verze řádku.");
                    break;
                case "" or "$":
                    errors.AddModelError("body", BodyError);
                    break;
                default:
                    errors.AddModelError(key, "Neplatná hodnota.");
                    break;
            }
        }
        if (errors.ErrorCount == 0)
            errors.AddModelError("body", BodyError); // např. tělo "null"

        var problem = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>()
            .CreateValidationProblemDetails(context.HttpContext, errors, StatusCodes.Status400BadRequest, title: "Neplatný požadavek");
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }
}

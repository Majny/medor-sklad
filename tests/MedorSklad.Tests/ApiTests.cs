using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MedorSklad.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace MedorSklad.Tests;

/// <summary>
/// Integrační testy REST API nad celou aplikací (skutečný Program, migrace i seed) s databází v dočasném souboru.
/// Databázi sdílí všechny testy třídy, proto každý zapisující test používá vlastní dvojici klient × produkt
/// a před PUT si řádek načte přes GET. Testy tak nezávisí na pořadí.
/// </summary>
public sealed class ApiTests : IClassFixture<TestApp>
{
    private readonly HttpClient _client;

    public ApiTests(TestApp app) => _client = app.CreateClient();

    private async Task<FilterOptions> Filters() =>
        (await _client.GetFromJsonAsync<FilterOptions>("/api/filters"))!;

    private async Task<List<StockRow>> Stock(string query = "") =>
        (await _client.GetFromJsonAsync<List<StockRow>>($"/api/stock{query}"))!;

    /// <summary>N-tý řádek celého gridu; každý zapisující test má svůj index.</summary>
    private async Task<StockRow> RowAt(int index) => (await Stock())[index];

    private async Task<StockRow> Reload(StockRow row) =>
        Assert.Single(await Stock($"?clientIds={row.ClientId}&productIds={row.ProductId}"));

    private Task<HttpResponseMessage> Put(StockRow row, int quantity, int version) =>
        _client.PutAsJsonAsync($"/api/stock/{row.ClientId}/{row.ProductId}", new { quantity, version });

    [Fact]
    public async Task Filters_return_seeded_clients_categories_and_products()
    {
        var filters = await Filters();

        Assert.NotEmpty(filters.Clients);
        Assert.NotEmpty(filters.Categories);
        Assert.NotEmpty(filters.Products);
        Assert.All(filters.Clients, c => Assert.NotEmpty(c.CategoryIds));
        Assert.All(filters.Products, p => Assert.NotEmpty(p.CategoryIds));
    }

    [Fact]
    public async Task Repeated_query_parameters_filter_by_multiple_clients()
    {
        var clients = (await Filters()).Clients.Take(2).Select(c => c.Id).ToArray();

        var rows = await Stock($"?clientIds={clients[0]}&clientIds={clients[1]}");

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Contains(r.ClientId, clients));
        Assert.Equal(2, rows.Select(r => r.ClientId).Distinct().Count());
    }

    [Fact]
    public async Task Only_out_of_stock_query_parameter_filters()
    {
        var rows = await Stock("?onlyOutOfStock=true");

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(0, r.Quantity));
        Assert.True(rows.Count < (await Stock()).Count);
    }

    [Fact]
    public async Task Put_updates_quantity_visible_in_grid()
    {
        var row = await RowAt(0);

        var response = await Put(row, row.Quantity + 1, row.Version);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StockRow>();
        Assert.Equal(row.Quantity + 1, body!.Quantity);
        Assert.Equal(row.Quantity + 1, (await Reload(row)).Quantity);
    }

    [Fact]
    public async Task Put_returns_new_version_usable_for_next_put()
    {
        var row = await RowAt(1);

        var first = await (await Put(row, 11, row.Version)).Content.ReadFromJsonAsync<StockRow>();
        var second = await Put(row, 12, first!.Version);

        Assert.Equal(row.Version + 1, first.Version);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var updated = await Reload(row);
        Assert.Equal(12, updated.Quantity);
        Assert.Equal(row.Version + 2, updated.Version);
    }

    [Fact]
    public async Task Second_put_with_same_version_returns_409_problem_with_current_quantity()
    {
        var row = await RowAt(2);
        await Put(row, 21, row.Version);

        var response = await Put(row, 99, row.Version);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(21, problem.RootElement.GetProperty("currentQuantity").GetInt32());
        Assert.Equal(row.Version + 1, problem.RootElement.GetProperty("currentVersion").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.RootElement.GetProperty("detail").GetString()));
        Assert.Equal(21, (await Reload(row)).Quantity);
    }

    [Theory]
    [InlineData("null", "body")]
    [InlineData("""{"qty": 7, "version": 0}""", "Quantity")]
    [InlineData("""{"quantity": null, "version": 0}""", "Quantity")]
    [InlineData("""{"quantity": -1, "version": 0}""", "Quantity")]
    [InlineData("""{"quantity": 1000001, "version": 0}""", "Quantity")]
    [InlineData("""{"quantity": 12.5, "version": 0}""", "Quantity")]
    [InlineData("""{"quantity": "abc", "version": 0}""", "Quantity")]
    [InlineData("""{"quantity": 5}""", "Version")]
    [InlineData("", "body")]
    public async Task Put_with_invalid_body_returns_400_with_czech_errors_and_keeps_stock(string body, string errorField)
    {
        var row = await RowAt(3);

        var response = await _client.PutAsync(
            $"/api/stock/{row.ClientId}/{row.ProductId}",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal([errorField], problem!.Errors.Keys); // žádné anglické chyby frameworku navíc
        Assert.Equal("Neplatný požadavek", problem.Title);
        Assert.Equal(row.Quantity, (await Reload(row)).Quantity);
    }

    [Fact]
    public async Task Put_for_product_outside_client_assortment_returns_422_with_czech_detail()
    {
        var filters = await Filters();
        var (client, product) = filters.Clients
            .SelectMany(c => filters.Products, (c, p) => (c, p))
            .First(x => !x.c.CategoryIds.Intersect(x.p.CategoryIds).Any());

        var response = await _client.PutAsJsonAsync($"/api/stock/{client.Id}/{product.Id}", new { quantity = 1, version = 0 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("nepatří", problem!.Detail);
    }

    [Fact]
    public async Task Put_for_unknown_client_returns_404_with_czech_detail()
    {
        var response = await _client.PutAsJsonAsync("/api/stock/99999/1", new { quantity = 1, version = 0 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Klient nebo produkt neexistuje.", problem!.Detail);
    }

    [Fact]
    public async Task OpenApi_document_lists_stock_endpoints()
    {
        var json = await _client.GetStringAsync("/openapi/v1.json");

        Assert.Contains("/api/stock/{clientId}/{productId}", json);
        Assert.Contains("/api/filters", json);
    }
}

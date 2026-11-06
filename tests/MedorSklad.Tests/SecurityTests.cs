using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;

namespace MedorSklad.Tests;

/// <summary>Bezpečnostní hlavičky, limit velikosti těla a chybové odpovědi bez interních detailů.</summary>
public sealed class SecurityTests(TestApp app) : IClassFixture<TestApp>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/js/main.js")]
    [InlineData("/api/filters")]
    [InlineData("/api/neexistuje")]
    public async Task Responses_carry_security_headers(string url)
    {
        var response = await app.CreateClient().GetAsync(url);

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Fact]
    public async Task Oversized_body_returns_413_without_internal_details_in_production()
    {
        // Limit těla vynucuje až Kestrel (TestServer ho ignoruje), proto běží na skutečném Kestrelu.
        using var factory = app.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        factory.UseKestrel(0);
        factory.StartServer();
        var client = factory.CreateClient();
        var body = $$"""{"quantity": 1, "version": 1, "padding": "{{new string('x', 20_000)}}"}""";

        var response = await client.PutAsync("/api/stock/1/1", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", problem);
        Assert.DoesNotContain("   at ", problem);
        Assert.True(response.Headers.Contains("Content-Security-Policy"), "Hlavičky musí mít i chybová odpověď.");
    }
}

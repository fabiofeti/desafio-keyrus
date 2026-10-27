using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace CashFlow.IntegrationTests;

[Trait("Category", "Integration")]
public class HostTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Health_Should_ReturnOk()
    {
        // Act
        HttpResponseMessage response = await HttpClient.GetAsync("health");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("health/live")]
    [InlineData("health/ready")]
    public async Task HealthProbe_Should_ReturnOk_WhenBothModulesReachTheirSchema(string path)
    {
        // Act
        HttpResponseMessage response = await HttpClient.GetAsync(path);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_Should_Return503_AndLiveStay200_WhenAModulesDatabaseIsUnreachable()
    {
        // Arrange: Consolidation's connection points at a port nothing listens on.
        using HttpClient client = Factory.WithWebHostBuilder(builder => builder.UseSetting(
                "ConnectionStrings:Consolidation", "Host=127.0.0.1;Port=1;Database=cashflow;Username=x;Password=x;Timeout=2"))
            .CreateClient();

        // Act
        HttpResponseMessage ready = await client.GetAsync("health/ready");
        HttpResponseMessage live = await client.GetAsync("health/live");
        HttpResponseMessage legacy = await client.GetAsync("health");

        // Assert: unready, not dead - a restart would not bring the database back.
        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        legacy.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BindingFailure_Should_ReturnProblemDetails_InProductionToo()
    {
        // Arrange: the compose environment. Development alone hid a bare, empty 400.
        using HttpClient client = Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production")).CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "transactions")
        {
            Content = new StringContent("{nope", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", "it-binding-production");

        // Act
        HttpResponseMessage response = await client.SendAsync(request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("\"code\":\"Request.Invalid\"");
    }

    [Fact]
    public async Task CorrelationId_Should_BeEchoed_WhenWellFormed()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "health");
        request.Headers.Add("X-Correlation-Id", "it-correlation-123");

        // Act
        HttpResponseMessage response = await HttpClient.SendAsync(request);

        // Assert
        response.Headers.GetValues("X-Correlation-Id").ShouldBe(["it-correlation-123"]);
    }

    [Fact]
    public async Task CorrelationId_Should_BeReplaced_WhenMalformed()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "health");
        request.Headers.Add("X-Correlation-Id", "not valid; drop table");

        // Act
        HttpResponseMessage response = await HttpClient.SendAsync(request);

        // Assert
        string echoed = response.Headers.GetValues("X-Correlation-Id").Single();
        echoed.ShouldNotBe("not valid; drop table");
        echoed.ShouldMatch("^[a-f0-9]{32}$");
    }

    [Fact]
    public async Task OpenApi_Should_BeServed()
    {
        // Act
        HttpResponseMessage response = await HttpClient.GetAsync("openapi/v1.json");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

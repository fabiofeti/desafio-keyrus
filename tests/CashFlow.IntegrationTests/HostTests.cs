using System.Net;

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

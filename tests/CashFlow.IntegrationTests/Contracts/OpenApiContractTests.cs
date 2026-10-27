using System.Text.Json;
using System.Text.Json.Nodes;

namespace CashFlow.IntegrationTests.Contracts;

/// <summary>
/// docs/api/openapi.json is the document the running API generates, committed.
/// This test fails when the code and the committed contract drift apart; after
/// an intended change, regenerate it with UPDATE_CONTRACTS=1 and review the diff.
/// </summary>
[Trait("Category", "Integration")]
public class OpenApiContractTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    // Relaxed escaping so the committed file reads "+" and "é", not \u002B.
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [Fact]
    public async Task GeneratedDocument_Should_MatchTheCommittedContract()
    {
        // Arrange
        string path = Path.Combine(RepositoryRoot(), "docs", "api", "openapi.json");

        // Act
        JsonNode generated = Normalize(JsonNode.Parse(await HttpClient.GetStringAsync("openapi/v1.json"))!);

        // Assert
        if (Environment.GetEnvironmentVariable("UPDATE_CONTRACTS") == "1")
        {
            await File.WriteAllTextAsync(path, generated.ToJsonString(Indented) + "\n");
            return;
        }

        File.Exists(path).ShouldBeTrue($"{path} is missing; generate it with UPDATE_CONTRACTS=1");
        JsonNode committed = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        JsonNode.DeepEquals(generated, committed).ShouldBeTrue(
            "the API no longer matches docs/api/openapi.json; if the change is intended, regenerate with UPDATE_CONTRACTS=1");
    }

    // The server URL depends on where the API runs, not on the contract.
    private static JsonNode Normalize(JsonNode document)
    {
        document.AsObject().Remove("servers");
        return document;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CashFlow.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("CashFlow.slnx not found above the test binaries.");
    }
}

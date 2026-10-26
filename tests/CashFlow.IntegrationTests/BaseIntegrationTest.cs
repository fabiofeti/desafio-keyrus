namespace CashFlow.IntegrationTests;

[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestWebAppFactory>;

/// <summary>
/// One set of containers for the whole run. Every derived class must also carry
/// <c>[Trait("Category", "Integration")]</c>: the Stop hook runs everything
/// except that category, and /verify runs it all.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public abstract class BaseIntegrationTest(IntegrationTestWebAppFactory factory)
{
    protected IntegrationTestWebAppFactory Factory { get; } = factory;

    protected HttpClient HttpClient { get; } = factory.CreateClient();
}

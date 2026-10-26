using System.Reflection;
using NetArchTest.Rules;

namespace CashFlow.ArchitectureTests;

/// <summary>
/// What the two hosts and the shared kernel may know. The API and the Worker
/// compose the same modules; these rules keep each one to its own job.
/// </summary>
public class HostTests
{
    [Fact]
    public void Common_Should_NotReferenceAnyModule()
    {
        // The shared kernel is shared precisely because it knows nothing about
        // Transactions or Consolidation. A module name here couples every module
        // to that one.
        foreach (Assembly assembly in new[]
                 {
                     Assemblies.CommonDomain, Assemblies.CommonApplication,
                     Assemblies.CommonInfrastructure, Assemblies.CommonPresentation
                 })
        {
            string[] violations = ReferencedAssemblyNames(assembly)
                .Where(name => name.StartsWith("CashFlow.Modules.", StringComparison.Ordinal))
                .ToArray();

            Assert.True(
                violations.Length == 0,
                $"{assembly.GetName().Name} must not reference modules, but references: {string.Join(", ", violations)}");
        }
    }

    [Fact]
    public void Api_Should_NotTalkToTheBroker()
    {
        // Rule 5: a failure in consolidation must not stop POST /transactions.
        // The API writes the outbox and is done; only the Worker publishes.
        TestResult result = Types.InAssembly(Assemblies.Api)
            .ShouldNot()
            .HaveDependencyOnAny("RabbitMQ.Client", "CashFlow.Common.Infrastructure.Messaging")
            .GetResult();

        AssertSuccess(result, "The API must not depend on the message broker.");
    }

    [Fact]
    public void Worker_Should_NotMapEndpoints()
    {
        // The Worker references the modules' Presentation assemblies only for
        // their integration event handlers. Serving HTTP is the API's job.
        TestResult result = Types.InAssembly(Assemblies.Worker)
            .ShouldNot()
            .HaveDependencyOnAny("CashFlow.Common.Presentation.Endpoints", "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Routing")
            .GetResult();

        AssertSuccess(result, "The Worker must not map endpoints.");
    }

    private static string[] ReferencedAssemblyNames(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty)];

    private static void AssertSuccess(TestResult result, string because) =>
        Assert.True(result.IsSuccessful, $"{because} Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
}

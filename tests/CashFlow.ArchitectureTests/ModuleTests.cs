using System.Reflection;
using NetArchTest.Rules;

namespace CashFlow.ArchitectureTests;

/// <summary>
/// The boundary between modules (CLAUDE.md, rule 1). While the module list is
/// short these rules have little to check - except the first one, which is what
/// stops a module from slipping past the rest by not being registered.
/// </summary>
public class ModuleTests
{
    /// <summary>
    /// The registry is derived from the filesystem, not trusted to be updated.
    /// Create a module folder without registering the module and this fails,
    /// which is the only reason the rules below can be trusted while the list
    /// is short.
    /// </summary>
    [Fact]
    public void ModuleRegistry_Should_MatchTheModuleFoldersOnDisk()
    {
        string modulesPath = Path.Combine(
            Assemblies.RepositoryRoot().FullName, "src", "Modules");

        string[] onDisk = Directory.Exists(modulesPath)
            ? [.. new DirectoryInfo(modulesPath).GetDirectories().Select(d => d.Name).Order()]
            : [];

        string[] registered = [.. Assemblies.Modules.Select(m => m.Name).Order()];

        Assert.Equal(onDisk, registered);

        Assert.Equal(
            onDisk.Length,
            Hosting.Modules.ApplicationAssemblies.Length);

        Assert.Equal(
            onDisk.Length,
            Hosting.Modules.PresentationAssemblies.Length);
    }

    [Fact]
    public void Module_Should_NotReferenceAnotherModulesInternals()
    {
        // The one allowed exception is another module's IntegrationEvents
        // assembly. Anything else means the boundary is in the wrong place.
        foreach (ModuleAssemblies module in Assemblies.Modules)
        {
            string[] otherModulePrefixes =
            [
                .. Assemblies.Modules
                    .Where(other => other.Name != module.Name)
                    .Select(other => $"CashFlow.Modules.{other.Name}.")
            ];

            foreach (Assembly assembly in Layers(module))
            {
                string[] violations =
                [
                    .. assembly.GetReferencedAssemblies()
                        .Select(a => a.Name ?? string.Empty)
                        .Where(name => otherModulePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))
                                       && !name.EndsWith(".IntegrationEvents", StringComparison.Ordinal))
                ];

                Assert.True(
                    violations.Length == 0,
                    $"{assembly.GetName().Name} reaches into another module: {string.Join(", ", violations)}");
            }
        }
    }

    [Fact]
    public void IntegrationEvents_Should_DependOnCommonApplicationOnly()
    {
        // This assembly is the module's public contract, so a consumer must be
        // able to reference it without gaining access to anything else.
        foreach (ModuleAssemblies module in Assemblies.Modules)
        {
            string[] allowed =
            [
                "CashFlow.Common.Application",
                "CashFlow.Common.Domain"
            ];

            string[] violations =
            [
                .. module.IntegrationEvents.GetReferencedAssemblies()
                    .Select(a => a.Name ?? string.Empty)
                    .Where(name => name.StartsWith("CoreMastery.", StringComparison.Ordinal)
                                   && !allowed.Contains(name))
            ];

            Assert.True(
                violations.Length == 0,
                $"{module.Name}.IntegrationEvents must carry ids and values only, but references: {string.Join(", ", violations)}");
        }
    }

    [Fact]
    public void ModuleLayers_Should_ObeyTheDependencyRule()
    {
        foreach (ModuleAssemblies module in Assemblies.Modules)
        {
            string prefix = $"CashFlow.Modules.{module.Name}";

            // Common.Application is on this list for the same reason the module's
            // own outer layers are: a Domain that can reach it can reach the
            // decorators, the integration event contracts and the outbox.
            AssertNoDependency(
                module.Domain,
                [$"{prefix}.Application", $"{prefix}.Infrastructure", $"{prefix}.Presentation",
                 "CashFlow.Common.Application"]);
            AssertNoDependency(module.Application, [$"{prefix}.Infrastructure", $"{prefix}.Presentation"]);
            AssertNoDependency(module.Infrastructure, [$"{prefix}.Presentation"]);
            AssertNoDependency(module.Presentation, [$"{prefix}.Infrastructure", $"{prefix}.Domain"]);
        }
    }

    [Fact]
    public void Handlers_Should_BeInternalAndSealed()
    {
        foreach (ModuleAssemblies module in Assemblies.Modules)
        {
            TestResult result = Types.InAssembly(module.Application)
                .That()
                .HaveNameEndingWith("Handler")
                .Should()
                .BeSealed()
                .And()
                .NotBePublic()
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"Handlers in {module.Name}.Application must be internal sealed: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    private static IEnumerable<Assembly> Layers(ModuleAssemblies module) =>
        [module.Domain, module.Application, module.Infrastructure, module.Presentation, module.IntegrationEvents];

    private static void AssertNoDependency(Assembly assembly, string[] forbiddenNamespaces)
    {
        TestResult result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenNamespaces)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{assembly.GetName().Name} must not depend on {string.Join(", ", forbiddenNamespaces)}. "
            + $"Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}

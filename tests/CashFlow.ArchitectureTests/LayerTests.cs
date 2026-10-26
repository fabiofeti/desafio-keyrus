using System.Reflection;
using NetArchTest.Rules;

namespace CashFlow.ArchitectureTests;

/// <summary>
/// The dependency rule from the module layer rules in .claude/skills/add-module, as
/// tests. The table there is the specification; this file is the enforcement.
/// A rule nobody checks is a rule that has already been broken.
/// </summary>
public class LayerTests
{
    private const string ApplicationNamespace = "CashFlow.Common.Application";
    private const string InfrastructureNamespace = "CashFlow.Common.Infrastructure";
    private const string PresentationNamespace = "CashFlow.Common.Presentation";

    [Fact]
    public void Domain_Should_HaveNoPackageDependencies()
    {
        // The innermost layer of every module depends on Common.Domain, so a
        // package added here is a package added to every module's Domain.
        string[] referenced = ReferencedAssemblyNames(Assemblies.CommonDomain);

        string[] unexpected = [.. referenced.Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                                                           && name != "netstandard")];

        Assert.True(
            unexpected.Length == 0,
            $"Common.Domain must reference nothing but the base class library, but references: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Domain_Should_NotDependOnOuterLayers()
    {
        TestResult result = Types.InAssembly(Assemblies.CommonDomain)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, PresentationNamespace)
            .GetResult();

        AssertSuccess(result, "Common.Domain must not know about any layer outside it.");
    }

    [Fact]
    public void Application_Should_NotDependOnInfrastructureOrPresentation()
    {
        TestResult result = Types.InAssembly(Assemblies.CommonApplication)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespace, PresentationNamespace)
            .GetResult();

        AssertSuccess(result, "Common.Application must not depend on Infrastructure or Presentation.");
    }

    [Fact]
    public void Application_Should_NotDependOnEntityFrameworkOrAspNet()
    {
        // Application states what it needs through interfaces - IUnitOfWork,
        // IUserContext - so persistence and transport can be swapped without
        // touching a handler.
        string[] forbidden = ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql"];

        string[] referenced = ReferencedAssemblyNames(Assemblies.CommonApplication);

        string[] violations = [.. referenced.Where(name => forbidden.Any(f => name.StartsWith(f, StringComparison.Ordinal)))];

        Assert.True(
            violations.Length == 0,
            $"Common.Application must not reference persistence or web packages, but references: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Infrastructure_Should_NotDependOnPresentation()
    {
        TestResult result = Types.InAssembly(Assemblies.CommonInfrastructure)
            .ShouldNot()
            .HaveDependencyOn(PresentationNamespace)
            .GetResult();

        AssertSuccess(result, "Common.Infrastructure must not depend on Presentation.");
    }

    [Fact]
    public void Presentation_Should_NotDependOnInfrastructure()
    {
        // An endpoint that can reach Infrastructure is an endpoint that can open
        // a DbContext, which is how data access escapes the Application layer.
        TestResult result = Types.InAssembly(Assemblies.CommonPresentation)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        AssertSuccess(result, "Common.Presentation must not depend on Infrastructure.");
    }

    [Fact]
    public void Decorators_Should_BeInternalAndSealed()
    {
        // They are wiring, not extension points. Public decorators invite a
        // module to new one up instead of letting the container apply it.
        Type[] decorators =
        [
            .. Assemblies.CommonApplication
                .GetTypes()
                .Where(t => t.IsClass && t.Name.Contains("Handler", StringComparison.Ordinal)
                            && t.DeclaringType?.Name.EndsWith("Decorator", StringComparison.Ordinal) == true)
        ];

        Assert.NotEmpty(decorators);

        foreach (Type decorator in decorators)
        {
            Assert.True(decorator is { IsSealed: true, IsPublic: false } or { IsSealed: true, IsNestedPublic: false },
                $"{decorator.FullName} must be internal and sealed.");
        }
    }

    private static string[] ReferencedAssemblyNames(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty)];

    private static void AssertSuccess(TestResult result, string because)
    {
        string offenders = result.FailingTypeNames is null
            ? string.Empty
            : string.Join(", ", result.FailingTypeNames);

        Assert.True(result.IsSuccessful, $"{because} Offending types: {offenders}");
    }
}

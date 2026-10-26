using CashFlow.Worker;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWorker(builder.Configuration);

await builder.Build().RunAsync();

/// <summary>
/// Kept internal on purpose. The Worker reaches ASP.NET Core through the
/// modules' Presentation assemblies, and the .NET 10 generator would otherwise
/// make this <c>Program</c> public, clashing with the API's in the test projects.
/// </summary>
internal partial class Program;

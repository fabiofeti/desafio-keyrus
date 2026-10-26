using Microsoft.AspNetCore.Routing;

namespace CashFlow.Common.Presentation.Endpoints;

/// <summary>
/// One endpoint, one class, mirroring the folder of the slice it exposes.
/// Discovered by assembly scanning, so adding an endpoint touches no shared file.
/// </summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}

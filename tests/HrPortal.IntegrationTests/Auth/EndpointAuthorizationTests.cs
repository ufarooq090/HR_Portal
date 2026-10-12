using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HrPortal.IntegrationTests.Auth;

/// <summary>Guards the rule that every endpoint declares its authorisation explicitly.</summary>
[Collection(ApiCollection.Name)]
public class EndpointAuthorizationTests(HrPortalApiFactory factory)
{
    [Fact]
    public void Every_endpoint_declares_a_named_policy_or_allows_anonymous()
    {
        _ = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        Assert.NotEmpty(endpoints);

        var missing = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null
                && !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => !string.IsNullOrEmpty(a.Policy)))
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        Assert.True(missing.Count == 0, "Endpoints without an explicit policy: " + string.Join(", ", missing));
    }
}

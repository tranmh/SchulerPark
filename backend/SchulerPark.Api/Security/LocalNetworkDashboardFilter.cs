namespace SchulerPark.Api.Security;

using Hangfire.Dashboard;

/// <summary>
/// Restricts the Hangfire dashboard to requests originating on the local machine or
/// a private network. The dashboard is only mapped in Development, but this filter
/// is defence-in-depth: if a production box is ever started with
/// ASPNETCORE_ENVIRONMENT=Development by mistake (e.g. the wrong compose overlay),
/// internet clients still can't reach the job dashboard — UseForwardedHeaders has
/// already rewritten RemoteIpAddress to the real client address by the time this
/// runs, so requests proxied by Caddy show their public source IP and are refused.
/// </summary>
public class LocalNetworkDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var remoteIp = context.GetHttpContext().Connection.RemoteIpAddress;
        return remoteIp != null && ClientNetwork.IsPrivateOrLocal(remoteIp);
    }
}

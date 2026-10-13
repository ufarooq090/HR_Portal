namespace HrPortal.Api.Auth;

/// <summary>Claim types as issued by Entra ID access tokens (inbound claim mapping is disabled).</summary>
public static class HrClaimTypes
{
    public const string ObjectId = "oid";
    public const string Name = "name";
    public const string PreferredUsername = "preferred_username";
    public const string Roles = "roles";
}

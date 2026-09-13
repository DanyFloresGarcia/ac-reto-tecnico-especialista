namespace EventService.Api.Security;

/// <summary>
/// Policy names are provider-agnostic on purpose (Constitucion §5.1): migrating the
/// token issuer to Keycloak in a future phase must only change the validator/authority,
/// never these names nor the [Authorize] attributes that reference them.
/// </summary>
public static class AuthorizationPolicies
{
    public const string RequireAdminRole = "RequireAdminRole";
    public const string RequireAuthenticatedUser = "RequireAuthenticatedUser";
}

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string User = "User";
}

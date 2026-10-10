using Microsoft.AspNetCore.Authorization;

namespace Einvoice.Api.Auth;

public static class ScopeAuthorizationExtensions
{
    public static AuthorizationPolicyBuilder RequireScope(this AuthorizationPolicyBuilder builder, string scope)
    {
        builder.RequireAssertion(context =>
            context.User.Claims.Any(c =>
                (c.Type == "scope" || c.Type == "scp") &&
                c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope)));
        return builder;
    }
}

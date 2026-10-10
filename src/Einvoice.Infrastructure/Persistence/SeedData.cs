using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Einvoice.Infrastructure.Persistence;

public static class SeedData
{
    public static async Task EnsureSeededAsync(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var clientId = config["Oidc:ClientId"] ?? "einvoice-client";
        var clientSecret = config["Oidc:ClientSecret"]
            ?? throw new InvalidOperationException("Oidc:ClientSecret must be configured (env Oidc__ClientSecret).");

        var applicationManager = services.GetRequiredService<IOpenIddictApplicationManager>();
        var scopeManager = services.GetRequiredService<IOpenIddictScopeManager>();

        foreach (var scope in new[] { "invoice:read", "invoice:write" })
        {
            if (await scopeManager.FindByNameAsync(scope) is null)
                await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
                {
                    Name = scope,
                    DisplayName = scope,
                    Description = $"Access to {scope} operations"
                });
        }

        if (await applicationManager.FindByClientIdAsync(clientId) is null)
        {
            await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
                ClientType = ClientTypes.Confidential,
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Scope + "invoice:read",
                    Permissions.Prefixes.Scope + "invoice:write"
                }
            });
        }
    }
}

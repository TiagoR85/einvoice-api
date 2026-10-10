using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Einvoice.Infrastructure.Persistence;

public static class SeedData
{
    public static async Task EnsureSeededAsync(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IHostEnvironment>();
        var clientId = config["Oidc:ClientId"] ?? "einvoice-client";
        var clientSecret = config["Oidc:ClientSecret"];

        if (string.IsNullOrWhiteSpace(clientSecret) && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Oidc:ClientSecret must be configured (env Oidc__ClientSecret). " +
                "An empty or whitespace-only secret is not allowed outside the Development environment.");
        }

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

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            return;
        }

        var application = await applicationManager.FindByClientIdAsync(clientId);
        if (application is null)
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
        else if (!await applicationManager.ValidateClientSecretAsync(application, clientSecret))
        {
            // The stored secret is missing/empty or no longer matches the configured
            // one (e.g. the client was originally seeded with an empty secret).
            // UpdateAsync hashes the new secret the same way CreateAsync does.
            await applicationManager.UpdateAsync(application, clientSecret);
        }
    }
}

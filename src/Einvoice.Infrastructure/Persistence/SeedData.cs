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
        var workerClientId = config["Oidc:WorkerClientId"] ?? "einvoice-worker";
        var workerClientSecret = config["Oidc:WorkerClientSecret"];

        if (!environment.IsDevelopment())
        {
            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException(
                    "Oidc:ClientSecret must be configured (env Oidc__ClientSecret). " +
                    "An empty or whitespace-only secret is not allowed outside the Development environment.");
            }

            if (string.IsNullOrWhiteSpace(workerClientSecret))
            {
                throw new InvalidOperationException(
                    "Oidc:WorkerClientSecret must be configured (env Oidc__WorkerClientSecret). " +
                    "An empty or whitespace-only secret is not allowed outside the Development environment.");
            }
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
            return; // Development without secrets: nothing to seed
        }

        await EnsureClientAsync(applicationManager, clientId, clientSecret,
            "invoice:read", "invoice:write");

        if (!string.IsNullOrWhiteSpace(workerClientSecret))
        {
            await EnsureClientAsync(applicationManager, workerClientId, workerClientSecret,
                "invoice:read");
        }
    }

    private static async Task EnsureClientAsync(
        IOpenIddictApplicationManager applicationManager,
        string clientId,
        string clientSecret,
        params string[] scopes)
    {
        var application = await applicationManager.FindByClientIdAsync(clientId);
        if (application is null)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
                ClientType = ClientTypes.Confidential,
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials
                }
            };
            foreach (var scope in scopes)
                descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);

            await applicationManager.CreateAsync(descriptor);
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

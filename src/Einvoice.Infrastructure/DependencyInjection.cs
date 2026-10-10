using Einvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Einvoice.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<EinvoiceDbContext>(options =>
        {
            options.UseNpgsql(configuration.GetConnectionString("Postgres"));
            options.UseOpenIddict();
        });

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<EinvoiceDbContext>();
            })
            .AddServer(options =>
            {
                options.SetTokenEndpointUris("/connect/token");
                options.AllowClientCredentialsFlow();
                options.RegisterScopes("invoice:read", "invoice:write");
                options.AddDevelopmentEncryptionCertificate();
                options.AddDevelopmentSigningCertificate();
                options.DisableAccessTokenEncryption();
                options.AddEventHandler<OpenIddictServerEvents.HandleTokenRequestContext>(descriptor =>
                    descriptor.UseInlineHandler(context =>
                    {
                        if (context.Request.IsClientCredentialsGrantType())
                        {
                            var identity = new ClaimsIdentity(
                                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                                Claims.Name,
                                Claims.Role);

                            identity.SetClaim(Claims.Subject, context.Request.ClientId!);

                            var principal = new ClaimsPrincipal(identity);
                            principal.SetScopes(context.Request.GetScopes());
                            context.Principal = principal;
                        }

                        return ValueTask.CompletedTask;
                    }));
                options.UseAspNetCore()
                    .DisableTransportSecurityRequirement();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis");
        });

        return services;
    }
}

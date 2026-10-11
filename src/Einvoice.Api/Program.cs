using Einvoice.Api.Auth;
using Einvoice.Api.Middleware;
using Einvoice.Api.Modules.Documents;
using Einvoice.Api.Modules.Parties;
using Einvoice.Infrastructure;
using Einvoice.Infrastructure.HealthChecks;
using Einvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("invoice:read", p => p.RequireAuthenticatedUser().RequireScope("invoice:read"));
    options.AddPolicy("invoice:write", p => p.RequireAuthenticatedUser().RequireScope("invoice:write"));
});
builder.Services.AddHealthChecks()
    .AddCheck<Einvoice.Infrastructure.HealthChecks.PostgresHealthCheck>("postgres")
    .AddCheck<Einvoice.Infrastructure.HealthChecks.RedisHealthCheck>("redis")
    .AddCheck<Einvoice.Infrastructure.HealthChecks.RabbitMqHealthCheck>("rabbitmq");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EinvoiceDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.EnsureSeededAsync(scope.ServiceProvider);
}

app.UseMiddleware<Einvoice.Api.Middleware.RedisRateLimitMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous();

app.MapParties();
app.MapDocuments();

app.Run();

public partial class Program { }

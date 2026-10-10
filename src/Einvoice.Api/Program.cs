using Einvoice.Api.Auth;
using Einvoice.Infrastructure;
using Einvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("invoice:read", p => p.RequireAuthenticatedUser().RequireScope("invoice:read"));
    options.AddPolicy("invoice:write", p => p.RequireAuthenticatedUser().RequireScope("invoice:write"));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EinvoiceDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.EnsureSeededAsync(scope.ServiceProvider);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
    .AllowAnonymous();

app.Run();

public partial class Program { }

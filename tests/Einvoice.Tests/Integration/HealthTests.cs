using System.Net;
using Einvoice.Tests.Infrastructure;
using Xunit;

namespace Einvoice.Tests.Integration;

[Collection("containers")]
public class HealthTests
{
    private readonly ContainersFixture _containers;

    public HealthTests(ContainersFixture containers) => _containers = containers;

    [Fact]
    public async Task Live_Returns200()
    {
        var factory = new EinvoiceApiFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_Returns200_WhenAllDependenciesUp()
    {
        var factory = new EinvoiceApiFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClubHub.Api.Tests;

public sealed class ForwardedHeadersConfigurationTests
{
    [Fact]
    public async Task ConfiguredProxyAddressesAndNetworksAreTrusted()
    {
        await using var baseFactory = new ClubHubWebApplicationFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ForwardedHeaders:KnownProxies:0"] = "10.20.30.40",
                    ["ForwardedHeaders:KnownIPNetworks:0"] = "172.30.0.0/24"
                })));

        var options = factory.Services
            .GetRequiredService<IOptions<ForwardedHeadersOptions>>()
            .Value;

        Assert.Contains(IPAddress.Parse("10.20.30.40"), options.KnownProxies);
        Assert.Contains(
            options.KnownIPNetworks,
            network => network.Contains(IPAddress.Parse("172.30.0.25")));
    }

    [Fact]
    public async Task UnrestrictedProxyNetworkIsRejected()
    {
        await using var baseFactory = new ClubHubWebApplicationFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ForwardedHeaders:KnownIPNetworks:0"] = "0.0.0.0/0"
                })));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Services
            .GetRequiredService<IOptions<ForwardedHeadersOptions>>()
            .Value);

        Assert.Contains("invalid or unrestricted CIDR network", exception.Message);
    }
}

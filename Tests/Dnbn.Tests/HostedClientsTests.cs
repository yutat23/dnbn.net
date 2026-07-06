using Dnbn.Core;
using Dnbn.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dnbn.Tests;

public class HostedClientsTests
{
  private sealed class FixedClientFactory : ITcpMessengerFactory
  {
    private readonly ITcpClient _client;

    public FixedClientFactory(ITcpClient client) => _client = client;

    public ITcpClient CreateClient(string name)
        => name == _client.Name ? _client : throw new ArgumentException("Unknown client", nameof(name));

    public ITcpServer CreateServer(string name)
        => throw new NotSupportedException();
  }

  [Theory]
  [InlineData("dnbn.net")]
  [InlineData("TcpMessenger")]
  public async Task AddDnbnNetHostedClients_RegistersStableKeyedClients(string section)
  {
    var values = new Dictionary<string, string?>
    {
      [$"{section}:Clients:0:Name"] = "ClientA",
      [$"{section}:Clients:0:RemoteHost"] = "127.0.0.1",
      [$"{section}:Clients:0:RemotePort"] = "5001",
      [$"{section}:Clients:1:Name"] = "ClientB",
      [$"{section}:Clients:1:RemoteHost"] = "127.0.0.1",
      [$"{section}:Clients:1:RemotePort"] = "5002",
    };
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddDnbnNet(configuration);
    services.AddDnbnNetHostedClients(configuration);

    await using var provider = services.BuildServiceProvider();
    var clients = provider.GetRequiredService<IDnbnClientCollection>();
    var keyedA = provider.GetRequiredKeyedService<ITcpClient>("ClientA");

    Assert.Equal(new[] { "ClientA", "ClientB" }, clients.Names);
    Assert.Same(keyedA, clients.GetClient("ClientA"));
    Assert.Same(keyedA, provider.GetRequiredKeyedService<ITcpClient>("ClientA"));
    Assert.Null(clients.GetClient("Missing"));
    Assert.Equal(2, clients.GetAllClients().Count());
  }

  [Fact]
  public async Task HostedClientService_ConnectsOnStart_AndDisconnectsOnStop()
  {
    var values = new Dictionary<string, string?>
    {
      ["dnbn.net:Clients:0:Name"] = "HostedClient",
      ["dnbn.net:Clients:0:RemoteHost"] = "127.0.0.1",
      ["dnbn.net:Clients:0:RemotePort"] = "5001",
    };
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    var transport = new MockTransport();
    var client = new Dnbn.Core.TcpClient(new Dnbn.Configuration.ClientConfig
    {
      Name = "HostedClient",
      RemoteHost = "127.0.0.1",
      RemotePort = 5001,
      MessageTerminator = "\n",
    }, transport);
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddDnbnNet(configuration);
    services.AddSingleton<ITcpMessengerFactory>(new FixedClientFactory(client));
    services.AddDnbnNetHostedClients(configuration);

    await using var provider = services.BuildServiceProvider();
    var hostedService = provider.GetServices<IHostedService>().Single();

    await hostedService.StartAsync(CancellationToken.None);
    await TestWait.UntilAsync(() => client.IsConnected);
    await hostedService.StopAsync(CancellationToken.None);

    Assert.False(client.IsConnected);
    Assert.Equal(1, transport.ConnectCalls);
  }
}

using Dnbn.Configuration;

namespace Dnbn.Tests;

public class ConfigurationValidationTests
{
  [Fact]
  public void Validate_RejectsDuplicateClientNames()
  {
    var config = new TcpMessengerConfig
    {
      Clients =
      [
        new ClientConfig { Name = "duplicate", RemoteHost = "127.0.0.1", RemotePort = 1 },
        new ClientConfig { Name = "duplicate", RemoteHost = "127.0.0.1", RemotePort = 2 }
      ]
    };

    Assert.Throws<InvalidOperationException>(() => TcpMessengerConfigValidator.Validate(config));
  }

  [Fact]
  public void Validate_RejectsUnimplementedHeaderBasedIdentification()
  {
    var config = new ServerConfig
    {
      Name = "server",
      ListenPort = 5000,
      ClientIdentification = ClientIdentification.HeaderBased
    };

    var error = Assert.Throws<InvalidOperationException>(() => TcpMessengerConfigValidator.ValidateServer(config));
    Assert.Contains("not implemented", error.Message);
  }

  [Fact]
  public void Validate_RejectsTerminatorAndLengthFramingCombination()
  {
    var config = new ClientConfig
    {
      Name = "client",
      RemoteHost = "127.0.0.1",
      RemotePort = 5000,
      MessageTerminator = "\n",
      FixedHeaderLength = 4,
      FixedBodyLength = 8
    };

    Assert.Throws<InvalidOperationException>(() => TcpMessengerConfigValidator.ValidateClient(config));
  }

  [Fact]
  public void Validate_RejectsFixedBodyAndLengthFieldCombination()
  {
    var config = new ClientConfig
    {
      Name = "client",
      RemoteHost = "127.0.0.1",
      RemotePort = 5000,
      FixedHeaderLength = 4,
      FixedBodyLength = 8,
      LengthFieldOffset = 0,
      LengthFieldLength = 2
    };

    var error = Assert.Throws<InvalidOperationException>(() => TcpMessengerConfigValidator.ValidateClient(config));
    Assert.Contains("cannot combine FixedBodyLength", error.Message);
  }
}

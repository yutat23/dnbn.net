using System.Text;
using System.Net;

namespace Dnbn.Configuration;

/// <summary>dnbn.net設定を接続開始前に検証する。</summary>
public static class TcpMessengerConfigValidator
{
  static TcpMessengerConfigValidator()
  {
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
  }

  /// <summary>ルート設定を検証する。</summary>
  public static void Validate(TcpMessengerConfig config)
  {
    if (config is null) throw new ArgumentNullException(nameof(config));
    ValidateUniqueNames(config.Servers.Select(server => server.Name), "server");
    ValidateUniqueNames(config.Clients.Select(client => client.Name), "client");
    foreach (var server in config.Servers) ValidateServer(server);
    foreach (var client in config.Clients) ValidateClient(client);
  }

  /// <summary>サーバー設定を検証する。</summary>
  public static void ValidateServer(ServerConfig config)
  {
    if (config is null) throw new ArgumentNullException(nameof(config));
    ValidateName(config.Name, "server");
    ValidatePort(config.ListenPort, nameof(config.ListenPort), config.Name);
    if (!IPAddress.TryParse(config.BindAddress, out _))
      throw new InvalidOperationException($"Server '{config.Name}' BindAddress must be an IPv4 or IPv6 address.");
    ValidateEncoding(config.Encoding, config.Name);
    if (config.ClientIdentification == ClientIdentification.HeaderBased)
    {
      throw new InvalidOperationException(
        $"Server '{config.Name}' uses ClientIdentification.HeaderBased, which is not implemented. Use SourceEndpoint.");
    }
    ValidateFraming(config.MessageTerminator, config.ReceiveMessageTerminator,
      config.FixedHeaderLength, config.FixedBodyLength, config.LengthFieldOffset,
      config.LengthFieldLength, config.MaxReceiveBufferBytes, config.Name);
    ValidateTcpKeepAlive(config.TcpKeepAlive, config.Name);
  }

  /// <summary>クライアント設定を検証する。</summary>
  public static void ValidateClient(ClientConfig config)
  {
    if (config is null) throw new ArgumentNullException(nameof(config));
    ValidateName(config.Name, "client");
    if (string.IsNullOrWhiteSpace(config.RemoteHost))
      throw new InvalidOperationException($"Client '{config.Name}' RemoteHost is required.");
    ValidatePort(config.RemotePort, nameof(config.RemotePort), config.Name);
    ValidateEncoding(config.Encoding, config.Name);
    ValidateFraming(config.MessageTerminator, config.ReceiveMessageTerminator,
      config.FixedHeaderLength, config.FixedBodyLength, config.LengthFieldOffset,
      config.LengthFieldLength, config.MaxReceiveBufferBytes, config.Name);
    if (config.TimeoutMilliseconds <= 0)
      throw new InvalidOperationException($"Client '{config.Name}' TimeoutMilliseconds must be greater than zero.");
    if (config.SendQueueCapacity <= 0)
      throw new InvalidOperationException($"Client '{config.Name}' SendQueueCapacity must be greater than zero.");
    if (config.MaxConcurrentResponseWaits is <= 0)
      throw new InvalidOperationException($"Client '{config.Name}' MaxConcurrentResponseWaits must be null or greater than zero.");
    if (config.WaitForConnectionOnSend && config.WaitForConnectionTimeoutMilliseconds <= 0)
      throw new InvalidOperationException($"Client '{config.Name}' WaitForConnectionTimeoutMilliseconds must be greater than zero.");
    ValidateRetryPolicy(config.RetryPolicy, config.Name, allowInfinite: false, "RetryPolicy");
    ValidateRetryPolicy(config.ConnectionRetryPolicy, config.Name, allowInfinite: true, "ConnectionRetryPolicy");
    ValidateTcpKeepAlive(config.TcpKeepAlive, config.Name);
    if (config.KeepAlive?.Enabled == true)
    {
      if (config.KeepAlive.IntervalSeconds <= 0)
        throw new InvalidOperationException($"Client '{config.Name}' KeepAlive.IntervalSeconds must be greater than zero.");
      if (string.IsNullOrEmpty(config.KeepAlive.Message))
        throw new InvalidOperationException($"Client '{config.Name}' KeepAlive.Message is required when KeepAlive is enabled.");
    }
  }

  private static void ValidateUniqueNames(IEnumerable<string> names, string kind)
  {
    var duplicate = names.Where(name => !string.IsNullOrWhiteSpace(name))
      .GroupBy(name => name, StringComparer.Ordinal)
      .FirstOrDefault(group => group.Count() > 1);
    if (duplicate != null)
      throw new InvalidOperationException($"Duplicate {kind} name '{duplicate.Key}'.");
  }

  private static void ValidateName(string name, string kind)
  {
    if (string.IsNullOrWhiteSpace(name))
      throw new InvalidOperationException($"The {kind} name is required.");
  }

  private static void ValidatePort(int port, string property, string name)
  {
    if (port is < 1 or > 65535)
      throw new InvalidOperationException($"'{name}' {property} must be between 1 and 65535.");
  }

  private static void ValidateEncoding(string encoding, string name)
  {
    if (string.IsNullOrWhiteSpace(encoding))
      throw new InvalidOperationException($"'{name}' Encoding is required.");
    try { _ = Encoding.GetEncoding(encoding); }
    catch (ArgumentException ex) { throw new InvalidOperationException($"'{name}' Encoding '{encoding}' is not supported.", ex); }
  }

  private static void ValidateFraming(
    string? sendTerminator, string[]? receiveTerminators,
    int? fixedHeaderLength, int? fixedBodyLength,
    int? lengthFieldOffset, int? lengthFieldLength,
    int? maxReceiveBufferBytes, string name)
  {
    if (receiveTerminators?.Any(string.IsNullOrEmpty) == true)
      throw new InvalidOperationException($"'{name}' ReceiveMessageTerminator cannot contain an empty value.");
    var hasTerminator = !string.IsNullOrEmpty(sendTerminator) || receiveTerminators is { Length: > 0 };
    var hasLengthFraming = fixedHeaderLength.HasValue || fixedBodyLength.HasValue ||
      lengthFieldOffset.HasValue || lengthFieldLength.HasValue;
    if (!hasTerminator && !hasLengthFraming)
      throw new InvalidOperationException($"'{name}' must configure terminator or length-based receive framing.");
    if (hasTerminator && hasLengthFraming)
      throw new InvalidOperationException($"'{name}' cannot combine terminator and length-based framing.");
    if (fixedHeaderLength is <= 0 || fixedBodyLength is < 0 || lengthFieldOffset is < 0)
      throw new InvalidOperationException($"'{name}' length framing contains a negative or zero value.");
    if (lengthFieldLength.HasValue && lengthFieldLength is not (1 or 2 or 4))
      throw new InvalidOperationException($"'{name}' LengthFieldLength must be 1, 2, or 4.");
    if (lengthFieldOffset.HasValue != lengthFieldLength.HasValue)
      throw new InvalidOperationException($"'{name}' LengthFieldOffset and LengthFieldLength must be specified together.");
    if (fixedBodyLength.HasValue && !fixedHeaderLength.HasValue)
      throw new InvalidOperationException($"'{name}' FixedBodyLength requires FixedHeaderLength.");
    if (fixedBodyLength.HasValue && lengthFieldLength.HasValue)
      throw new InvalidOperationException($"'{name}' cannot combine FixedBodyLength and a length field.");
    if (fixedHeaderLength.HasValue && !fixedBodyLength.HasValue && !lengthFieldLength.HasValue)
      throw new InvalidOperationException($"'{name}' FixedHeaderLength requires FixedBodyLength or a length field.");
    if (fixedHeaderLength.HasValue && lengthFieldOffset.HasValue &&
        lengthFieldOffset.Value + lengthFieldLength!.Value > fixedHeaderLength.Value)
      throw new InvalidOperationException($"'{name}' length field must fit inside FixedHeaderLength.");
    if (maxReceiveBufferBytes is <= 0)
      throw new InvalidOperationException($"'{name}' MaxReceiveBufferBytes must be greater than zero when specified.");
  }

  private static void ValidateRetryPolicy(RetryPolicy? policy, string name, bool allowInfinite, string property)
  {
    if (policy == null) return;
    if (policy.MaxRetryCount < (allowInfinite ? -1 : 0))
      throw new InvalidOperationException($"Client '{name}' {property}.MaxRetryCount is invalid.");
    if (policy.InitialDelayMs < 0 || policy.MaxDelayMs < 0)
      throw new InvalidOperationException($"Client '{name}' {property} delays cannot be negative.");
    if (policy.MaxDelayMs < policy.InitialDelayMs)
      throw new InvalidOperationException($"Client '{name}' {property}.MaxDelayMs cannot be less than InitialDelayMs.");
  }

  private static void ValidateTcpKeepAlive(TcpKeepAliveConfig? config, string name)
  {
    if (config?.Enabled != true) return;
    if (config.TimeSeconds <= 0 || config.IntervalSeconds <= 0 || config.RetryCount <= 0)
      throw new InvalidOperationException($"'{name}' TcpKeepAlive values must be greater than zero.");
  }
}

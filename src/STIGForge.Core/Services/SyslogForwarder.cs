using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace STIGForge.Core.Services;

public sealed class SyslogForwarder
{
  private readonly string _server;
  private readonly int _port;
  private readonly string _protocol;
  private readonly bool _useTls;

  public SyslogForwarder(string server, int port = 514, string protocol = "udp", bool useTls = false)
  {
    _server = server ?? throw new ArgumentNullException(nameof(server));
    _port = port;
    _protocol = protocol?.ToLowerInvariant() ?? "udp";
    _useTls = useTls && string.Equals(protocol?.ToLowerInvariant(), "tcp", StringComparison.Ordinal);
  }

  public async Task SendAsync(SyslogMessage message, CancellationToken ct)
  {
    var syslogMsg = FormatSyslog(message);
    var bytes = Encoding.UTF8.GetBytes(syslogMsg);

    if (_protocol == "tcp")
    {
      using var client = new TcpClient();
      await client.ConnectAsync(_server, _port, ct).ConfigureAwait(false);
      if (_useTls)
      {
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false,
          (sender, cert, chain, errors) => errors == SslPolicyErrors.None);
        await using var _ = sslStream.ConfigureAwait(false);
        await sslStream.AuthenticateAsClientAsync(
          new SslClientAuthenticationOptions { TargetHost = _server },
          ct).ConfigureAwait(false);
        await sslStream.WriteAsync(bytes, ct).ConfigureAwait(false);
      }
      else
      {
        await client.GetStream().WriteAsync(bytes, ct).ConfigureAwait(false);
      }
    }
    else
    {
      using var client = new UdpClient();
      ct.ThrowIfCancellationRequested();
      await client.SendAsync(bytes, bytes.Length, _server, _port).ConfigureAwait(false);
    }
  }

  public async Task SendBatchAsync(IEnumerable<SyslogMessage> messages, CancellationToken ct)
  {
    foreach (var message in messages)
    {
      await SendAsync(message, ct).ConfigureAwait(false);
    }
  }

  private static string FormatSyslog(SyslogMessage msg)
  {
    var priority = CalculatePriority(msg.Facility, msg.Severity);
    var timestamp = msg.Timestamp.ToString("MMM dd HH:mm:ss");
    return $"<{priority}>{timestamp} {msg.Host} {msg.AppName}: {msg.Message}";
  }

  private static int CalculatePriority(int facility, int severity)
  {
    return facility * 8 + severity;
  }
}

public sealed class CEFFormatter
{
  public string Format(CEFEvent evt)
  {
    var extensions = string.Join(" ", evt.Extensions.Select(kvp => $"{kvp.Key}={EscapeCEF(kvp.Value)}"));

    return $"CEF:0|{evt.DeviceVendor}|{evt.DeviceProduct}|{evt.DeviceVersion}|{evt.SignatureId}|{evt.Name}|{evt.Severity}|{extensions}";
  }

  private static string EscapeCEF(string value)
  {
    return value
      .Replace("\\", "\\\\")
      .Replace("=", "\\=")
      .Replace("|", "\\|")
      .Replace("\n", "\\n");
  }
}

public sealed class AuditLogForwarder
{
  private readonly SyslogForwarder? _syslog;
  private readonly CEFFormatter _cef;
  private readonly string _hostName;

  public AuditLogForwarder(SyslogForwarder? syslog = null)
  {
    _syslog = syslog;
    _cef = new CEFFormatter();
    _hostName = Dns.GetHostName();
  }

  public async Task ForwardComplianceEventAsync(
    string bundleId,
    string ruleId,
    string status,
    string? details,
    CancellationToken ct)
  {
    if (_syslog == null)
      return;

    var cefEvent = new CEFEvent
    {
      DeviceVendor = "STIGForge",
      DeviceProduct = "ComplianceEngine",
      DeviceVersion = "1.3.0",
      SignatureId = $"STIG-{ruleId}",
      Name = $"STIG Control {status}",
      Severity = status.ToLowerInvariant() == "fail" ? 6 : 3,
      Extensions = new Dictionary<string, string>
      {
        ["bundleId"] = bundleId,
        ["ruleId"] = ruleId,
        ["status"] = status,
        ["details"] = details ?? string.Empty,
        ["host"] = _hostName
      }
    };

    var syslogMsg = new SyslogMessage
    {
      Facility = 4,
      Severity = cefEvent.Severity,
      Timestamp = DateTimeOffset.UtcNow,
      Host = _hostName,
      AppName = "stigforge",
      Message = _cef.Format(cefEvent)
    };

    await _syslog.SendAsync(syslogMsg, ct).ConfigureAwait(false);
  }

  public async Task ForwardDriftEventAsync(
    string bundleId,
    string ruleId,
    string changeType,
    string previousState,
    string currentState,
    CancellationToken ct)
  {
    if (_syslog == null)
      return;

    var cefEvent = new CEFEvent
    {
      DeviceVendor = "STIGForge",
      DeviceProduct = "DriftDetection",
      DeviceVersion = "1.3.0",
      SignatureId = "DRIFT-001",
      Name = $"Baseline Drift Detected: {changeType}",
      Severity = 7,
      Extensions = new Dictionary<string, string>
      {
        ["bundleId"] = bundleId,
        ["ruleId"] = ruleId,
        ["changeType"] = changeType,
        ["previousState"] = previousState,
        ["currentState"] = currentState,
        ["host"] = _hostName
      }
    };

    var syslogMsg = new SyslogMessage
    {
      Facility = 4,
      Severity = 4,
      Timestamp = DateTimeOffset.UtcNow,
      Host = _hostName,
      AppName = "stigforge",
      Message = _cef.Format(cefEvent)
    };

    await _syslog.SendAsync(syslogMsg, ct).ConfigureAwait(false);
  }
}

public sealed class SyslogMessage
{
  public int Facility { get; set; } = 16;
  public int Severity { get; set; } = 6;
  public DateTimeOffset Timestamp { get; set; }
  public string Host { get; set; } = string.Empty;
  public string AppName { get; set; } = string.Empty;
  public string Message { get; set; } = string.Empty;
}

public sealed class CEFEvent
{
  public string DeviceVendor { get; set; } = string.Empty;
  public string DeviceProduct { get; set; } = string.Empty;
  public string DeviceVersion { get; set; } = string.Empty;
  public string SignatureId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public int Severity { get; set; }
  public Dictionary<string, string> Extensions { get; set; } = new();
}

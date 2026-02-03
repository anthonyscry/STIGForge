using STIGForge.Core.Abstractions;

namespace STIGForge.Core.Services;

public sealed class SystemClock : IClock
{
  public DateTimeOffset Now => DateTimeOffset.Now;
}

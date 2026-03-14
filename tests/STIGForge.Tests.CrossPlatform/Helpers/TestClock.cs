using STIGForge.Core.Abstractions;

namespace STIGForge.Tests.CrossPlatform.Helpers;

public sealed class TestClock : IClock
{
    public TestClock(DateTimeOffset now) => Now = now;
    public DateTimeOffset Now { get; set; }
    public void Advance(TimeSpan delta) => Now = Now.Add(delta);
}

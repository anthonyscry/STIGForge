using System;
using System.Threading;
using Xunit;
using Xunit.Sdk;

namespace STIGForge.UnitTests.Helpers;

/// <summary>
/// Runs a test method on an STA thread (required for WPF UI component instantiation).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
[XunitTestCaseDiscoverer("Xunit.Sdk.FactDiscoverer", "xunit.core")]
public sealed class StaFactAttribute : FactAttribute { }

/// <summary>
/// Helper to run actions on an STA thread for WPF tests.
/// </summary>
public static class StaThreadRunner
{
  public static void Run(Action action)
  {
    if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
    {
      action();
      return;
    }

    Exception? exception = null;
    var thread = new Thread(() =>
    {
      try
      {
        action();
      }
      catch (Exception ex)
      {
        exception = ex;
      }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (exception != null)
      throw new AggregateException("STA thread action failed.", exception);
  }

  public static T Run<T>(Func<T> func)
  {
    if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
      return func();

    T result = default!;
    Exception? exception = null;
    var thread = new Thread(() =>
    {
      try
      {
        result = func();
      }
      catch (Exception ex)
      {
        exception = ex;
      }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (exception != null)
      throw new AggregateException("STA thread action failed.", exception);

    return result;
  }
}

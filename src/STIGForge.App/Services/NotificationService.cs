using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace STIGForge.App.Services;

public enum NotificationType { Info, Success, Warning, Error }

public sealed class NotificationItem
{
    public string Message { get; init; } = string.Empty;
    public NotificationType Type { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
}

public sealed class NotificationService
{
    public ObservableCollection<NotificationItem> Notifications { get; } = new();

    public void Show(string message, NotificationType type = NotificationType.Info, int durationMs = 3000)
    {
        var item = new NotificationItem { Message = message, Type = type };
        Notifications.Add(item);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            Notifications.Remove(item);
        };
        timer.Start();
    }

    public void Success(string message) => Show(message, NotificationType.Success);
    public void Warn(string message) => Show(message, NotificationType.Warning, 5000);
    public void Error(string message) => Show(message, NotificationType.Error, 7000);
}

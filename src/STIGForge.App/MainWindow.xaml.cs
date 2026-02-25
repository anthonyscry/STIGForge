using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Apply;

namespace STIGForge.App;

public partial class MainWindow : Window
{
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyTitleBarColors();

        // Resolve services from DI container when available
        var app = Application.Current as App;
        ImportInboxScanner? importScanner = null;
        IVerificationWorkflowService? verifyService = null;
        Func<ApplyRequest, CancellationToken, Task<ApplyResult>>? runApply = null;

        if (app?.Services != null)
        {
            importScanner = app.Services.GetService<ImportInboxScanner>();
            verifyService = app.Services.GetService<IVerificationWorkflowService>();

            var applyRunner = app.Services.GetService<ApplyRunner>();
            if (applyRunner != null)
            {
                runApply = applyRunner.RunAsync;
            }
        }

        DataContext = new WorkflowViewModel(importScanner, verifyService, runApply);
    }

    private void ApplyTitleBarColors()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        if (TryResolveColor("WindowBackgroundBrush", out var captionColor))
        {
            var captionColorRef = ToColorRef(captionColor);
            _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionColorRef, sizeof(int));
        }

        if (TryResolveColor("AccentBrush", out var textColor))
        {
            var textColorRef = ToColorRef(textColor);
            _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref textColorRef, sizeof(int));
        }
    }

    private static bool TryResolveColor(string resourceKey, out Color color)
    {
        if (Application.Current?.Resources[resourceKey] is SolidColorBrush brush)
        {
            color = brush.Color;
            return true;
        }

        color = default;
        return false;
    }

    private static int ToColorRef(Color color)
    {
        return color.R | (color.G << 8) | (color.B << 16);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}

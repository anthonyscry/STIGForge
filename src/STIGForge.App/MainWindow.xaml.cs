using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Apply;

namespace STIGForge.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

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
}

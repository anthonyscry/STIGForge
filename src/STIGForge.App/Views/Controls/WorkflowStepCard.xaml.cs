using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace STIGForge.App.Views;

public partial class WorkflowStepCard : UserControl
{
    public static readonly DependencyProperty StepNameProperty =
        DependencyProperty.Register(
            nameof(StepName),
            typeof(string),
            typeof(WorkflowStepCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StepStateProperty =
        DependencyProperty.Register(
            nameof(StepState),
            typeof(StepState),
            typeof(WorkflowStepCard),
            new PropertyMetadata(StepState.Locked));

    public static readonly DependencyProperty ErrorMessageProperty =
        DependencyProperty.Register(
            nameof(ErrorMessage),
            typeof(string),
            typeof(WorkflowStepCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RunCommandProperty =
        DependencyProperty.Register(
            nameof(RunCommand),
            typeof(ICommand),
            typeof(WorkflowStepCard));

    public static readonly DependencyProperty RunButtonLabelProperty =
        DependencyProperty.Register(
            nameof(RunButtonLabel),
            typeof(string),
            typeof(WorkflowStepCard),
            new PropertyMetadata("Run"));

    public static readonly DependencyProperty RunButtonAutomationNameProperty =
        DependencyProperty.Register(
            nameof(RunButtonAutomationName),
            typeof(string),
            typeof(WorkflowStepCard),
            new PropertyMetadata("Run step"));

    public static readonly DependencyProperty RecoveryButtonAutomationNameProperty =
        DependencyProperty.Register(
            nameof(RecoveryButtonAutomationName),
            typeof(string),
            typeof(WorkflowStepCard),
            new PropertyMetadata("Retry step"));

    public static readonly DependencyProperty RunButtonToolTipProperty =
        DependencyProperty.Register(
            nameof(RunButtonToolTip),
            typeof(string),
            typeof(WorkflowStepCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RecoveryButtonToolTipProperty =
        DependencyProperty.Register(
            nameof(RecoveryButtonToolTip),
            typeof(string),
            typeof(WorkflowStepCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SecondaryCommandProperty =
        DependencyProperty.Register(
            nameof(SecondaryCommand),
            typeof(ICommand),
            typeof(WorkflowStepCard));

    public static readonly DependencyProperty SecondaryButtonLabelProperty =
        DependencyProperty.Register(
            nameof(SecondaryButtonLabel),
            typeof(string),
            typeof(WorkflowStepCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SecondaryButtonAutomationNameProperty =
        DependencyProperty.Register(
            nameof(SecondaryButtonAutomationName),
            typeof(string),
            typeof(WorkflowStepCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SecondaryButtonToolTipProperty =
        DependencyProperty.Register(
            nameof(SecondaryButtonToolTip),
            typeof(string),
            typeof(WorkflowStepCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SecondaryButtonTabIndexProperty =
        DependencyProperty.Register(
            nameof(SecondaryButtonTabIndex),
            typeof(int),
            typeof(WorkflowStepCard),
            new PropertyMetadata(0));

    public static readonly DependencyProperty RunButtonTabIndexProperty =
        DependencyProperty.Register(
            nameof(RunButtonTabIndex),
            typeof(int),
            typeof(WorkflowStepCard),
            new PropertyMetadata(0));

    public static readonly DependencyProperty RecoveryButtonTabIndexProperty =
        DependencyProperty.Register(
            nameof(RecoveryButtonTabIndex),
            typeof(int),
            typeof(WorkflowStepCard),
            new PropertyMetadata(1));

    public WorkflowStepCard()
    {
        InitializeComponent();
    }

    public string StepName
    {
        get => (string)GetValue(StepNameProperty);
        set => SetValue(StepNameProperty, value);
    }

    public StepState StepState
    {
        get => (StepState)GetValue(StepStateProperty);
        set => SetValue(StepStateProperty, value);
    }

    public string ErrorMessage
    {
        get => (string)GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public ICommand RunCommand
    {
        get => (ICommand)GetValue(RunCommandProperty);
        set => SetValue(RunCommandProperty, value);
    }

    public string RunButtonLabel
    {
        get => (string)GetValue(RunButtonLabelProperty);
        set => SetValue(RunButtonLabelProperty, value);
    }

    public string RunButtonAutomationName
    {
        get => (string)GetValue(RunButtonAutomationNameProperty);
        set => SetValue(RunButtonAutomationNameProperty, value);
    }

    public string RecoveryButtonAutomationName
    {
        get => (string)GetValue(RecoveryButtonAutomationNameProperty);
        set => SetValue(RecoveryButtonAutomationNameProperty, value);
    }

    public string RunButtonToolTip
    {
        get => (string)GetValue(RunButtonToolTipProperty);
        set => SetValue(RunButtonToolTipProperty, value);
    }

    public string RecoveryButtonToolTip
    {
        get => (string)GetValue(RecoveryButtonToolTipProperty);
        set => SetValue(RecoveryButtonToolTipProperty, value);
    }

    public int RunButtonTabIndex
    {
        get => (int)GetValue(RunButtonTabIndexProperty);
        set => SetValue(RunButtonTabIndexProperty, value);
    }

    public int RecoveryButtonTabIndex
    {
        get => (int)GetValue(RecoveryButtonTabIndexProperty);
        set => SetValue(RecoveryButtonTabIndexProperty, value);
    }

    public ICommand SecondaryCommand
    {
        get => (ICommand)GetValue(SecondaryCommandProperty);
        set => SetValue(SecondaryCommandProperty, value);
    }

    public string SecondaryButtonLabel
    {
        get => (string)GetValue(SecondaryButtonLabelProperty);
        set => SetValue(SecondaryButtonLabelProperty, value);
    }

    public string SecondaryButtonAutomationName
    {
        get => (string)GetValue(SecondaryButtonAutomationNameProperty);
        set => SetValue(SecondaryButtonAutomationNameProperty, value);
    }

    public string SecondaryButtonToolTip
    {
        get => (string)GetValue(SecondaryButtonToolTipProperty);
        set => SetValue(SecondaryButtonToolTipProperty, value);
    }

    public int SecondaryButtonTabIndex
    {
        get => (int)GetValue(SecondaryButtonTabIndexProperty);
        set => SetValue(SecondaryButtonTabIndexProperty, value);
    }
}

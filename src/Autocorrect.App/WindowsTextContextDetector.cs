using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using Autocorrect.Core;

namespace Autocorrect.App;

public sealed class WindowsTextContextDetector : ITextContextDetector
{
    public AppContextSnapshot GetActiveContext(CorrectionSettings settings)
    {
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var title = GetWindowTitle(foregroundWindow);
        var processName = GetProcessName(foregroundWindow);
        var className = GetClassName(foregroundWindow);

        var isTerminal = IsTerminal(processName, title, className);
        var isCodeEditor = IsCodeEditor(processName);
        var isBrowser = IsBrowser(processName);
        var isRemote = IsRemoteOrVm(processName);
        var blockedByUser = settings.BlockedProcesses.Contains(processName);
        var blockedByDeveloperMode = isCodeEditor && !settings.DeveloperModeEnabled;
        var blockedByTerminal = isTerminal;
        var blockedByRemote = isRemote;

        if (blockedByUser || blockedByDeveloperMode || blockedByTerminal || blockedByRemote)
        {
            return new AppContextSnapshot(
                processName,
                title,
                className,
                true,
                blockedByUser ? "blocked process" : "blocked context",
                IsEditable: false,
                IsTerminal: isTerminal,
                IsCodeEditor: isCodeEditor,
                IsBrowser: isBrowser,
                IsAllowedForAutocorrect: false,
                IsAllowedForAiOverlay: false);
        }

        var sensitiveReason = DetectSensitiveAutomationElement();
        if (sensitiveReason is not null)
        {
            return new AppContextSnapshot(
                processName,
                title,
                className,
                true,
                sensitiveReason,
                IsPasswordField: sensitiveReason.Contains("password", StringComparison.OrdinalIgnoreCase),
                IsTerminal: isTerminal,
                IsCodeEditor: isCodeEditor,
                IsBrowser: isBrowser,
                IsAllowedForAutocorrect: false,
                IsAllowedForAiOverlay: false);
        }

        return new AppContextSnapshot(
            processName,
            title,
            className,
            false,
            IsEditable: true,
            IsTerminal: isTerminal,
            IsCodeEditor: isCodeEditor,
            IsBrowser: isBrowser,
            IsLikelyPromptBox: isBrowser || title.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase),
            IsAllowedForAutocorrect: true,
            IsAllowedForAiOverlay: settings.AiOverlayEnabled);
    }

    private static string GetProcessName(nint window)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            return processId == 0 ? string.Empty : Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetWindowTitle(nint window)
    {
        var builder = new StringBuilder(512);
        NativeMethods.GetWindowText(window, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetClassName(nint window)
    {
        var builder = new StringBuilder(256);
        NativeMethods.GetClassName(window, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string? DetectSensitiveAutomationElement()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return null;
            }

            if (focused.Current.IsPassword)
            {
                return "password field";
            }

            var automationId = focused.Current.AutomationId ?? string.Empty;
            var name = focused.Current.Name ?? string.Empty;
            var combined = $"{automationId} {name}";
            if (combined.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("token", StringComparison.OrdinalIgnoreCase))
            {
                return "sensitive field name";
            }
        }
        catch
        {
            return "unreadable automation context";
        }

        return null;
    }

    private static bool IsTerminal(string processName, string title, string className)
    {
        return processName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("Terminal", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("ConsoleWindowClass", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCodeEditor(string processName)
    {
        return processName.Equals("Code", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("Cursor", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("devenv", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("rider64", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("idea64", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBrowser(string processName)
    {
        return processName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("firefox", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("brave", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRemoteOrVm(string processName)
    {
        return processName.Equals("mstsc", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("vmconnect", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("VirtualBoxVM", StringComparison.OrdinalIgnoreCase);
    }
}

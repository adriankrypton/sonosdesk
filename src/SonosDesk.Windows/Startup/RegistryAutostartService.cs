using System;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;

namespace SonosDesk.Windows.Startup;

public sealed class RegistryAutostartService(ILogger<RegistryAutostartService> logger) : IAutostartService
{
    public const string AutostartArgument = "--autostart";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SonosDesk";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string value && value.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private static string ExecutablePath => Environment.ProcessPath ?? string.Empty;

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled && ExecutablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, $"\"{ExecutablePath}\" {AutostartArgument}", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            logger.LogWarning("Autostart could not be changed: {Message}", ex.Message);
            throw new InvalidOperationException(L.T("win.autostart.change_failed"), ex);
        }
    }
}

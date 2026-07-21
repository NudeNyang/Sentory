using System.IO;
using Microsoft.Win32;
using Sentory.Infrastructure.Data;

namespace Sentory.App;

internal static class SentoryThemePreference
{
    private const string PersonalizeRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    public static bool ResolveIsDark(
        SentoryThemeMode mode,
        bool windowsDark) =>
        mode switch
        {
            SentoryThemeMode.Light => false,
            SentoryThemeMode.Dark => true,
            SentoryThemeMode.System => windowsDark,
            _ => false
        };

    public static bool ReadWindowsIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                PersonalizeRegistryPath);
            return key?.GetValue(AppsUseLightThemeValue) is int value &&
                   value == 0;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                  System.Security.SecurityException)
        {
            return false;
        }
    }
}

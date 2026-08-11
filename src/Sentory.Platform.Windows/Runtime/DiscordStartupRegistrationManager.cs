using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Sentory.Platform.Windows.Runtime;

internal readonly record struct DiscordStartupCommand(
    string Value,
    RegistryValueKind Kind);

internal readonly record struct DiscordStartupBackup(
    DiscordStartupCommand? OriginalCommand);

internal interface IDiscordStartupRegistry
{
    DiscordStartupCommand? ReadRunCommand();

    void WriteRunCommand(DiscordStartupCommand command);

    void DeleteRunCommand();

    DiscordStartupBackup? ReadBackup();

    void WriteBackup(DiscordStartupBackup backup);

    void DeleteBackup();
}

public sealed class DiscordStartupRegistrationManager
{
    public const string RestoreArgument = "--restore-discord-startup";
    internal const int DependencyWaitSeconds = 30;
    private const string TranslatorRunValueName =
        "NudeNyang Translator";

    private readonly IDiscordStartupRegistry _registry;
    private readonly Func<bool> _launcherExists;

    public DiscordStartupRegistrationManager()
    {
        var launcherPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Discord",
            "Update.exe");
        _registry = new WindowsDiscordStartupRegistry();
        LauncherPath = launcherPath;
        _launcherExists = () => File.Exists(launcherPath);
    }

    internal DiscordStartupRegistrationManager(
        IDiscordStartupRegistry registry,
        string launcherPath,
        Func<bool> launcherExists)
    {
        _registry = registry;
        LauncherPath = launcherPath;
        _launcherExists = launcherExists;
    }

    internal string LauncherPath { get; }

    internal string ManagedCommand
    {
        get
        {
            var powershellPath = Path.Combine(
                Environment.SystemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            var script = CreateManagedScript();
            var encoded = Convert.ToBase64String(
                Encoding.Unicode.GetBytes(script));
            return $"\"{powershellPath}\" -NoLogo -NoProfile " +
                   "-NonInteractive -WindowStyle Hidden " +
                   $"-EncodedCommand {encoded}";
        }
    }

    internal string CreateManagedScript()
    {
        var launcher = LauncherPath.Replace("'", "''");
        return
            "$runKey = 'HKCU:\\Software\\Microsoft\\Windows\\" +
            "CurrentVersion\\Run'; " +
            "$backupKey = 'HKCU:\\Software\\Sentory\\" +
            "DiscordStartupBackup'; " +
            "$translatorExpected = $null -ne (Get-ItemProperty " +
            $"-Path $runKey -Name '{TranslatorRunValueName}' " +
            "-ErrorAction SilentlyContinue); " +
            "$deadline = [DateTime]::UtcNow.AddSeconds(" +
            $"{DependencyWaitSeconds}); " +
            "do { " +
            "$sentoryReady = $null -ne (Get-Process -Name 'Sentory' " +
            "-ErrorAction SilentlyContinue); " +
            "$translatorReady = -not $translatorExpected -or " +
            "$null -ne (Get-Process -Name 'NudeNyang Translator' " +
            "-ErrorAction SilentlyContinue); " +
            "if ($sentoryReady -and $translatorReady) { break }; " +
            "Start-Sleep -Milliseconds 250 " +
            "} while ([DateTime]::UtcNow -lt $deadline); " +
            "if (-not $sentoryReady) { " +
            "$backup = Get-ItemProperty -Path $backupKey " +
            "-ErrorAction SilentlyContinue; " +
            "if ($null -ne $backup -and $backup.Managed -eq 1) { " +
            "if ($backup.OriginalPresent -eq 1 -and " +
            "$null -ne $backup.OriginalCommand) { " +
            "$propertyType = if ($backup.OriginalKind -eq 2) " +
            "{ 'ExpandString' } else { 'String' }; " +
            "New-ItemProperty -Path $runKey -Name 'Discord' " +
            "-Value $backup.OriginalCommand -PropertyType $propertyType " +
            "-Force | Out-Null " +
            "} else { Remove-ItemProperty -Path $runKey -Name 'Discord' " +
            "-ErrorAction SilentlyContinue }; " +
            "Remove-Item -Path $backupKey -Recurse -Force " +
            "-ErrorAction SilentlyContinue } }; " +
            $"& '{launcher}' '--processStart' 'Discord.exe' " +
            "'--process-start-args' " +
            "'\"--force-renderer-accessibility\"'";
    }

    public void Synchronize(bool shouldManage)
    {
        if (!shouldManage || !_launcherExists())
        {
            Restore();
            return;
        }

        var backup = _registry.ReadBackup();
        if (backup is null)
        {
            backup = new DiscordStartupBackup(
                _registry.ReadRunCommand());
            _registry.WriteBackup(backup.Value);
        }

        var managed = new DiscordStartupCommand(
            ManagedCommand,
            RegistryValueKind.String);
        if (_registry.ReadRunCommand() != managed)
        {
            _registry.WriteRunCommand(managed);
        }
    }

    public void Restore()
    {
        var backup = _registry.ReadBackup();
        if (backup is null)
        {
            return;
        }

        var current = _registry.ReadRunCommand();
        var currentIsManaged = current is not null &&
                               string.Equals(
                                   current.Value.Value,
                                   ManagedCommand,
                                   StringComparison.OrdinalIgnoreCase);
        if (currentIsManaged)
        {
            if (backup.Value.OriginalCommand is { } original)
            {
                _registry.WriteRunCommand(original);
            }
            else
            {
                _registry.DeleteRunCommand();
            }
        }

        _registry.DeleteBackup();
    }
}

internal sealed class WindowsDiscordStartupRegistry :
    IDiscordStartupRegistry
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DiscordValueName = "Discord";
    private const string BackupKeyPath =
        @"Software\Sentory\DiscordStartupBackup";
    private const string ManagedValueName = "Managed";
    private const string OriginalPresentValueName = "OriginalPresent";
    private const string OriginalCommandValueName = "OriginalCommand";
    private const string OriginalKindValueName = "OriginalKind";

    public DiscordStartupCommand? ReadRunCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        if (key?.GetValue(
                DiscordValueName,
                null,
                RegistryValueOptions.DoNotExpandEnvironmentNames) is not
            string command)
        {
            return null;
        }

        return new DiscordStartupCommand(
            command,
            NormalizeKind(key.GetValueKind(DiscordValueName)));
    }

    public void WriteRunCommand(DiscordStartupCommand command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(
            DiscordValueName,
            command.Value,
            NormalizeKind(command.Kind));
    }

    public void DeleteRunCommand()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.DeleteValue(DiscordValueName, throwOnMissingValue: false);
    }

    public DiscordStartupBackup? ReadBackup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(BackupKeyPath);
        if (key?.GetValue(ManagedValueName) is not int managed ||
            managed != 1)
        {
            return null;
        }

        var originalPresent =
            key.GetValue(OriginalPresentValueName) is int present &&
            present == 1;
        if (!originalPresent)
        {
            return new DiscordStartupBackup(null);
        }

        var command = key.GetValue(
            OriginalCommandValueName,
            null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        var kindValue = key.GetValue(OriginalKindValueName);
        if (command is null || kindValue is not int rawKind)
        {
            return null;
        }

        return new DiscordStartupBackup(
            new DiscordStartupCommand(
                command,
                NormalizeKind((RegistryValueKind)rawKind)));
    }

    public void WriteBackup(DiscordStartupBackup backup)
    {
        using var key = Registry.CurrentUser.CreateSubKey(BackupKeyPath);
        if (backup.OriginalCommand is not { } original)
        {
            key.SetValue(
                OriginalPresentValueName,
                0,
                RegistryValueKind.DWord);
            key.DeleteValue(
                OriginalCommandValueName,
                throwOnMissingValue: false);
            key.DeleteValue(
                OriginalKindValueName,
                throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(
                OriginalPresentValueName,
                1,
                RegistryValueKind.DWord);
            key.SetValue(
                OriginalCommandValueName,
                original.Value,
                RegistryValueKind.String);
            key.SetValue(
                OriginalKindValueName,
                (int)NormalizeKind(original.Kind),
                RegistryValueKind.DWord);
        }

        key.SetValue(ManagedValueName, 1, RegistryValueKind.DWord);
    }

    public void DeleteBackup() =>
        Registry.CurrentUser.DeleteSubKeyTree(
            BackupKeyPath,
            throwOnMissingSubKey: false);

    private static RegistryValueKind NormalizeKind(
        RegistryValueKind kind) =>
        kind == RegistryValueKind.ExpandString
            ? RegistryValueKind.ExpandString
            : RegistryValueKind.String;
}

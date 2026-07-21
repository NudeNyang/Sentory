using Microsoft.Win32;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordStartupRegistrationManagerTests
{
    private const string LauncherPath =
        @"C:\Users\tester\AppData\Local\Discord\Update.exe";

    [Fact]
    public void BacksUpExistingCommandAndAddsAccessibilityArgument()
    {
        var original = new DiscordStartupCommand(
            $"\"{LauncherPath}\" --processStart Discord.exe",
            RegistryValueKind.ExpandString);
        var registry = new FakeDiscordStartupRegistry
        {
            RunCommand = original
        };
        var manager = CreateManager(registry);

        manager.Synchronize(shouldManage: true);

        Assert.Equal(original, registry.Backup?.OriginalCommand);
        Assert.Equal(manager.ManagedCommand, registry.RunCommand?.Value);
        Assert.Equal(RegistryValueKind.String, registry.RunCommand?.Kind);
    }

    [Fact]
    public void ReappliesManagedCommandWithoutReplacingOriginalBackup()
    {
        var original = new DiscordStartupCommand(
            "original Discord startup",
            RegistryValueKind.String);
        var registry = new FakeDiscordStartupRegistry
        {
            RunCommand = original
        };
        var manager = CreateManager(registry);
        manager.Synchronize(shouldManage: true);
        registry.RunCommand = new DiscordStartupCommand(
            "Discord updater replacement",
            RegistryValueKind.String);

        manager.Synchronize(shouldManage: true);

        Assert.Equal(original, registry.Backup?.OriginalCommand);
        Assert.Equal(manager.ManagedCommand, registry.RunCommand?.Value);
    }

    [Fact]
    public void RestoresOriginalCommandWhenManagementStops()
    {
        var original = new DiscordStartupCommand(
            "original Discord startup",
            RegistryValueKind.ExpandString);
        var registry = new FakeDiscordStartupRegistry
        {
            RunCommand = original
        };
        var manager = CreateManager(registry);
        manager.Synchronize(shouldManage: true);

        manager.Synchronize(shouldManage: false);

        Assert.Equal(original, registry.RunCommand);
        Assert.Null(registry.Backup);
    }

    [Fact]
    public void RemovesManagedCommandWhenDiscordWasNotOriginallyRegistered()
    {
        var registry = new FakeDiscordStartupRegistry();
        var manager = CreateManager(registry);
        manager.Synchronize(shouldManage: true);

        manager.Synchronize(shouldManage: false);

        Assert.Null(registry.RunCommand);
        Assert.Null(registry.Backup);
    }

    [Fact]
    public void PreservesNewUserCommandWhenManagementStops()
    {
        var registry = new FakeDiscordStartupRegistry
        {
            RunCommand = new DiscordStartupCommand(
                "original Discord startup",
                RegistryValueKind.String)
        };
        var manager = CreateManager(registry);
        manager.Synchronize(shouldManage: true);
        var userCommand = new DiscordStartupCommand(
            "user changed Discord startup",
            RegistryValueKind.String);
        registry.RunCommand = userCommand;

        manager.Synchronize(shouldManage: false);

        Assert.Equal(userCommand, registry.RunCommand);
        Assert.Null(registry.Backup);
    }

    [Fact]
    public void PreservesUserDeletionWhenManagementStops()
    {
        var registry = new FakeDiscordStartupRegistry
        {
            RunCommand = new DiscordStartupCommand(
                "original Discord startup",
                RegistryValueKind.String)
        };
        var manager = CreateManager(registry);
        manager.Synchronize(shouldManage: true);
        registry.RunCommand = null;

        manager.Synchronize(shouldManage: false);

        Assert.Null(registry.RunCommand);
        Assert.Null(registry.Backup);
    }

    [Fact]
    public void MissingLauncherRestoresPreviousRegistration()
    {
        var original = new DiscordStartupCommand(
            "original Discord startup",
            RegistryValueKind.String);
        var registry = new FakeDiscordStartupRegistry
        {
            RunCommand = original
        };
        var manager = CreateManager(registry);
        manager.Synchronize(shouldManage: true);
        var unavailable = new DiscordStartupRegistrationManager(
            registry,
            LauncherPath,
            () => false);

        unavailable.Synchronize(shouldManage: true);

        Assert.Equal(original, registry.RunCommand);
        Assert.Null(registry.Backup);
    }

    private static DiscordStartupRegistrationManager CreateManager(
        FakeDiscordStartupRegistry registry) =>
        new(registry, LauncherPath, () => true);

    private sealed class FakeDiscordStartupRegistry :
        IDiscordStartupRegistry
    {
        public DiscordStartupCommand? RunCommand { get; set; }

        public DiscordStartupBackup? Backup { get; set; }

        public DiscordStartupCommand? ReadRunCommand() => RunCommand;

        public void WriteRunCommand(DiscordStartupCommand command) =>
            RunCommand = command;

        public void DeleteRunCommand() => RunCommand = null;

        public DiscordStartupBackup? ReadBackup() => Backup;

        public void WriteBackup(DiscordStartupBackup backup) =>
            Backup = backup;

        public void DeleteBackup() => Backup = null;
    }
}

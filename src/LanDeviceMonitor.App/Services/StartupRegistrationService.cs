using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace LanDeviceMonitor.Services;

internal sealed record StartupTaskRegistration(string? Source, string? UserId, bool Enabled);

internal sealed record StartupTaskDefinition(
    string Name,
    string Source,
    string UserSid,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    int TriggerType,
    string TriggerUserSid,
    int LogonType,
    int RunLevel,
    int MultipleInstances,
    bool DisallowStartIfOnBatteries,
    bool StopIfGoingOnBatteries,
    bool AllowHardTerminate,
    string ExecutionTimeLimit,
    bool StartWhenAvailable);

internal interface IStartupTaskScheduler
{
    StartupTaskRegistration? GetTask(string taskName);
    void RegisterTask(StartupTaskDefinition definition, bool update);
    void SetTaskEnabled(string taskName, bool enabled);
}

internal interface ILegacyStartupEntryStore
{
    string? ReadCommand(string valueName);
    bool DeleteCommandIfMatches(string valueName, string expectedCommand);
}

public sealed class StartupRegistrationService
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string LegacyValueName = "局域网设备助手";
    internal const string BackgroundArgument = "--background";
    internal const string TaskNamePrefix = "LanDeviceMonitor.Logon.";
    internal const string StartupSource = "LanDeviceMonitor.App/logon-v1";

    private const int LogonTrigger = 9;
    private const int InteractiveToken = 3;
    private const int LeastPrivilege = 0;
    private const int IgnoreNewInstances = 2;

    private readonly IStartupTaskScheduler _taskScheduler;
    private readonly ILegacyStartupEntryStore _legacyEntries;
    private readonly string _executablePath;
    private readonly string _userSid;
    private readonly string _taskName;
    private readonly string _source;

    public StartupRegistrationService()
        : this(
            new TaskSchedulerStartupStore(),
            new RegistryLegacyStartupEntryStore(),
            Environment.ProcessPath ?? throw new InvalidOperationException("无法确定应用程序路径。"),
            GetCurrentUserSid())
    {
    }

    internal StartupRegistrationService(
        IStartupTaskScheduler taskScheduler,
        ILegacyStartupEntryStore legacyEntries,
        string executablePath,
        string userSid,
        string? taskName = null,
        string source = StartupSource)
    {
        _taskScheduler = taskScheduler;
        _legacyEntries = legacyEntries;
        _executablePath = Path.GetFullPath(executablePath);
        _userSid = new SecurityIdentifier(userSid).Value;
        _taskName = taskName ?? TaskNamePrefix + _userSid;
        _source = source;
    }

    public bool IsEnabled()
    {
        var task = GetOwnedTask();
        var expectedCommand = BuildCommand(_executablePath);
        if (task is not null)
        {
            return task.Enabled;
        }

        return IsExactLegacyCommand(_legacyEntries.ReadCommand(LegacyValueName), expectedCommand);
    }

    public void SetEnabled(bool enabled)
    {
        var existingTask = GetOwnedTask();
        var expectedCommand = BuildCommand(_executablePath);
        var legacyCommand = _legacyEntries.ReadCommand(LegacyValueName);
        var hasExactLegacyCommand = IsExactLegacyCommand(legacyCommand, expectedCommand);

        if (enabled && legacyCommand is not null && !hasExactLegacyCommand)
        {
            throw new InvalidOperationException("发现同名但命令不属于本应用的旧启动项，已保留该项且未创建计划任务。");
        }

        if (!enabled)
        {
            DisableOwnedTaskAndMigrateLegacy(existingTask, hasExactLegacyCommand, expectedCommand);
            return;
        }

        var definition = CreateValidatedTaskDefinition();
        var previousEnabled = existingTask?.Enabled ?? false;
        var registered = false;
        try
        {
            _taskScheduler.RegisterTask(definition, update: existingTask is not null);
            registered = true;

            if (hasExactLegacyCommand &&
                !_legacyEntries.DeleteCommandIfMatches(LegacyValueName, expectedCommand))
            {
                throw new InvalidOperationException("旧启动项在迁移期间发生变化，已保留该项。");
            }
        }
        catch (Exception exception)
        {
            if (!registered)
            {
                throw;
            }

            try
            {
                _taskScheduler.SetTaskEnabled(_taskName, previousEnabled);
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    "迁移旧启动项失败，并且未能恢复计划任务的原启用状态。未修改其他启动项。",
                    new AggregateException(exception, rollbackException));
            }

            throw new InvalidOperationException(
                "迁移旧启动项失败，计划任务已恢复为原启用状态；旧启动项及其他软件的启动项均已保留。",
                exception);
        }
    }

    internal static string BuildCommand(string executablePath) =>
        $"\"{Path.GetFullPath(executablePath)}\" {BackgroundArgument}";

    private StartupTaskDefinition CreateTaskDefinition() => new(
        _taskName,
        _source,
        _userSid,
        _executablePath,
        BackgroundArgument,
        Path.GetDirectoryName(_executablePath) ?? string.Empty,
        LogonTrigger,
        _userSid,
        InteractiveToken,
        LeastPrivilege,
        IgnoreNewInstances,
        DisallowStartIfOnBatteries: false,
        StopIfGoingOnBatteries: false,
        AllowHardTerminate: false,
        ExecutionTimeLimit: "PT0S",
        StartWhenAvailable: true);

    private StartupTaskDefinition CreateValidatedTaskDefinition()
    {
        if (!File.Exists(_executablePath))
        {
            throw new InvalidOperationException("无法确定应用程序路径，未修改开机启动设置。");
        }

        return CreateTaskDefinition();
    }

    private StartupTaskRegistration? GetOwnedTask()
    {
        var task = _taskScheduler.GetTask(_taskName);
        if (task is null)
        {
            return null;
        }

        if (!string.Equals(task.Source, _source, StringComparison.Ordinal) ||
            !PrincipalMatches(task.UserId, _userSid))
        {
            throw new InvalidOperationException("发现同名但不属于本应用和当前用户的计划任务，已保留该任务且未修改。");
        }

        return task;
    }

    private void DisableOwnedTaskAndMigrateLegacy(
        StartupTaskRegistration? existingTask,
        bool hasExactLegacyCommand,
        string expectedCommand)
    {
        var taskWasEnabled = existingTask?.Enabled == true;
        if (taskWasEnabled)
        {
            _taskScheduler.SetTaskEnabled(_taskName, false);
        }

        if (!hasExactLegacyCommand)
        {
            return;
        }

        try
        {
            if (_legacyEntries.DeleteCommandIfMatches(LegacyValueName, expectedCommand))
            {
                return;
            }

            throw new InvalidOperationException("旧启动项在迁移期间发生变化，已保留该项。");
        }
        catch (Exception exception)
        {
            if (taskWasEnabled)
            {
                try
                {
                    _taskScheduler.SetTaskEnabled(_taskName, true);
                }
                catch (Exception rollbackException)
                {
                    throw new InvalidOperationException(
                        "关闭旧启动项失败，并且未能恢复计划任务原启用状态。未修改其他启动项。",
                        new AggregateException(exception, rollbackException));
                }
            }

            throw new InvalidOperationException(
                "无法安全迁移旧启动项，已恢复计划任务原启用状态；其他软件的启动项未修改。",
                exception);
        }
    }

    private static bool IsExactLegacyCommand(string? actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static bool PrincipalMatches(string? configuredUser, string expectedSid)
    {
        if (string.IsNullOrWhiteSpace(configuredUser))
        {
            return false;
        }

        if (string.Equals(configuredUser, expectedSid, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var sid = (SecurityIdentifier)new NTAccount(configuredUser).Translate(typeof(SecurityIdentifier));
            return string.Equals(sid.Value, expectedSid, StringComparison.OrdinalIgnoreCase);
        }
        catch (IdentityNotMappedException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string GetCurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? throw new InvalidOperationException("无法确定当前用户 SID。");
    }

    private sealed class RegistryLegacyStartupEntryStore : ILegacyStartupEntryStore
    {
        public string? ReadCommand(string valueName)
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return runKey?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }

        public bool DeleteCommandIfMatches(string valueName, string expectedCommand)
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (runKey is null)
            {
                return true;
            }

            var rawValue = runKey.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (rawValue is null)
            {
                return true;
            }

            if (rawValue is not string command ||
                !IsExactLegacyCommand(command, expectedCommand))
            {
                return false;
            }

            runKey.DeleteValue(valueName, throwOnMissingValue: false);
            return true;
        }
    }

    internal sealed class TaskSchedulerStartupStore : IStartupTaskScheduler
    {
        private const int TaskCreate = 2;
        private const int TaskUpdate = 4;
        private const int TaskLogonInteractiveToken = 3;
        private const int FileNotFound = unchecked((int)0x80070002);
        private const int PathNotFound = unchecked((int)0x80070003);

        public StartupTaskRegistration? GetTask(string taskName)
        {
            using var scope = new ComObjectScope();
            dynamic service = Connect(scope);
            dynamic rootFolder = scope.Track(service.GetFolder("\\"));

            dynamic task;
            try
            {
                task = scope.Track(rootFolder.GetTask(taskName));
            }
            catch (COMException exception) when (IsTaskNotFound(exception))
            {
                return null;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }

            dynamic definition = scope.Track(task.Definition);
            dynamic registration = scope.Track(definition.RegistrationInfo);
            dynamic principal = scope.Track(definition.Principal);
            return new StartupTaskRegistration(
                Convert.ToString(registration.Source),
                Convert.ToString(principal.UserId),
                Convert.ToBoolean(task.Enabled));
        }

        public void RegisterTask(StartupTaskDefinition definition, bool update)
        {
            using var scope = new ComObjectScope();
            dynamic service = Connect(scope);
            dynamic rootFolder = scope.Track(service.GetFolder("\\"));
            dynamic taskDefinition = scope.Track(service.NewTask(0));

            dynamic registration = scope.Track(taskDefinition.RegistrationInfo);
            registration.Source = definition.Source;
            registration.Author = definition.UserSid;
            registration.Description = "局域网设备助手：用户登录后立即启动";

            dynamic principal = scope.Track(taskDefinition.Principal);
            principal.UserId = definition.UserSid;
            principal.LogonType = definition.LogonType;
            principal.RunLevel = definition.RunLevel;

            dynamic settings = scope.Track(taskDefinition.Settings);
            settings.MultipleInstances = definition.MultipleInstances;
            settings.DisallowStartIfOnBatteries = definition.DisallowStartIfOnBatteries;
            settings.StopIfGoingOnBatteries = definition.StopIfGoingOnBatteries;
            settings.AllowHardTerminate = definition.AllowHardTerminate;
            settings.ExecutionTimeLimit = definition.ExecutionTimeLimit;
            settings.StartWhenAvailable = definition.StartWhenAvailable;
            settings.Enabled = true;

            dynamic triggers = scope.Track(taskDefinition.Triggers);
            dynamic trigger = scope.Track(triggers.Create(definition.TriggerType));
            trigger.UserId = definition.TriggerUserSid;

            dynamic actions = scope.Track(taskDefinition.Actions);
            dynamic action = scope.Track(actions.Create(0));
            action.Path = definition.ExecutablePath;
            action.Arguments = definition.Arguments;
            action.WorkingDirectory = definition.WorkingDirectory;

            scope.Track(rootFolder.RegisterTaskDefinition(
                definition.Name,
                taskDefinition,
                update ? TaskUpdate : TaskCreate,
                definition.UserSid,
                Type.Missing,
                TaskLogonInteractiveToken,
                Type.Missing));
        }

        public void SetTaskEnabled(string taskName, bool enabled)
        {
            using var scope = new ComObjectScope();
            dynamic service = Connect(scope);
            dynamic rootFolder = scope.Track(service.GetFolder("\\"));
            dynamic task = scope.Track(rootFolder.GetTask(taskName));
            task.Enabled = enabled;
        }

        private static object Connect(ComObjectScope scope)
        {
            var serviceType = Type.GetTypeFromProgID("Schedule.Service")
                ?? throw new InvalidOperationException("Windows 任务计划程序不可用。");
            dynamic service = scope.Track(Activator.CreateInstance(serviceType)
                ?? throw new InvalidOperationException("无法连接 Windows 任务计划程序。"));
            service.Connect(Type.Missing, Type.Missing, Type.Missing, Type.Missing);
            return service;
        }

        private static bool IsTaskNotFound(COMException exception) =>
            exception.ErrorCode is FileNotFound or PathNotFound;
    }

    private sealed class ComObjectScope : IDisposable
    {
        private readonly Stack<object> _objects = new();

        public object Track(object value)
        {
            _objects.Push(value);
            return value;
        }

        public void Dispose()
        {
            while (_objects.TryPop(out var value))
            {
                if (!Marshal.IsComObject(value))
                {
                    continue;
                }

                try
                {
                    Marshal.FinalReleaseComObject(value);
                }
                catch (InvalidComObjectException)
                {
                }
            }
        }
    }
}

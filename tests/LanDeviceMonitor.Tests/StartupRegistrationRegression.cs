using System.Runtime.InteropServices;
using System.Security.Principal;
using LanDeviceMonitor.Services;

internal static class StartupRegistrationRegression
{
    private const string UserSid = "S-1-5-21-1000-1000-1000-1001";
    private const string LegacyValueName = "局域网设备助手";
    private static readonly string ExecutablePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("Could not determine the test executable path.");
    private static string ExpectedCommand => $"\"{Path.GetFullPath(ExecutablePath)}\" --background";
    private const string ExpectedTaskName = "LanDeviceMonitor.Logon.S-1-5-21-1000-1000-1000-1001";
    private const string ExpectedSource = "LanDeviceMonitor.App/logon-v1";

    internal static int Run()
    {
        EnablingMigratesTheExactLegacyEntryAndPreservesOtherStartupItems();
        EnablingRefreshesOnlyAnOwnedTask();
        AForeignScheduledTaskCollisionIsNeverOverwritten();
        ATaskOwnedByAnotherUserIsNeverChanged();
        DisablingOnlyDisablesAnOwnedTaskAndKeepsUnmatchedEntries();
        DisablingMigratesOnlyTheExactLegacyEntry();
        AChangedLegacyCommandIsNotClaimedOrDeleted();
        AFailedLegacyMigrationRollsBackTheNewTask();
        WindowsTaskSchedulerRoundTripsAnIsolatedOwnedTask();
        Console.WriteLine("Startup registration PASS: per-user logon task, exact migration, and unrelated startup preservation.");
        return 0;
    }

    private static void EnablingMigratesTheExactLegacyEntryAndPreservesOtherStartupItems()
    {
        var scheduler = new FakeTaskScheduler();
        scheduler.Tasks["OtherVendor.UpdateCheck"] = new("OtherVendor/startup-v1", UserSid, true);
        var legacy = new FakeLegacyStartupStore();
        legacy.Values[LegacyValueName] = ExpectedCommand;
        legacy.Values["OtherVendor"] = "C:\\OtherVendor\\Updater.exe";
        var service = CreateService(scheduler, legacy);

        service.SetEnabled(true);

        Require(scheduler.RegisterCount == 1, "Enabling did not register exactly one logon task.");
        Require(!scheduler.LastUpdate, "A new task was registered as an update.");
        var definition = scheduler.LastDefinition ?? throw new InvalidOperationException("No task definition was registered.");
        Require(definition.Name == ExpectedTaskName, "Task name was not scoped to this app and user SID.");
        Require(definition.Source == ExpectedSource, "Task ownership marker was incorrect.");
        Require(definition.UserSid == UserSid && definition.TriggerUserSid == UserSid,
            "Task principal and logon trigger must target the current user SID.");
        Require(definition.ExecutablePath == ExecutablePath && definition.Arguments == "--background",
            "Task did not launch the app with its background argument.");
        Require(definition.WorkingDirectory == Path.GetDirectoryName(Path.GetFullPath(ExecutablePath)),
            "Task working directory did not match the app folder.");
        Require(definition.TriggerType == 9 && definition.LogonType == 3 && definition.RunLevel == 0,
            "Task must use an interactive token at least privilege.");
        Require(definition.MultipleInstances == 2 && definition.StartWhenAvailable &&
                !definition.DisallowStartIfOnBatteries && !definition.StopIfGoingOnBatteries &&
                !definition.AllowHardTerminate && definition.ExecutionTimeLimit == "PT0S",
            "Task settings did not match ReTray's logon-task behavior.");
        Require(!legacy.Values.ContainsKey(LegacyValueName), "The exact old app entry was not migrated.");
        Require(legacy.Values["OtherVendor"] == "C:\\OtherVendor\\Updater.exe",
            "Another software's registry startup entry was changed.");
        Require(scheduler.Tasks["OtherVendor.UpdateCheck"].Enabled,
            "Another software's scheduled task was changed.");
    }

    private static void AForeignScheduledTaskCollisionIsNeverOverwritten()
    {
        var scheduler = new FakeTaskScheduler();
        scheduler.Tasks[ExpectedTaskName] = new("OtherProduct/logon-v1", UserSid, true);
        var legacy = new FakeLegacyStartupStore();
        legacy.Values[LegacyValueName] = ExpectedCommand;
        var service = CreateService(scheduler, legacy);

        ExpectCollision(() => service.SetEnabled(true));

        Require(scheduler.RegisterCount == 0, "A foreign task with the same name was overwritten.");
        Require(scheduler.Tasks[ExpectedTaskName] == new StartupTaskRegistration("OtherProduct/logon-v1", UserSid, true),
            "The foreign scheduled task was changed.");
        Require(legacy.Values[LegacyValueName] == ExpectedCommand,
            "The legacy entry was removed after a task-name collision.");
    }

    private static void EnablingRefreshesOnlyAnOwnedTask()
    {
        var scheduler = new FakeTaskScheduler();
        scheduler.Tasks[ExpectedTaskName] = new(ExpectedSource, UserSid, false);
        var service = CreateService(scheduler, new FakeLegacyStartupStore());

        service.SetEnabled(true);

        Require(scheduler.RegisterCount == 1 && scheduler.LastUpdate,
            "An existing owned task was not refreshed in place.");
        Require(scheduler.Tasks[ExpectedTaskName] == new StartupTaskRegistration(ExpectedSource, UserSid, true),
            "Refreshing the owned task did not enable it.");
    }

    private static void ATaskOwnedByAnotherUserIsNeverChanged()
    {
        var scheduler = new FakeTaskScheduler();
        scheduler.Tasks[ExpectedTaskName] = new(ExpectedSource, "S-1-5-21-2000-2000-2000-2002", true);
        var legacy = new FakeLegacyStartupStore();
        legacy.Values[LegacyValueName] = ExpectedCommand;
        var service = CreateService(scheduler, legacy);

        ExpectCollision(() => service.SetEnabled(false));

        Require(scheduler.RegisterCount == 0, "A task belonging to another user was updated.");
        Require(scheduler.Tasks[ExpectedTaskName] == new StartupTaskRegistration(ExpectedSource, "S-1-5-21-2000-2000-2000-2002", true),
            "A task belonging to another user was disabled.");
        Require(legacy.Values[LegacyValueName] == ExpectedCommand,
            "The old startup entry was removed after a user-identity collision.");
    }

    private static void DisablingOnlyDisablesAnOwnedTaskAndKeepsUnmatchedEntries()
    {
        var scheduler = new FakeTaskScheduler();
        scheduler.Tasks[ExpectedTaskName] = new(ExpectedSource, UserSid, true);
        scheduler.Tasks["OtherVendor.UpdateCheck"] = new("OtherVendor/startup-v1", UserSid, true);
        var legacy = new FakeLegacyStartupStore();
        legacy.Values[LegacyValueName] = "C:\\DifferentProgram\\launcher.exe";
        legacy.Values["OtherVendor"] = "C:\\OtherVendor\\Updater.exe";
        var service = CreateService(scheduler, legacy);

        service.SetEnabled(false);

        Require(scheduler.Tasks[ExpectedTaskName] == new StartupTaskRegistration(ExpectedSource, UserSid, false),
            "Disabling did not disable the owned task.");
        Require(scheduler.Tasks.ContainsKey(ExpectedTaskName), "Disabling deleted the owned task instead of disabling it.");
        Require(scheduler.Tasks["OtherVendor.UpdateCheck"].Enabled,
            "Disabling the app changed another software's scheduled task.");
        Require(legacy.Values[LegacyValueName] == "C:\\DifferentProgram\\launcher.exe",
            "A non-matching registry command was deleted.");
        Require(legacy.Values["OtherVendor"] == "C:\\OtherVendor\\Updater.exe",
            "Another software's registry startup entry was changed.");
    }

    private static void DisablingMigratesOnlyTheExactLegacyEntry()
    {
        var scheduler = new FakeTaskScheduler();
        var legacy = new FakeLegacyStartupStore();
        legacy.Values[LegacyValueName] = ExpectedCommand;
        legacy.Values["OtherVendor"] = "C:\\OtherVendor\\Updater.exe";
        var service = CreateService(scheduler, legacy);

        service.SetEnabled(false);

        Require(scheduler.RegisterCount == 0 && scheduler.Tasks.Count == 0,
            "Disabling a legacy entry unexpectedly created a scheduled task.");
        Require(!legacy.Values.ContainsKey(LegacyValueName), "The exact old app entry was not disabled.");
        Require(legacy.Values["OtherVendor"] == "C:\\OtherVendor\\Updater.exe",
            "Disabling the app changed another software's registry startup entry.");
    }

    private static void AChangedLegacyCommandIsNotClaimedOrDeleted()
    {
        var scheduler = new FakeTaskScheduler();
        var legacy = new FakeLegacyStartupStore();
        legacy.Values[LegacyValueName] = "C:\\DifferentProgram\\launcher.exe";
        var service = CreateService(scheduler, legacy);

        Require(!service.IsEnabled(), "A changed legacy command was claimed as this app's startup entry.");
        ExpectCollision(() => service.SetEnabled(true));

        Require(scheduler.RegisterCount == 0, "A changed legacy command was replaced.");
        Require(legacy.Values[LegacyValueName] == "C:\\DifferentProgram\\launcher.exe",
            "A changed legacy command was deleted.");
    }

    private static void AFailedLegacyMigrationRollsBackTheNewTask()
    {
        var scheduler = new FakeTaskScheduler();
        scheduler.Tasks["OtherVendor.UpdateCheck"] = new("OtherVendor/startup-v1", UserSid, true);
        var legacy = new FakeLegacyStartupStore { RejectDeletion = true };
        legacy.Values[LegacyValueName] = ExpectedCommand;
        var service = CreateService(scheduler, legacy);

        ExpectCollision(() => service.SetEnabled(true));

        Require(scheduler.Tasks[ExpectedTaskName] == new StartupTaskRegistration(ExpectedSource, UserSid, false),
            "A failed migration left a newly created task enabled.");
        Require(legacy.Values[LegacyValueName] == ExpectedCommand,
            "A failed migration changed the old app entry.");
        Require(scheduler.Tasks["OtherVendor.UpdateCheck"].Enabled,
            "A failed migration changed another software's scheduled task.");
    }

    private static void WindowsTaskSchedulerRoundTripsAnIsolatedOwnedTask()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var currentSid = identity.User?.Value
            ?? throw new InvalidOperationException("Could not determine the current test user SID.");
        var taskName = "LanDeviceMonitor.Tests.Logon." + Guid.NewGuid().ToString("N");
        const string source = "LanDeviceMonitor.Tests/logon-v1";
        var scheduler = new StartupRegistrationService.TaskSchedulerStartupStore();
        var legacy = new FakeLegacyStartupStore();
        var service = new StartupRegistrationService(scheduler, legacy, ExecutablePath, currentSid, taskName, source);

        try
        {
            service.SetEnabled(true);
            var enabledTask = scheduler.GetTask(taskName)
                ?? throw new InvalidOperationException("Windows Task Scheduler did not return the test task.");
            Require(enabledTask.Source == source && enabledTask.Enabled && service.IsEnabled(),
                "The isolated logon task was not registered as enabled and owned by this user.");
            VerifyRegisteredTaskDefinition(taskName, source, currentSid);

            service.SetEnabled(false);
            var disabledTask = scheduler.GetTask(taskName)
                ?? throw new InvalidOperationException("Disabling deleted the isolated test task.");
            Require(disabledTask.Source == source && !disabledTask.Enabled && !service.IsEnabled(),
                "The isolated task was not disabled while remaining owned.");
        }
        finally
        {
            DeleteTestTaskIfStillOwned(scheduler, taskName, source, currentSid);
        }

        Require(scheduler.GetTask(taskName) is null, "The isolated test task remained after cleanup.");
    }

    private static void VerifyRegisteredTaskDefinition(string taskName, string source, string userSid)
    {
        using var scope = new TestComObjectScope();
        var serviceType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Could not connect to Task Scheduler to inspect the test task.");
        dynamic taskService = scope.Track(Activator.CreateInstance(serviceType)
            ?? throw new InvalidOperationException("Could not create Task Scheduler service to inspect the test task."));
        taskService.Connect(Type.Missing, Type.Missing, Type.Missing, Type.Missing);
        dynamic folder = scope.Track(taskService.GetFolder("\\"));
        dynamic task = scope.Track(folder.GetTask(taskName));
        dynamic definition = scope.Track(task.Definition);
        dynamic registration = scope.Track(definition.RegistrationInfo);
        dynamic principal = scope.Track(definition.Principal);
        dynamic triggers = scope.Track(definition.Triggers);
        dynamic trigger = scope.Track(triggers.Item(1));
        dynamic settings = scope.Track(definition.Settings);
        dynamic actions = scope.Track(definition.Actions);
        dynamic action = scope.Track(actions.Item(1));

        var taskSource = Convert.ToString(registration.Source);
        var principalUser = Convert.ToString(principal.UserId);
        var principalLogonType = Convert.ToInt32(principal.LogonType);
        var principalRunLevel = Convert.ToInt32(principal.RunLevel);
        var triggerType = Convert.ToInt32(trigger.Type);
        var triggerUser = Convert.ToString(trigger.UserId);
        var triggerDelay = Convert.ToString(trigger.Delay);
        var multipleInstances = Convert.ToInt32(settings.MultipleInstances);
        var disallowOnBattery = Convert.ToBoolean(settings.DisallowStartIfOnBatteries);
        var stopOnBattery = Convert.ToBoolean(settings.StopIfGoingOnBatteries);
        var allowHardTerminate = Convert.ToBoolean(settings.AllowHardTerminate);
        var executionTimeLimit = Convert.ToString(settings.ExecutionTimeLimit);
        var startWhenAvailable = Convert.ToBoolean(settings.StartWhenAvailable);
        var actualCommand = Convert.ToString(action.Path);
        var actualArguments = Convert.ToString(action.Arguments);
        var actualWorkingDirectory = Convert.ToString(action.WorkingDirectory);

        Require(taskSource == source, "Task Scheduler did not preserve the product ownership marker.");
        Require(PrincipalMatches(principalUser, userSid) && principalLogonType == 3 && principalRunLevel == 0,
            "Task Scheduler did not preserve the current-user, interactive, least-privilege principal.");
        Require(triggerType == 9 && PrincipalMatches(triggerUser, userSid) && string.IsNullOrEmpty(triggerDelay),
            "Task Scheduler did not preserve the current-user, immediate logon trigger.");
        Require(multipleInstances == 2 && !disallowOnBattery && !stopOnBattery && !allowHardTerminate &&
                executionTimeLimit == "PT0S" && startWhenAvailable,
            "Task Scheduler did not preserve the requested startup task settings.");
        Require(actualCommand == Path.GetFullPath(ExecutablePath) && actualArguments == "--background" &&
                actualWorkingDirectory == Path.GetDirectoryName(Path.GetFullPath(ExecutablePath)),
            "Task Scheduler did not preserve the executable, argument, or working directory.");
    }

    private static void DeleteTestTaskIfStillOwned(
        StartupRegistrationService.TaskSchedulerStartupStore scheduler,
        string taskName,
        string source,
        string userSid)
    {
        var registration = scheduler.GetTask(taskName);
        if (registration is null || registration.Source != source || !PrincipalMatches(registration.UserId, userSid))
        {
            return;
        }

        var serviceType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Could not connect to Task Scheduler for test cleanup.");
        dynamic taskService = Activator.CreateInstance(serviceType)
            ?? throw new InvalidOperationException("Could not create Task Scheduler service for test cleanup.");
        var objects = new Stack<object>();
        object Track(object value)
        {
            objects.Push(value);
            return value;
        }

        try
        {
            taskService.Connect(Type.Missing, Type.Missing, Type.Missing, Type.Missing);
            dynamic folder = Track(taskService.GetFolder("\\"));
            dynamic task = Track(folder.GetTask(taskName));
            dynamic definition = Track(task.Definition);
            dynamic taskRegistration = Track(definition.RegistrationInfo);
            dynamic principal = Track(definition.Principal);
            var currentSource = Convert.ToString(taskRegistration.Source);
            var currentUser = Convert.ToString(principal.UserId);
            if (currentSource == source && PrincipalMatches(currentUser, userSid))
            {
                folder.DeleteTask(taskName, 0);
            }
        }
        finally
        {
            while (objects.TryPop(out var value))
            {
                if (Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
            }

            if (Marshal.IsComObject(taskService)) Marshal.FinalReleaseComObject(taskService);
        }
    }

    private static bool PrincipalMatches(string? configuredUser, string expectedSid)
    {
        if (string.Equals(configuredUser, expectedSid, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrWhiteSpace(configuredUser)) return false;
        try
        {
            return ((SecurityIdentifier)new NTAccount(configuredUser).Translate(typeof(SecurityIdentifier)))
                .Value.Equals(expectedSid, StringComparison.OrdinalIgnoreCase);
        }
        catch (IdentityNotMappedException)
        {
            return false;
        }
    }

    private sealed class TestComObjectScope : IDisposable
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
                if (Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
            }
        }
    }

    private static StartupRegistrationService CreateService(FakeTaskScheduler scheduler, FakeLegacyStartupStore legacy) =>
        new(scheduler, legacy, ExecutablePath, UserSid);

    private static void ExpectCollision(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("Expected an ownership collision to fail without changing startup state.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeTaskScheduler : IStartupTaskScheduler
    {
        public Dictionary<string, StartupTaskRegistration> Tasks { get; } = new(StringComparer.Ordinal);
        public int RegisterCount { get; private set; }
        public bool LastUpdate { get; private set; }
        public StartupTaskDefinition? LastDefinition { get; private set; }

        public StartupTaskRegistration? GetTask(string taskName) =>
            Tasks.TryGetValue(taskName, out var registration) ? registration : null;

        public void RegisterTask(StartupTaskDefinition definition, bool update)
        {
            RegisterCount++;
            LastUpdate = update;
            LastDefinition = definition;
            Tasks[definition.Name] = new(definition.Source, definition.UserSid, true);
        }

        public void SetTaskEnabled(string taskName, bool enabled)
        {
            var registration = Tasks[taskName];
            Tasks[taskName] = registration with { Enabled = enabled };
        }
    }

    private sealed class FakeLegacyStartupStore : ILegacyStartupEntryStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool RejectDeletion { get; init; }

        public string? ReadCommand(string valueName) =>
            Values.TryGetValue(valueName, out var command) ? command : null;

        public bool DeleteCommandIfMatches(string valueName, string expectedCommand)
        {
            if (RejectDeletion) return false;
            if (!Values.TryGetValue(valueName, out var command)) return true;
            if (!string.Equals(command, expectedCommand, StringComparison.OrdinalIgnoreCase)) return false;
            Values.Remove(valueName);
            return true;
        }
    }
}

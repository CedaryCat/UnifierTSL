namespace UnifierTSL.Surface;

internal static class SurfaceRuntimeOptions
{
    private static StatusProjectionSettings statusSettings = StatusProjectionSettings.Default;
    private static Func<bool>? rootActivityActiveProvider;
    private static int useColorfulStatus = 1;

    public static event Action? StatusAppearanceChanged;

    public static StatusProjectionSettings StatusSettings => Volatile.Read(ref statusSettings);

    public static bool UseColorfulStatus => Volatile.Read(ref useColorfulStatus) != 0;

    public static bool HasRootActivity {
        get {
            var provider = Volatile.Read(ref rootActivityActiveProvider);
            return provider?.Invoke() ?? false;
        }
    }

    public static bool ApplyStatusSettings(StatusProjectionSettings settings, bool colorfulStatus) {
        ArgumentNullException.ThrowIfNull(settings);

        var previousSettings = StatusSettings;
        var previousColorfulStatus = UseColorfulStatus;
        var changed = previousSettings != settings || previousColorfulStatus != colorfulStatus;

        Volatile.Write(ref statusSettings, settings);
        Volatile.Write(ref useColorfulStatus, colorfulStatus ? 1 : 0);
        return changed;
    }

    public static void SetRootActivityActiveProvider(Func<bool>? provider) {
        Volatile.Write(ref rootActivityActiveProvider, provider);
    }

    public static void NotifyStatusAppearanceChanged() {
        StatusAppearanceChanged?.Invoke();
    }
}

namespace Autocorrect.Core;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GlobalAutocorrect");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
}

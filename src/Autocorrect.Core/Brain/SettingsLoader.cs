using System.Text.Json;

namespace Autocorrect.Core.Brain;

public static class SettingsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static CorrectionSettings Load()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        if (!File.Exists(AppPaths.SettingsPath))
        {
            return new CorrectionSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<CorrectionSettings>(File.ReadAllText(AppPaths.SettingsPath), JsonOptions)
                   ?? new CorrectionSettings();
        }
        catch
        {
            return new CorrectionSettings();
        }
    }
}

using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Autocorrect.Core.Brain;

// Shared, stable paths/keys for all Brain data so every store agrees on where a project's data lives.
public static class BrainStorage
{
    public static string BrainDirectory(string baseDirectory)
    {
        var directory = Path.Combine(baseDirectory, "brain");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string ProjectKey(string projectRoot)
    {
        var normalized = Path.GetFullPath(projectRoot).ToLowerInvariant().TrimEnd('\\', '/');
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..16];
    }
}

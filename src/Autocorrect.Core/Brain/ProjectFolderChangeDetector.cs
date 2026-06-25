namespace Autocorrect.Core.Brain;

public sealed class ProjectFolderChanges
{
    public List<string> Added { get; } = new();
    public List<string> Modified { get; } = new();
    public List<string> Removed { get; } = new();

    public bool HasChanges => Added.Count > 0 || Modified.Count > 0 || Removed.Count > 0;
    public int TotalChanges => Added.Count + Modified.Count + Removed.Count;
}

public static class ProjectFolderChangeDetector
{
    public static ProjectFolderChanges Detect(string projectRoot, IndexOptions options, ProjectIndexMetadata? previous)
    {
        var changes = new ProjectFolderChanges();
        var previousHashes = previous?.FileHashes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scanner = new ProjectScanner();
        var scanned = scanner.Scan(projectRoot, options, out _);
        var current = scanned
            .ToDictionary(
                file => file.RelativePath.Replace('\\', '/'),
                file => file.Hash,
                StringComparer.OrdinalIgnoreCase);

        foreach (var (path, hash) in current)
        {
            if (!previousHashes.TryGetValue(path, out var oldHash))
            {
                changes.Added.Add(path);
            }
            else if (!oldHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
            {
                changes.Modified.Add(path);
            }
        }

        foreach (var path in previousHashes.Keys)
        {
            if (!current.ContainsKey(path))
            {
                changes.Removed.Add(path);
            }
        }

        return changes;
    }
}

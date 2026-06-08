namespace Autocorrect.Core.Brain;

// Classifies a project file into a coarse role using its path, extension, and a content peek.
public static class FileRoleDetector
{
    public static FileRole Detect(string relativePath, string extension, string content)
    {
        var path = relativePath.Replace('\\', '/').ToLowerInvariant();
        var name = System.IO.Path.GetFileName(path);

        if (IsTest(name, path))
        {
            return FileRole.Test;
        }

        if (IsConfig(name))
        {
            return FileRole.Config;
        }

        if (extension is ".css" or ".scss" or ".sass" or ".less" || name == "tailwind.config.js" || name == "tailwind.config.ts")
        {
            return FileRole.Style;
        }

        if (extension is ".md" or ".mdx" or ".txt" || path.Contains("/docs/"))
        {
            return FileRole.Docs;
        }

        if (path.Contains("/prisma/") || path.Contains("/db/") || path.Contains("/database/") || path.Contains("/models/") || extension is ".sql" or ".prisma")
        {
            return FileRole.Database;
        }

        if (path.Contains("/api/") || path.Contains("/routes/") || name.StartsWith("route.", StringComparison.Ordinal) || content.Contains("export async function GET", StringComparison.Ordinal) || content.Contains("createServer", StringComparison.Ordinal))
        {
            return FileRole.Api;
        }

        if (path.Contains("/pages/") || path.Contains("/app/") && (name.StartsWith("page.", StringComparison.Ordinal) || name.StartsWith("layout.", StringComparison.Ordinal)))
        {
            return FileRole.Route;
        }

        if (path.Contains("/hooks/") || name.StartsWith("use", StringComparison.Ordinal) && extension is ".ts" or ".tsx" or ".js" or ".jsx")
        {
            return FileRole.Hook;
        }

        if (path.Contains("/components/") || extension is ".tsx" or ".jsx" or ".vue" or ".svelte")
        {
            return FileRole.Component;
        }

        if (path.Contains("/utils/") || path.Contains("/lib/") || path.Contains("/helpers/"))
        {
            return FileRole.Util;
        }

        return FileRole.Unknown;
    }

    public static NodeType ToNodeType(FileRole role)
    {
        return role switch
        {
            FileRole.Route => NodeType.Route,
            FileRole.Component => NodeType.Component,
            FileRole.Hook => NodeType.Hook,
            FileRole.Api => NodeType.Api,
            FileRole.Style => NodeType.Style,
            FileRole.Config => NodeType.Config,
            _ => NodeType.File
        };
    }

    private static bool IsTest(string name, string path)
    {
        return name.Contains(".test.", StringComparison.Ordinal) ||
               name.Contains(".spec.", StringComparison.Ordinal) ||
               path.Contains("/__tests__/") ||
               path.Contains("/tests/") ||
               path.Contains("/test/");
    }

    private static bool IsConfig(string name)
    {
        if (name is "package.json" or "tsconfig.json" or "components.json" or ".cursorrules" or "agents.md")
        {
            return true;
        }

        return name.Contains(".config.", StringComparison.Ordinal) ||
               name.StartsWith("vite.config", StringComparison.Ordinal) ||
               name.StartsWith("next.config", StringComparison.Ordinal) ||
               name.StartsWith("tailwind.config", StringComparison.Ordinal) ||
               name.StartsWith("webpack.config", StringComparison.Ordinal);
    }
}

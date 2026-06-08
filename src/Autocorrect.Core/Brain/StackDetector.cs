using System.IO;
using System.Text.Json;

namespace Autocorrect.Core.Brain;

// Infers framework, language, styling, UI library, and package manager from manifest and lock files.
public static class StackDetector
{
    public static ProjectStack Detect(string projectRoot)
    {
        var stack = new ProjectStack();
        var dependencies = ReadDependencies(projectRoot);

        stack.PackageManager = DetectPackageManager(projectRoot, dependencies.Count > 0);
        stack.Language = DetectLanguage(projectRoot, dependencies);
        stack.Framework = DetectFramework(projectRoot, dependencies);
        stack.Styling = DetectStyling(projectRoot, dependencies);
        stack.UiLibrary = DetectUiLibrary(dependencies);
        return stack;
    }

    private static Dictionary<string, string> ReadDependencies(string projectRoot)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var packageJson = Path.Combine(projectRoot, "package.json");
        if (!File.Exists(packageJson))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
            foreach (var section in new[] { "dependencies", "devDependencies" })
            {
                if (document.RootElement.TryGetProperty(section, out var deps))
                {
                    foreach (var dep in deps.EnumerateObject())
                    {
                        result[dep.Name] = dep.Value.GetString() ?? string.Empty;
                    }
                }
            }
        }
        catch
        {
            // A malformed manifest should never stop indexing.
        }

        return result;
    }

    private static string? DetectPackageManager(string projectRoot, bool isNode)
    {
        if (File.Exists(Path.Combine(projectRoot, "pnpm-lock.yaml"))) return "pnpm";
        if (File.Exists(Path.Combine(projectRoot, "yarn.lock"))) return "yarn";
        if (File.Exists(Path.Combine(projectRoot, "bun.lockb"))) return "bun";
        if (File.Exists(Path.Combine(projectRoot, "package-lock.json"))) return "npm";
        if (Directory.GetFiles(projectRoot, "*.csproj").Length > 0 || Directory.GetFiles(projectRoot, "*.sln").Length > 0) return "dotnet";
        if (File.Exists(Path.Combine(projectRoot, "requirements.txt")) || File.Exists(Path.Combine(projectRoot, "pyproject.toml"))) return "pip";
        return isNode ? "npm" : null;
    }

    private static string? DetectLanguage(string projectRoot, Dictionary<string, string> deps)
    {
        if (File.Exists(Path.Combine(projectRoot, "tsconfig.json")) || deps.ContainsKey("typescript")) return "TypeScript";
        if (Directory.GetFiles(projectRoot, "*.csproj").Length > 0) return "C#";
        if (File.Exists(Path.Combine(projectRoot, "pyproject.toml")) || File.Exists(Path.Combine(projectRoot, "requirements.txt"))) return "Python";
        if (deps.Count > 0) return "JavaScript";
        return null;
    }

    private static string? DetectFramework(string projectRoot, Dictionary<string, string> deps)
    {
        if (deps.ContainsKey("next")) return "Next.js";
        if (deps.ContainsKey("@remix-run/react")) return "Remix";
        if (deps.ContainsKey("nuxt")) return "Nuxt";
        if (deps.ContainsKey("@angular/core")) return "Angular";
        if (deps.ContainsKey("svelte")) return "Svelte";
        if (deps.ContainsKey("vue")) return "Vue";
        if (deps.ContainsKey("react")) return "React";
        if (deps.ContainsKey("express") || deps.ContainsKey("fastify")) return "Node API";
        if (Directory.GetFiles(projectRoot, "*.csproj").Length > 0) return ".NET";
        return null;
    }

    private static string? DetectStyling(string projectRoot, Dictionary<string, string> deps)
    {
        if (deps.ContainsKey("tailwindcss") ||
            File.Exists(Path.Combine(projectRoot, "tailwind.config.js")) ||
            File.Exists(Path.Combine(projectRoot, "tailwind.config.ts")))
        {
            return "Tailwind CSS";
        }

        if (deps.ContainsKey("styled-components")) return "styled-components";
        if (deps.ContainsKey("@emotion/react")) return "Emotion";
        if (deps.ContainsKey("sass")) return "Sass";
        return null;
    }

    private static string? DetectUiLibrary(Dictionary<string, string> deps)
    {
        if (deps.Keys.Any(k => k.StartsWith("@radix-ui/", StringComparison.OrdinalIgnoreCase))) return "shadcn/Radix UI";
        if (deps.ContainsKey("@mui/material")) return "MUI";
        if (deps.ContainsKey("@chakra-ui/react")) return "Chakra UI";
        if (deps.ContainsKey("antd")) return "Ant Design";
        if (deps.ContainsKey("@mantine/core")) return "Mantine";
        return null;
    }

    public static IReadOnlyList<string> KnownAnimationLibraries(string projectRoot)
    {
        var packageJson = Path.Combine(projectRoot, "package.json");
        if (!File.Exists(packageJson))
        {
            return Array.Empty<string>();
        }

        var text = File.ReadAllText(packageJson);
        var found = new List<string>();
        foreach (var (dep, label) in new[]
                 {
                     ("gsap", "GSAP"),
                     ("lenis", "Lenis"),
                     ("@studio-freight/lenis", "Lenis"),
                     ("framer-motion", "Framer Motion"),
                     ("animejs", "anime.js")
                 })
        {
            if (text.Contains($"\"{dep}\"", StringComparison.OrdinalIgnoreCase) && !found.Contains(label))
            {
                found.Add(label);
            }
        }

        return found;
    }
}

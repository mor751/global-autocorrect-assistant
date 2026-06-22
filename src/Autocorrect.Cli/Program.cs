using Autocorrect.Cli.Commands;
using Autocorrect.Core;
using Autocorrect.Core.Brain;

namespace Autocorrect.Cli;

internal static class Program
{
    private static readonly ICliCommand[] Commands =
    [
        new IndexCommand(),
        new StatusCommand(),
        new SearchCommand(),
        new PromptCommand(),
        new EnhanceCommand(),
        new DoctorCommand(),
        new VectorsCommand(),
        new BrainCommand()
    ];

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            var settings = SettingsLoader.Load();
            var activeRoot = ActiveProjectStore.Get(settings);
            var projectRoot = CliArgs.Flag(args, "--path") ?? activeRoot ?? settings.ProjectRoot;
            using var brain = new ProjectBrainService(AppPaths.DataDirectory, BrainOptionsFactory.FromSettings(settings));
            var context = new CliContext(settings, brain, string.IsNullOrWhiteSpace(projectRoot) ? null : Path.GetFullPath(projectRoot));

            var command = Commands.FirstOrDefault(item => item.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase));
            if (command is null)
            {
                Console.Error.WriteLine($"Unknown command: {args[0]}");
                PrintHelp();
                return 1;
            }

            return await command.RunAsync(args[1..], context);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Woody CLI — project-aware brain for coding prompts");
        Console.WriteLine();
        Console.WriteLine("Usage: woody <command> [options]");
        Console.WriteLine();
        foreach (var command in Commands)
        {
            Console.WriteLine($"  {command.Name,-10} {command.Summary}");
        }

        Console.WriteLine();
        Console.WriteLine("Global options:");
        Console.WriteLine("  --path <folder>   Project root (or active project from Woody dashboard)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  woody doctor");
        Console.WriteLine("  woody index \"C:\\repo\"");
        Console.WriteLine("  woody search \"vector db unavailable\"");
        Console.WriteLine("  woody prompt \"fix dashboard refresh\"");
        Console.WriteLine("  woody enhance \"fix dashboard refresh\"   # alias");
        Console.WriteLine("  woody vectors count");
        Console.WriteLine("  woody brain open");
    }
}

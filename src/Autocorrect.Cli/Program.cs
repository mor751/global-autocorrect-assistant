using Autocorrect.Cli.Commands;
using Autocorrect.Core;
using Autocorrect.Core.Brain;

namespace Autocorrect.Cli;

internal static class Program
{
    private static readonly Dictionary<string, ICliCommand> Commands = BuildCommands();

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            WoodyConsole.WriteHelp();
            return 0;
        }

        try
        {
            var settings = SettingsLoader.Load();
            var activeRoot = ActiveProjectStore.Get(settings);
            var projectRoot = CliArgs.Flag(args, "--path") ?? activeRoot ?? settings.ProjectRoot;
            using var brain = new ProjectBrainService(AppPaths.DataDirectory, BrainOptionsFactory.FromSettings(settings));
            var context = new CliContext(settings, brain, string.IsNullOrWhiteSpace(projectRoot) ? null : Path.GetFullPath(projectRoot));

            var commandName = ResolveCommandName(args[0]);
            if (!Commands.TryGetValue(commandName, out var command))
            {
                WoodyConsole.WriteError($"Unknown command: {args[0]}");
                Console.WriteLine();
                WoodyConsole.WriteHelp();
                return 1;
            }

            return await command.RunAsync(args[1..], context);
        }
        catch (Exception ex)
        {
            WoodyConsole.WriteError(ex.Message);
            return 1;
        }
    }

    private static Dictionary<string, ICliCommand> BuildCommands()
    {
        ICliCommand[] commands =
        [
            new IndexCommand(),
            new BrainCommand(),
            new PromptCommand(),
            new SearchCommand(),
            new ReloadCommand(),
            new DoctorCommand(),
            new EnhanceCommand(),
            new StatusCommand(),
            new VectorsCommand()
        ];

        var map = new Dictionary<string, ICliCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            map[command.Name] = command;
        }

        map["sync"] = map["reload"];
        return map;
    }

    private static string ResolveCommandName(string raw) =>
        raw.Equals("sync", StringComparison.OrdinalIgnoreCase) ? "reload"
        : raw.Equals("enhance", StringComparison.OrdinalIgnoreCase) ? "prompt"
        : raw;
}

namespace Autocorrect.Cli.Commands;

internal sealed class PromptCommand : ICliCommand
{
    public string Name => "prompt";
    public string Summary => "Optimize a prompt with project context";

    public async Task<int> RunAsync(string[] args, CliContext context)
    {
        var projectRoot = CliArgs.RequireProject(args, context);
        var prompt = CliArgs.Flag(args, "--prompt") ?? CliArgs.RequireText(args, "--prompt");
        var outcome = await context.Brain.EnhanceAsync(prompt, projectRoot, CancellationToken.None);
        Console.WriteLine($"Status: {outcome.Status}");
        Console.WriteLine($"Ollama: {(outcome.OllamaAvailable ? "online" : "offline")}");
        Console.WriteLine();
        Console.WriteLine(outcome.Result.ImprovedPrompt);
        if (outcome.UsedFiles.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Relevant files:");
            foreach (var file in outcome.UsedFiles.Take(12))
            {
                Console.WriteLine($"  - {file}");
            }
        }

        return 0;
    }
}


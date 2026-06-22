using Autocorrect.Core;
using Autocorrect.Core.Brain;

namespace Autocorrect.Cli;

internal sealed class CliContext
{
    public CliContext(CorrectionSettings settings, ProjectBrainService brain, string? projectRoot)
    {
        Settings = settings;
        Brain = brain;
        ProjectRoot = projectRoot;
    }

    public CorrectionSettings Settings { get; }
    public ProjectBrainService Brain { get; }
    public string? ProjectRoot { get; }
}

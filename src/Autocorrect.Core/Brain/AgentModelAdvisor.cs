namespace Autocorrect.Core.Brain;

public static class AgentModelAdvisor
{
    public static string Recommend(PromptTargetAgent agent) =>
        agent switch
        {
            PromptTargetAgent.Codex => "OpenAI Codex / GPT-4.1 / o3",
            PromptTargetAgent.Cursor => "Cursor Auto or Claude Sonnet (Agent)",
            PromptTargetAgent.ClaudeCode => "Claude Opus 4 / Sonnet 4",
            _ => "Your agent's strongest coding model"
        };

    public static string AgentLabel(PromptTargetAgent agent) =>
        agent switch
        {
            PromptTargetAgent.Codex => "Codex",
            PromptTargetAgent.Cursor => "Cursor",
            PromptTargetAgent.ClaudeCode => "Claude Code",
            _ => "Generic"
        };

    public static int EstimateDownstreamTokenSavings(string original, string improved, int relevantFileCount)
    {
        var originalTokens = EstimateTokens(original);
        var improvedTokens = EstimateTokens(improved);
        var structureBonus = relevantFileCount * 180;
        var clarityBonus = improved.Length > original.Length * 1.1 ? 220 : 120;
        var vagueOriginalBonus = originalTokens < 25 ? 650 : 320;
        return Math.Max(150, structureBonus + clarityBonus + vagueOriginalBonus - Math.Max(0, improvedTokens - originalTokens));
    }

    private static int EstimateTokens(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : Math.Max(1, (int)Math.Round(text.Length / 4.0));
}

namespace Autocorrect.Core;

public sealed class CorrectionSettings
{
    public bool Enabled { get; set; } = true;

    public bool AutocorrectEnabled { get; set; } = true;

    public bool AiOverlayEnabled { get; set; } = false;

    public double ConfidenceThreshold { get; set; } = 0.92;

    public double SuggestionThreshold { get; set; } = 0.72;

    public int MaxCorrectionLatencyMs { get; set; } = 230;

    public bool EnableOnnxFallback { get; set; } = false;

    public string? OnnxModelPath { get; set; }

    public bool UseClipboardFallback { get; set; } = false;

    public bool ShowSuggestionPopup { get; set; } = true;

    public bool ShowFloatingPill { get; set; } = false;

    public bool AiPetEnabled { get; set; } = true;

    public double? AiPetLeft { get; set; }

    public double? AiPetTop { get; set; }

    public string AiPetName { get; set; } = "Pet";

    public string? AiPetImagePath { get; set; }

    public List<string> AiPetFrames { get; set; } = new();

    public int AiPetFrameIntervalMs { get; set; } = 110;

    public int FloatingPillDelayMs { get; set; } = 800;

    public int MinWordsForOverlay { get; set; } = 8;

    public bool DeveloperModeEnabled { get; set; } = true;

    public bool LocalOnlyMode { get; set; } = true;

    public string AiProvider { get; set; } = "ollama";

    public string AiEndpoint { get; set; } = "http://localhost:11434";

    public string AiModel { get; set; } = "qwen2.5:3b";

    public string? AiApiKeyStorageReference { get; set; }

    public bool StartupEnabled { get; set; } = false;

    public bool ProjectBrainEnabled { get; set; } = true;

    public string? ProjectRoot { get; set; }

    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    public int MaxIndexedFileSizeKb { get; set; } = 200;

    public int MaxIndexedFiles { get; set; } = 1500;

    public HashSet<string> IgnoredProjectFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "dist", "build", ".next", "out", "coverage",
        ".turbo", ".cache", ".vercel", "bin", "obj", ".vs", ".idea", "vendor", "__pycache__"
    };

    public int CorrectionHistoryLimit { get; set; } = 250;

    public bool EnableUserLearning { get; set; } = true;

    public int LearnWordAfterCount { get; set; } = 3;

    public HashSet<string> BlockedProcesses { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd",
        "powershell",
        "pwsh",
        "WindowsTerminal",
        "mstsc",
        "vmconnect",
        "VirtualBoxVM"
    };

    public HashSet<string> EnabledProcesses { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> IgnoredWords { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ProtectedVocabulary { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Kling",
        "Cursor",
        "Base44",
        "GSAP",
        "Lenis",
        "AppSheet",
        "Power BI",
        "Learning Mode",
        "מצב למידה"
    };

    public Dictionary<string, int> LearnedWordFrequencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> LearnedCorrections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> RejectedCorrections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> LearnedBigrams { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> CustomCorrections { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["teh"] = "the",
        ["thw"] = "the",
        ["ti"] = "it",
        ["ot"] = "to",
        ["fo"] = "of",
        ["adn"] = "and",
        ["wrod"] = "word",
        ["wrods"] = "words",
        ["wrtiee"] = "write",
        ["writtee"] = "write",
        ["writie"] = "write",
        ["writng"] = "writing",
        ["wrting"] = "writing",
        ["exmple"] = "example",
        ["copmle"] = "complete",
        ["nnotworkigng"] = "not working",
        ["ntio"] = "not",
        ["fnot"] = "not",
        ["ood"] = "good",
        ["coumputer"] = "computer",
        ["compuyter"] = "computer",
        ["compomment"] = "component",
        ["componment"] = "component",
        ["grammyl"] = "Grammarly",
        ["keybroad"] = "keyboard",
        ["keybaord"] = "keyboard",
        ["insted"] = "instead",
        ["automatcly"] = "automatically",
        ["automaticaly"] = "automatically",
        ["backgounrs"] = "background",
        ["buidl"] = "build",
        ["buildd"] = "build",
        ["liek"] = "like",
        ["emant"] = "meant",
        ["stnad"] = "stand",
        ["remveri"] = "remember",
        ["wroking"] = "working",
        ["dotn"] = "don't",
        ["dont"] = "don't",
        ["wnat"] = "want",
        ["nwo"] = "now",
        ["jsut"] = "just",
        ["confgiartiuon"] = "configuration",
        ["sue"] = "use",
        ["ollam"] = "ollama",
        ["samll"] = "small",
        ["pythoin"] = "python",
        ["ptyonh"] = "python",
        ["tensorfloe"] = "tensorflow",
        ["librires"] = "libraries",
        ["soemthgin"] = "something",
        ["msooth"] = "smooth",
        ["msot"] = "most",
        ["imporotnat"] = "important",
        ["seocnds"] = "seconds",
        ["mdole"] = "model",
        ["collaspe"] = "collapse",
        ["stkac"] = "stuck",
        ["fell"] = "feel",
        ["nto"] = "not",
        ["maek"] = "make",
        ["amek"] = "make",
        ["hwo"] = "how",
        ["wht"] = "what",
        ["nmsiektae"] = "mistake",
        ["wroids"] = "words",
        ["setgigna"] = "settings",
        ["coplme"] = "complete",
        ["wiodns"] = "windows",
        ["promto"] = "prompt",
        ["promot"] = "prompt",
        ["protm"] = "prompt",
        ["ptomot"] = "prompt",
        ["iamge"] = "image",
        ["viede"] = "video",
        ["vidoe"] = "video",
        ["chercter"] = "character",
        ["chercet"] = "character",
        ["perrefct"] = "perfect",
        ["seciton"] = "section",
        ["compeotn"] = "component",
        ["comepton"] = "component",
        ["scorll"] = "scroll",
        ["stlye"] = "style",
        ["evye"] = "every",
        ["suer"] = "user",
        ["chhsoe"] = "choose",
        ["reqeite"] = "rewrite",
        ["tokesn"] = "tokens",
        ["perosnl"] = "personal",
        ["leant"] = "learned",
        ["thsi"] = "this",
        ["thast"] = "that",
        ["icnldue"] = "include"
    };
}

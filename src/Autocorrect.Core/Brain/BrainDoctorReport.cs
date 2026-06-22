namespace Autocorrect.Core.Brain;

public sealed class BrainDoctorReport
{
    public string ProjectRoot { get; set; } = string.Empty;
    public bool ProjectIndexed { get; set; }
    public ProjectBrainStatus Status { get; set; } = ProjectBrainStatus.NoFolder;
    public string RagMode { get; set; } = "No brain";
    public bool OllamaAvailable { get; set; }
    public string OllamaModel { get; set; } = string.Empty;
    public bool EmbedderReady { get; set; }
    public int EmbedderDimension { get; set; }
    public bool EmbedderDownloaded { get; set; }
    public string? EmbedderError { get; set; }
    public bool VectorStoreReady { get; set; }
    public long VectorCount { get; set; }
    public string Collection { get; set; } = string.Empty;
    public int SymbolNodes { get; set; }
    public int SymbolEdges { get; set; }
    public int IndexedFiles { get; set; }
    public int TotalChunks { get; set; }
    public int EmbeddedChunks { get; set; }
    public int SkippedFiles { get; set; }
    public string? LastError { get; set; }
}

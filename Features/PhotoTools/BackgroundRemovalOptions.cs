namespace ProjectManagement.Features.PhotoTools;

/// <summary>
/// Settings for Remove background in Photo Tools. The photo is sent to the PRISM server, a
/// segmentation model finds the subject, and only the mask goes back; nothing is stored or logged.
/// Off by default: an administrator installs an approved model, sets its checksum and turns it on.
/// </summary>
public sealed class BackgroundRemovalOptions
{
    public const string SectionName = "PhotoTools:BackgroundRemoval";

    /// <summary>Whether the tool may run. While false the page says it is not set up.</summary>
    public bool Enabled { get; set; }

    /// <summary>Folder holding the model file; relative paths are under the content root.</summary>
    public string ModelRoot { get; set; } = string.Empty;

    public BackgroundModelOptions Model { get; set; } = new();

    /// <summary>
    /// Threads one inference may use. Kept well under the server's cores so the rest of PRISM
    /// stays responsive while a photo is processed.
    /// </summary>
    public int IntraOpThreads { get; set; } = 6;

    /// <summary>Photos processed at the same time.</summary>
    public int MaximumConcurrent { get; set; } = 1;

    /// <summary>Photos that may wait for a turn; beyond this the server answers "busy".</summary>
    public int MaximumWaiting { get; set; } = 4;

    /// <summary>Largest request body accepted, in bytes.</summary>
    public long MaximumUploadBytes { get; set; } = 20L * 1024 * 1024;

    /// <summary>
    /// Longest side of the copy the browser sends. The model works at about 1024 px, so a larger
    /// copy adds transfer and decoding without a better mask; the browser enlarges the mask itself.
    /// </summary>
    public int MaximumSide { get; set; } = 2048;

    /// <summary>How long one photo may take, waiting included, before the server gives up.</summary>
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// The approved model and how to feed it. The defaults suit BRIA RMBG-2.0; see
/// docs/photo-tools-background-removal.md for the values of other models.
/// </summary>
public sealed class BackgroundModelOptions
{
    public string Key { get; set; } = "bria-rmbg";
    public string Version { get; set; } = "2.0";
    public string FileName { get; set; } = "model.onnx";

    /// <summary>Lower-case hex SHA-256 of the file. Required: the model is not used without it.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public string License { get; set; } = "CC-BY-NC-4.0";
    public string Publisher { get; set; } = "BRIA AI";

    /// <summary>Credit shown with the tool, as the model's licence asks.</summary>
    public string Attribution { get; set; } = "Background removal by BRIA RMBG";

    /// <summary>Input tensor name; empty takes the model's only input.</summary>
    public string InputName { get; set; } = string.Empty;

    /// <summary>Output tensor name; empty takes the model's first output.</summary>
    public string OutputName { get; set; } = string.Empty;

    public int InputWidth { get; set; } = 1024;
    public int InputHeight { get; set; } = 1024;

    /// <summary>Per channel (R, G, B): value = (pixel / 255 - Mean) / Std.</summary>
    public float[] Mean { get; set; } = { 0.485f, 0.456f, 0.406f };
    public float[] Std { get; set; } = { 0.229f, 0.224f, 0.225f };

    /// <summary>
    /// "Auto" (the sigmoid is applied when the output holds values outside 0 to 1), "None" when the
    /// output is already 0 to 1, or "Sigmoid".
    /// </summary>
    public string OutputActivation { get; set; } = "Auto";

    /// <summary>"None", or "MinMax" to stretch the output to the full 0 to 1 range.</summary>
    public string OutputNormalization { get; set; } = "None";
}

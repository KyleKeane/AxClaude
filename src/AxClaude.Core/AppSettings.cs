using System.Text.Json;
using System.Text.Json.Serialization;
using AxClaude.Core.Transcript;

namespace AxClaude.Core;

/// <summary>Window placement saved between runs.</summary>
public sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>
/// Everything the app remembers between runs. Stored as JSON in <c>%APPDATA%\AxClaude\settings.json</c>
/// (see SPEC.md Appendix C). A missing or unreadable file falls back to the defaults.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public const int MaxRecentFolders = 10;

    public string? LastProjectFolder { get; set; }
    public List<string> RecentFolders { get; set; } = [];
    public string? ClaudePath { get; set; }
    public int PtyColumns { get; set; } = 240;
    public int PtyRows { get; set; } = 50;

    /// <summary>Null means the Windows system font (which follows the text size setting under Accessibility).</summary>
    public string? FontFamily { get; set; }

    /// <summary>Point size; 0 means the system font size.</summary>
    public float FontSize { get; set; }

    public bool FontBold { get; set; }
    public bool AnnounceBell { get; set; } = true;

    /// <summary>The chimes when a message is sent and when Claude replies, finishes and asks (FR-7.3). The key keeps its 1.0 name.</summary>
    public bool SoundOnBell { get; set; } = true;

    public bool FlashTaskbar { get; set; } = true;

    /// <summary>How much of a reply is spoken as it arrives (FR-7.4): none, the first line of each message, or all.</summary>
    public ReplySpeechMode ReplySpeech { get; set; }

    /// <summary>The 1.1 key, true meaning every line: read from an old file, turned into <see cref="ReplySpeech"/> and no longer written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SpeakReplies { get; set; }

    /// <summary>Speak the <c>tool:</c> row of each tool call as it arrives, whatever <see cref="ReplySpeech"/> says (FR-7.4).</summary>
    public bool SpeakToolCalls { get; set; }

    /// <summary>A tick whenever a line from Claude arrives (FR-7.8).</summary>
    public bool ClickOnNewLine { get; set; } = true;

    /// <summary>Time of sending in the Input and Output markers (FR-4.6).</summary>
    public bool MarkerTimeStamps { get; set; }

    /// <summary>Show reply rows that Claude wrapped at the console width as one line (FR-3.2a). File only, no menu item.</summary>
    public bool JoinWrappedLines { get; set; } = true;

    /// <summary>Ask GitHub once at startup whether a newer release exists (FR-1.10).</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Recognise what Claude waits on and open the answer notice or the screen notice for it, and keep messages out
    /// of a waiting question (D33, FR-2.10). Off: questions are only announced, as before 1.4.0.
    /// </summary>
    public bool QuestionNotices { get; set; } = true;

    public int MaxTranscriptLines { get; set; } = 20000;
    public WindowPlacement? Window { get; set; }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AxClaude", "settings.json");

    /// <summary>Loads the file, or returns defaults. <paramref name="error"/> describes an unreadable file.</summary>
    public static AppSettings Load(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            settings.Sanitize();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"{path}: {ex.Message}";
            return new AppSettings();
        }
    }

    /// <summary>Writes the file atomically: a temporary file next to it is moved into place.</summary>
    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    public void RememberFolder(string folder)
    {
        LastProjectFolder = folder;
        RecentFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, folder);
        if (RecentFolders.Count > MaxRecentFolders)
        {
            RecentFolders.RemoveRange(MaxRecentFolders, RecentFolders.Count - MaxRecentFolders);
        }
    }

    private void Sanitize()
    {
        RecentFolders ??= [];
        RecentFolders.RemoveAll(string.IsNullOrWhiteSpace);
        if (PtyColumns is < 20 or > 1000) PtyColumns = 240;
        if (PtyRows is < 5 or > 500) PtyRows = 50;
        if (FontSize is < 0 or > 96) FontSize = 0;
        if (MaxTranscriptLines < 1000) MaxTranscriptLines = 1000;
        if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = null;
        if (Window is { } w && (w.Width < 200 || w.Height < 150)) Window = null;
        if (SpeakReplies && ReplySpeech == ReplySpeechMode.None) ReplySpeech = ReplySpeechMode.All;
        SpeakReplies = false;
    }
}

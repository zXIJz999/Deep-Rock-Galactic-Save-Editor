using DrgSave.Core;

namespace DrgSave.Web;

/// <summary>
/// The open save and everything the pages share. Every write goes through Core (DrgSaveFile,
/// DrgUnlocks), the same code the desktop editor uses.
/// </summary>
public sealed class EditorState
{
    public const string AppTitle = "Deep Rock Galactic Save Editor";
    public const string Author = "zXIJz";
    public const string Credit = "Made by " + Author;

    public DrgSaveFile? Save { get; private set; }
    public byte[]? OriginalBytes { get; private set; }
    public string FileName { get; private set; } = "";
    public bool Dirty { get; private set; }
    public List<string> LogLines { get; } = new();
    public string Status { get; private set; } = "No save open";

    /// <summary>Bumped on every open/undo so pages rebuild from the new save.</summary>
    public int Generation { get; private set; }

    public event Action? Changed;

    public void Log(string message)
    {
        LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        Status = message;
        Changed?.Invoke();
    }

    public void MarkDirty()
    {
        if (Dirty) return;
        Dirty = true;
        Changed?.Invoke();
    }

    public void Open(byte[] data, string fileName)
    {
        var save = DrgSaveFile.Load(data, fileName);
        Save = save;
        OriginalBytes = data;
        FileName = fileName;
        Dirty = false;
        Generation++;
        Log($"Opened {fileName} ({data.Length:N0} bytes).");
        if (!save.RoundTrips()) Log("WARNING: this save does not write back byte-identical. Saving will be refused.");
    }

    public void Undo()
    {
        if (OriginalBytes != null) Open(OriginalBytes, FileName);
    }

    public byte[] Export()
    {
        var bytes = Save!.Export();
        Dirty = false;
        Log($"Saved {FileName} ({bytes.Length:N0} bytes). Your browser downloads it; your original file is not changed.");
        return bytes;
    }

    // ---- in-page confirmation (no browser alert/confirm boxes) ----

    public string? Question { get; private set; }
    private Action? _onYes;

    public void Ask(string question, Action onYes)
    {
        Question = question;
        _onYes = onYes;
        Changed?.Invoke();
    }

    public void Answer(bool yes)
    {
        var act = yes ? _onYes : null;
        Question = null;
        _onYes = null;
        act?.Invoke();
        Changed?.Invoke();
    }
}

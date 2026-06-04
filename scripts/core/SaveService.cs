using System.Collections.Generic;
using System.Text;
using Godot;

namespace NWO.Core;

// Godot-side file IO for named save slots under user://saves/*.json. Delegates all
// (de)serialization to SaveSerializer (which is headless/testable) and only adds
// the FileAccess/DirAccess layer this can't be unit-tested without the engine.
//
// Save fidelity note: combat is reproduced from the stored seed rather than the
// exact pre-save RNG stream position, so combat after a load is deterministic from
// the seed but not a byte-exact continuation. Acceptable for MVP.
public static class SaveService
{
    private const string SaveDir = "user://saves";

    public record SaveSlot(string File, SaveSerializer.SaveHeaderDto Header);

    public static void Save(GameState state, string slotName)
    {
        EnsureSaveDir();
        string json = SaveSerializer.Serialize(state, slotName);
        string path = $"{SaveDir}/{Slug(slotName)}.json";
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushError($"Cannot write save {path}: {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(json);
    }

    // All saves on disk, newest first, with their headers for the slot list.
    public static List<SaveSlot> ListSaves()
    {
        var slots = new List<SaveSlot>();
        if (!DirAccess.DirExistsAbsolute(SaveDir)) return slots;

        foreach (var name in DirAccess.GetFilesAt(SaveDir))
        {
            if (!name.EndsWith(".json")) continue;
            string path = $"{SaveDir}/{name}";
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file == null) continue;
            var header = SaveSerializer.ReadHeader(file.GetAsText());
            if (header != null) slots.Add(new SaveSlot(name, header));
        }

        slots.Sort((a, b) => string.CompareOrdinal(b.Header.Timestamp, a.Header.Timestamp));
        return slots;
    }

    public static GameState Load(string fileName, DataCatalog catalog)
    {
        string path = $"{SaveDir}/{fileName}";
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
            throw new System.IO.FileNotFoundException($"Cannot open {path}: {FileAccess.GetOpenError()}");
        return SaveSerializer.Deserialize(file.GetAsText(), catalog);
    }

    public static bool SlotExists(string slotName)
        => FileAccess.FileExists($"{SaveDir}/{Slug(slotName)}.json");

    public static void Delete(string fileName)
    {
        string path = $"{SaveDir}/{fileName}";
        if (FileAccess.FileExists(path))
            DirAccess.RemoveAbsolute(path);
    }

    private static void EnsureSaveDir()
    {
        if (!DirAccess.DirExistsAbsolute(SaveDir))
            DirAccess.MakeDirRecursiveAbsolute(SaveDir);
    }

    // Maps a display name to a safe file stem (the original name is preserved in
    // the file's header for display). Keeps letters/digits/-/_; collapses the rest,
    // then appends a short stable hash of the normalized name so names that differ
    // only in collapsed punctuation ("My Save!" vs "My Save?") don't slug to the
    // same file and silently overwrite one another. The same name always maps to the
    // same slot, so re-saving still overwrites as intended.
    private static string Slug(string name)
    {
        string normalized = name.Trim().ToLowerInvariant();
        var sb = new StringBuilder(normalized.Length);
        foreach (char c in normalized)
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        string stem = sb.ToString().Trim('_');
        if (stem.Length == 0) stem = "save";
        return $"{stem}_{StableHash(normalized)}";
    }

    // FNV-1a 32-bit → 6 hex chars. Deterministic across runs and machines, unlike
    // string.GetHashCode (which is randomized per process on .NET Core).
    private static string StableHash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return (h & 0xFFFFFF).ToString("x6");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>PrepareRestore must validate without mutation; its returned action replaces all owned state.</summary>
public interface ISaveSection
{
    string Key { get; }
    int Version { get; }
    string Capture();
    Action PrepareRestore(int version, string json);
}

[Serializable] public sealed class SaveSectionData
{
    public string key;
    public int version;
    public string json;
}

[Serializable] public sealed class SaveDocument
{
    public int version = 1;
    public string savedAtUtc;
    public List<SaveSectionData> sections = new();
}

/// <summary>Local, synchronous two-slot storage. Call on Unity's main thread at an explicit save point.</summary>
public sealed class SaveSlotService
{
    [Serializable] private sealed class FileEnvelope
    {
        public string payload;
        public string sha256;
    }

    public const int SlotCount = 2;
    private readonly string directory;
    private readonly List<ISaveSection> sections = new();
    private SaveDocument loaded;
    public SaveSlotService(string directory) => this.directory = directory ?? throw new ArgumentNullException(nameof(directory));

    public void Register(ISaveSection section)
    {
        if (section == null || string.IsNullOrWhiteSpace(section.Key)) throw new ArgumentException("A section needs a stable key.");
        if (sections.Exists(value => value.Key == section.Key)) throw new ArgumentException("Duplicate save section: " + section.Key);
        sections.Add(section);
    }

    public string GetSlotPath(int slot)
    {
        if (slot < 1 || slot > SlotCount) throw new ArgumentOutOfRangeException(nameof(slot), "Slots are 1 and 2.");
        return Path.Combine(directory, $"slot-{slot}.json");
    }

    public bool Exists(int slot) => File.Exists(GetSlotPath(slot));

    public void Save(int slot)
    {
        string path = GetSlotPath(slot);
        var document = new SaveDocument { savedAtUtc = DateTime.UtcNow.ToString("O") };
        // Keep sections belonging to currently uninstalled/disabled features after a load/save cycle.
        if (loaded != null)
            foreach (var entry in loaded.sections)
                if (!sections.Exists(section => section.Key == entry.key)) document.sections.Add(entry);
        foreach (var section in sections)
        {
            string snapshot = section.Capture();
            section.PrepareRestore(section.Version, snapshot);
            document.sections.Add(new SaveSectionData { key = section.Key, version = section.Version, json = snapshot });
        }
        string payload = JsonUtility.ToJson(document);
        string json = JsonUtility.ToJson(new FileEnvelope { payload = payload, sha256 = Hash(payload) }, true);
        Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        // Flush before replacing the previous file. An interrupted write leaves the original intact.
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }

    public SaveDocument Read(int slot, bool backup = false)
    {
        var envelope = JsonUtility.FromJson<FileEnvelope>(File.ReadAllText(GetSlotPath(slot) + (backup ? ".bak" : "")));
        if (envelope == null || envelope.payload == null || envelope.sha256 != Hash(envelope.payload))
            throw new InvalidDataException("Save file checksum failed. Try the previous backup.");
        var document = JsonUtility.FromJson<SaveDocument>(envelope.payload);
        if (document == null || document.version != 1 || document.sections == null || string.IsNullOrEmpty(document.savedAtUtc))
            throw new InvalidDataException("Unsupported or incomplete save document.");
        var keys = new HashSet<string>();
        foreach (var entry in document.sections)
            if (entry == null || string.IsNullOrWhiteSpace(entry.key) || !keys.Add(entry.key) || entry.version < 1 || entry.json == null)
                throw new InvalidDataException("Invalid save section.");
        return document;
    }

    public void Load(int slot, bool backup = false)
    {
        var document = Read(slot, backup);
        var apply = new List<Action>();
        var rollback = new List<Action>();
        foreach (var section in sections)
        {
            var entry = document.sections.Find(value => value.key == section.Key);
            if (entry == null) throw new InvalidDataException("Missing required section: " + section.Key);
            apply.Add(section.PrepareRestore(entry.version, entry.json));
            rollback.Add(section.PrepareRestore(section.Version, section.Capture()));
        }
        int index = 0;
        try { for (; index < apply.Count; index++) apply[index](); }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            for (int undo = Math.Min(index, rollback.Count - 1); undo >= 0; undo--)
                try { rollback[undo](); } catch (Exception error) { failures.Add(error); }
            throw new AggregateException("Load failed; previous state restoration attempted.", failures);
        }
        loaded = document;
    }

    private static string Hash(string value)
    {
        using var algorithm = SHA256.Create();
        return Convert.ToBase64String(algorithm.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
}

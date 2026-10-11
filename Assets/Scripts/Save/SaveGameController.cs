using System;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Runtime API and Inspector context-menu controls. Slots are numbered 1 and 2.</summary>
[DisallowMultipleComponent]
public sealed class SaveGameController : MonoBehaviour
{
    public static SaveGameController Instance { get; private set; }
    public SaveSlotService Slots { get; private set; }
    public InventorySaveSection Inventories { get; private set; }
    public string LastError { get; private set; }
    public event Action<int> Saved;
    public event Action<int> Loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureInstance();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureInstance();

    private static void EnsureInstance()
    {
        if (Instance || !FindAnyObjectByType<PotatoFarmingSystem>(FindObjectsInactive.Include)) return;
        new GameObject("SaveGameController").AddComponent<SaveGameController>();
    }

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Slots = new SaveSlotService(Path.Combine(Application.persistentDataPath, "Saves"));
        Inventories = new InventorySaveSection();
        Slots.Register(new FarmingSaveSection());
        Slots.Register(Inventories);
    }

    public bool Save(int slot) => Execute(() => Slots.Save(slot), () => Saved?.Invoke(slot));
    public bool Load(int slot) => Execute(() => Slots.Load(slot), () => Loaded?.Invoke(slot));
    public bool LoadBackup(int slot) => Execute(() => Slots.Load(slot, true), () => Loaded?.Invoke(slot));

    private bool Execute(Action operation, Action completed)
    {
        try
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Save/load is available in Play mode.");
            if (!FindAnyObjectByType<PotatoFarmingSystem>(FindObjectsInactive.Include))
                throw new InvalidOperationException("Open a farming gameplay scene first.");
            if (NetworkManager.Singleton && NetworkManager.Singleton.IsListening)
                throw new InvalidOperationException("Local save/load requires an offline session.");
            operation();
            LastError = null;
        }
        catch (Exception error)
        {
            LastError = error.Message;
            Debug.LogWarning("[SaveGame] " + LastError, this);
            return false;
        }
        try { completed(); }
        catch (Exception error) { Debug.LogException(error, this); }
        return true;
    }

    [ContextMenu("Save/Slot 1")] private void SaveSlot1() => Save(1);
    [ContextMenu("Save/Slot 2")] private void SaveSlot2() => Save(2);
    [ContextMenu("Load/Slot 1")] private void LoadSlot1() => Load(1);
    [ContextMenu("Load/Slot 2")] private void LoadSlot2() => Load(2);
    [ContextMenu("Load Backup/Slot 1")] private void LoadBackup1() => LoadBackup(1);
    [ContextMenu("Load Backup/Slot 2")] private void LoadBackup2() => LoadBackup(2);

    private void OnDestroy() { if (Instance == this) Instance = null; }
}

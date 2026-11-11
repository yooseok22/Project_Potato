using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TabletPreferences : MonoBehaviour
{
    [Serializable] public sealed class Values
    {
        public float cameraSensitivity = 1f, mouseSensitivity = 1f, fov = 60f;
        public bool cameraShake = true, vSync;
        public int windowMode, width = 1920, height = 1080, maxFps = 120;
        public float master = 0.8f, music = 1f, effects = 1f, ambience = 1f, microphoneVolume = 0.5f;
        public string microphone = "";
        public int microphoneMode;
    }
    public Values Data { get; private set; } = new();
    public string PathOverride { get; set; }
    public string LastError { get; private set; }
    private bool dirty;
    private float saveAt;
    private readonly Dictionary<AudioSource, float> sourceLevels = new();
    private string FilePath => PathOverride ?? Path.Combine(Application.persistentDataPath, "Settings", "tablet-settings.json");
    public static Vector2Int[] Resolutions => Screen.resolutions.Select(r => new Vector2Int(r.width, r.height))
        .Concat(new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) }).Distinct().OrderBy(v => v.x).ThenBy(v => v.y).ToArray();
    public string[] Microphones => Microphone.devices;
    public event Action Changed;

    private void Start()
    {
        var owner = GetComponent<FirstPersonPlayerController>();
        if (owner && !owner.IsLocallyControlled) { enabled = false; return; }
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonUtility.FromJson<Values>(File.ReadAllText(FilePath));
                if (loaded != null) Data = loaded;
            }
        }
        catch (Exception error) { LastError = error.Message; }
        Clamp();
        Apply();
    }

    private void Clamp()
    {
        Data.cameraSensitivity = Finite(Data.cameraSensitivity, 1f, 0.1f, 4f);
        Data.mouseSensitivity = Finite(Data.mouseSensitivity, 1f, 0.1f, 4f);
        Data.fov = Finite(Data.fov, 60f, 40f, 110f);
        Data.master = Finite(Data.master, 0.8f, 0f, 1f);
        Data.music = Finite(Data.music, 1f, 0f, 1f);
        Data.effects = Finite(Data.effects, 1f, 0f, 1f);
        Data.ambience = Finite(Data.ambience, 1f, 0f, 1f);
        Data.microphoneVolume = Finite(Data.microphoneVolume, 0.5f, 0f, 1f);
        Data.windowMode = Mathf.Clamp(Data.windowMode, 0, 2);
        Data.maxFps = Mathf.Clamp(Data.maxFps, 30, 360);
        Data.width = Mathf.Clamp(Data.width, 800, 7680); Data.height = Mathf.Clamp(Data.height, 600, 4320);
        Data.microphoneMode = Mathf.Clamp(Data.microphoneMode, 0, 2);
    }
    private static float Finite(float value, float fallback, float min, float max) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    public void Set(string key, float value)
    {
        switch (key)
        {
            case "cameraSensitivity": Data.cameraSensitivity = value; break;
            case "mouseSensitivity": Data.mouseSensitivity = value; break;
            case "fov": Data.fov = value; break;
            case "shake": Data.cameraShake = !Data.cameraShake; break;
            case "vsync": Data.vSync = !Data.vSync; break;
            case "master": Data.master = value; break;
            case "music": Data.music = value; break;
            case "effects": Data.effects = value; break;
            case "ambience": Data.ambience = value; break;
            case "microphoneVolume": Data.microphoneVolume = value; break;
            case "microphone": Data.microphone = Microphones.Length > 0 ? Microphones[Mathf.Clamp((int)value, 0, Microphones.Length - 1)] : ""; break;
            case "microphoneMode": Data.microphoneMode = (int)value; break;
            case "window": Data.windowMode = (int)value; break;
            case "fps": Data.maxFps = new[] { 30, 60, 90, 120, 144, 240, 360 }[Mathf.Clamp((int)value, 0, 6)]; break;
            case "resolution": var resolution = Resolutions[Mathf.Clamp((int)value, 0, Resolutions.Length - 1)]; Data.width = resolution.x; Data.height = resolution.y; break;
        }
        Clamp(); Apply(); dirty = true; saveAt = Time.unscaledTime + 0.35f; Changed?.Invoke();
    }
    public void Apply()
    {
        var rig = GetComponent<FirstPersonPlayerController>()?.ViewCamera;
        if (rig) rig.SetTabletSensitivity(Data.cameraSensitivity * Data.mouseSensitivity);
        var camera = rig ? rig.GetComponent<Camera>() : null;
        if (camera) camera.fieldOfView = Data.fov;
        var visuals = GetComponent<PlayerVisualController>();
        if (visuals) visuals.MotionEffectsEnabled = Data.cameraShake;
        QualitySettings.vSyncCount = Data.vSync ? 1 : 0;
        Application.targetFrameRate = Data.maxFps;
        if (!Application.isEditor) Screen.SetResolution(Data.width, Data.height,
            Data.windowMode == 0 ? FullScreenMode.ExclusiveFullScreen : Data.windowMode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        AudioListener.volume = Data.master;
        foreach (var source in FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
        {
            if (!sourceLevels.ContainsKey(source)) sourceLevels.Add(source, source.volume);
            string name = (source.name + " " + (source.clip ? source.clip.name : "")).ToLowerInvariant();
            float level = name.Contains("music") || name.Contains("bgm") ? Data.music : name.Contains("ambient") || name.Contains("environment") ? Data.ambience : Data.effects;
            source.volume = sourceLevels[source] * level;
        }
    }
    private void Update() { if (dirty && Time.unscaledTime >= saveAt) Flush(); }
    public bool Flush()
    {
        if (!dirty) return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath + ".tmp", JsonUtility.ToJson(Data, true));
            if (File.Exists(FilePath)) File.Replace(FilePath + ".tmp", FilePath, null);
            else File.Move(FilePath + ".tmp", FilePath);
            dirty = false; LastError = null; return true;
        }
        catch (Exception error) { LastError = error.Message; return false; }
    }
    private void OnDisable() { if (Application.isPlaying) Flush(); }
}

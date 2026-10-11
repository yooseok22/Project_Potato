using System;
using UnityEngine;

/// <summary>Shared simulation seconds; calendar speed does not change crop growth speed.</summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed partial class WorldClock : MonoBehaviour
{
    [SerializeField, Min(1f)] private float dayLengthSeconds = 600f;
    [SerializeField, Range(0f, 24f)] private float startingHour = 6f;
    [SerializeField, Range(0f, 24f)] private float sunriseHour = 6f;
    [SerializeField, Range(0f, 24f)] private float sunsetHour = 18f;
    [SerializeField, Min(0f)] private float speed = 1f;
    [SerializeField] private bool paused;
    [SerializeField] private bool logTransitions = true;

    public double ElapsedSeconds { get; private set; }
    public double TotalDays => startingHour / 24d + ElapsedSeconds / Math.Max(1d, dayLengthSeconds);
    public int DayNumber => (int)Math.Floor(TotalDays) + 1;
    public double HourOfDay => (TotalDays - Math.Floor(TotalDays)) * 24d;
    public bool IsDay => sunriseHour < sunsetHour
        ? HourOfDay >= sunriseHour && HourOfDay < sunsetHour
        : HourOfDay >= sunriseHour || HourOfDay < sunsetHour;
    public bool IsPaused { get => paused; set => paused = value; }
    public float Speed
    {
        get => speed;
        set
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(nameof(value));
            speed = value;
        }
    }
    public string DisplayTime => $"Day {DayNumber} {((int)HourOfDay):00}:{((int)(HourOfDay * 60) % 60):00}";

    /// <summary>Raised once after each advance; time skips report the final calendar state.</summary>
    public event Action TimeAdvanced;
    public event Action<bool> DayNightChanged;

    private void Start()
    {
        if (logTransitions) Debug.Log($"[World] {DisplayTime} / {(IsDay ? "낮" : "밤")}", this);
    }

    private void Update() => Tick(Time.deltaTime);

    /// <summary>Scaled Unity seconds. Pausing or changing speed affects all clock consumers.</summary>
    public void Tick(double deltaSeconds)
    {
        ValidateSeconds(deltaSeconds);
        if (!paused) AdvanceBy(deltaSeconds * speed);
    }

    /// <summary>Explicit simulation-time skip (e.g. sleeping); deliberately works while paused.</summary>
    public void AdvanceBy(double seconds)
    {
        ValidateSeconds(seconds);
        if (seconds == 0d) return;
        bool wasDay = IsDay;
        int previousDay = DayNumber;
        ElapsedSeconds += seconds;
        TimeAdvanced?.Invoke();
        if (wasDay != IsDay) DayNightChanged?.Invoke(IsDay);
        if (logTransitions && (wasDay != IsDay || previousDay != DayNumber))
            Debug.Log($"[World] {DisplayTime} / {(IsDay ? "낮" : "밤")}", this);
    }

    private static void ValidateSeconds(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Time must be finite and non-negative.");
    }

    [ContextMenu("Debug/Advance 40 Seconds")]
    private void DebugAdvance()
    {
        if (Application.isPlaying) AdvanceBy(40d);
    }

    [ContextMenu("Debug/Toggle Pause")]
    private void DebugPause()
    {
        if (Application.isPlaying) paused = !paused;
    }
}

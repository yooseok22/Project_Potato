public sealed partial class WorldClock
{
    internal FarmClockData CaptureSaveData() => new()
    {
        id = SaveIdentity.Key(this), elapsedSeconds = ElapsedSeconds, dayLengthSeconds = dayLengthSeconds,
        startingHour = startingHour, sunriseHour = sunriseHour, sunsetHour = sunsetHour, speed = speed, paused = paused
    };

    internal static void ValidateSaveData(FarmClockData data)
    {
        SaveValidation.Require(SaveValidation.NonNegative(data.elapsedSeconds) && data.elapsedSeconds < 100000000000d
            && SaveValidation.Finite(data.dayLengthSeconds) && data.dayLengthSeconds >= 1
            && SaveValidation.NonNegative(data.startingHour) && data.startingHour <= 24
            && SaveValidation.NonNegative(data.sunriseHour) && data.sunriseHour <= 24
            && SaveValidation.NonNegative(data.sunsetHour) && data.sunsetHour <= 24
            && SaveValidation.NonNegative(data.speed), "Invalid saved clock.");
    }

    internal void RestoreSaveData(FarmClockData data)
    {
        ElapsedSeconds = data.elapsedSeconds;
        dayLengthSeconds = data.dayLengthSeconds;
        startingHour = data.startingHour;
        sunriseHour = data.sunriseHour;
        sunsetHour = data.sunsetHour;
        speed = data.speed;
        paused = data.paused;
    }

    internal void NotifySaveRestored()
    {
        TimeAdvanced?.Invoke();
        DayNightChanged?.Invoke(IsDay);
    }
}

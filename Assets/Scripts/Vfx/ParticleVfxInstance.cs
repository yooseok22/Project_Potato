using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Owns all child emitters and releases a one-shot only after its last particle dies.</summary>
[DisallowMultipleComponent]
public sealed class ParticleVfxInstance : MonoBehaviour
{
    private ParticleSystem[] systems;
    private bool looping;
    private ParticleSystem.MinMaxGradient[] fadeColors;
    private float fadeDuration;
    private float fadeElapsed;

    public bool IsFading => fadeColors != null;

    public static ParticleVfxInstance Spawn(GameObject prefab, Vector3 position, Transform owner, bool loop)
    {
        if (!prefab || !owner) return null;
        // Exported prefab positions are demonstration coordinates, not gameplay offsets.
        var go = Instantiate(prefab, position, prefab.transform.rotation);
        SceneManager.MoveGameObjectToScene(go, owner.gameObject.scene);
        go.transform.SetParent(owner, true);
        go.name = prefab.name + " (Runtime)";
        var instance = go.AddComponent<ParticleVfxInstance>();
        instance.systems = go.GetComponentsInChildren<ParticleSystem>(true);
        instance.looping = loop;
        foreach (var system in instance.systems)
        {
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = loop;
            main.playOnAwake = false;
            main.stopAction = ParticleSystemStopAction.None;
        }
        foreach (var system in instance.systems)
            if (system.gameObject.activeInHierarchy) system.Play(false);
        return instance;
    }

    private void Update()
    {
        if (IsFading)
        {
            fadeElapsed += Time.deltaTime;
            float opacity = 1f - Mathf.Clamp01(fadeElapsed / fadeDuration);
            for (int index = 0; index < systems.Length; index++)
            {
                if (!systems[index]) continue;
                var color = systems[index].colorOverLifetime;
                color.enabled = true;
                color.color = WithOpacity(fadeColors[index], opacity);
            }
            if (opacity <= 0f) Release();
            return;
        }
        if (looping || systems == null) return;
        foreach (var system in systems)
            if (system && system.IsAlive(false)) return;
        Release();
    }

    /// <summary>Stops new emissions and fades existing particles without clearing or restarting them.</summary>
    public void FadeOut(float seconds)
    {
        if (IsFading) return;
        if (seconds <= 0f || systems == null) { Release(); return; }
        fadeDuration = seconds;
        fadeElapsed = 0f;
        fadeColors = new ParticleSystem.MinMaxGradient[systems.Length];
        for (int index = 0; index < systems.Length; index++)
        {
            var system = systems[index];
            if (!system) continue;
            system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            var main = system.main;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var color = system.colorOverLifetime;
            fadeColors[index] = color.enabled ? color.color : new ParticleSystem.MinMaxGradient(Color.white);
        }
    }

    private static ParticleSystem.MinMaxGradient WithOpacity(ParticleSystem.MinMaxGradient source, float opacity)
    {
        Color FadeColor(Color color) { color.a *= opacity; return color; }
        Gradient FadeGradient(Gradient original)
        {
            var alphas = original.alphaKeys;
            for (int index = 0; index < alphas.Length; index++) alphas[index].alpha *= opacity;
            var gradient = new Gradient { mode = original.mode };
            gradient.SetKeys(original.colorKeys, alphas);
            return gradient;
        }
        return source.mode switch
        {
            ParticleSystemGradientMode.Color => new ParticleSystem.MinMaxGradient(FadeColor(source.color)),
            ParticleSystemGradientMode.TwoColors => new ParticleSystem.MinMaxGradient(FadeColor(source.colorMin), FadeColor(source.colorMax)),
            ParticleSystemGradientMode.TwoGradients => new ParticleSystem.MinMaxGradient(FadeGradient(source.gradientMin), FadeGradient(source.gradientMax)),
            _ => new ParticleSystem.MinMaxGradient(FadeGradient(source.gradient)) { mode = source.mode }
        };
    }

    public void Release()
    {
        if (systems != null)
            foreach (var system in systems)
                if (system) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }
}

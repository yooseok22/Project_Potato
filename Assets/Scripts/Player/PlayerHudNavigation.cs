using TMPro;
using UnityEngine;

/// <summary>Camera-bearing compass and the same previous/current/next order as the player's wheel input.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
public sealed class PlayerHudNavigation : MonoBehaviour
{
    [SerializeField] private PlayerItemController playerItems;
    [SerializeField] private Sprite compassArtwork;
    [SerializeField] private RectTransform compassViewport;
    [SerializeField] private RectTransform[] tickSegments;
    [SerializeField] private RectTransform[] bearingLabels;
    [SerializeField] private TMP_Text headingLabel;
    [SerializeField, Min(1f)] private float pixelsPerDegree = 8f;
    [SerializeField] private UnityEngine.UI.Image previousIcon, currentIcon, nextIcon;
    [SerializeField] private TMP_Text previousFallback, currentFallback, nextFallback;
    [SerializeField] private TMP_Text previousName, currentName, nextName;
    [SerializeField] private Sprite[] toolSprites = new Sprite[5];
    private PlayerItemController boundItems;
    private FirstPersonPlayerController player;
    private Transform view;
    private float nextResolveAt;
    public float Heading { get; private set; }
    public int DisplayedSlot { get; private set; }
    public int PreviousSlot => WrappedSlot(DisplayedSlot - 1);
    public int NextSlot => WrappedSlot(DisplayedSlot + 1);
    public Sprite CompassArtwork => compassArtwork;

    public static int WrappedSlot(int index) => ((index % 5) + 5) % 5;
    public static string ToolName(int slot) => (PlayerItemSlot)WrappedSlot(slot) switch
    {
        PlayerItemSlot.Hands => "맨손",
        PlayerItemSlot.Hoe => "괭이",
        PlayerItemSlot.WateringCan => "물뿌리개",
        PlayerItemSlot.Sickle => "낫",
        _ => "감자 포크"
    };

    public void Configure(Sprite artwork, RectTransform viewport, RectTransform[] ticks, RectTransform[] labels,
        TMP_Text heading, UnityEngine.UI.Image[] icons, TMP_Text[] fallbacks, TMP_Text[] names, Sprite[] sprites)
    {
        compassArtwork = artwork;
        compassViewport = viewport;
        tickSegments = ticks;
        bearingLabels = labels;
        headingLabel = heading;
        previousIcon = icons[0]; currentIcon = icons[1]; nextIcon = icons[2];
        previousFallback = fallbacks[0]; currentFallback = fallbacks[1]; nextFallback = fallbacks[2];
        previousName = names[0]; currentName = names[1]; nextName = names[2];
        toolSprites = sprites;
        if (Application.isPlaying) ResolvePlayer();
        RefreshToolViews(playerItems ? playerItems.SelectedIndex : 0);
        RefreshHeading(0f);
    }

    private void OnEnable()
    {
        if (Application.isPlaying) ResolvePlayer();
    }

    private void ResolvePlayer()
    {
        if (!playerItems || !IsLocal(playerItems))
        {
            playerItems = null;
            foreach (var candidate in FindObjectsByType<PlayerItemController>(FindObjectsSortMode.None))
                if (IsLocal(candidate)) { playerItems = candidate; break; }
        }
        if (!playerItems) return;
        player = playerItems.GetComponent<FirstPersonPlayerController>();
        if (boundItems != playerItems)
        {
            Unbind();
            boundItems = playerItems;
            boundItems.SelectedSlotChanged += OnSlotChanged;
        }
        view = player && player.ViewCamera ? player.ViewCamera.transform : Camera.main ? Camera.main.transform : null;
        RefreshToolViews(playerItems.SelectedIndex);
    }

    private static bool IsLocal(PlayerItemController items)
    {
        var controller = items ? items.GetComponent<FirstPersonPlayerController>() : null;
        return controller && controller.isActiveAndEnabled && controller.IsLocallyControlled;
    }

    private void LateUpdate()
    {
        if ((!playerItems || !IsLocal(playerItems) || !view) && Time.unscaledTime >= nextResolveAt)
        {
            nextResolveAt = Time.unscaledTime + 0.5f;
            ResolvePlayer();
        }
        if (!view) return;
        Vector3 forward = view.forward;
        if (forward.x * forward.x + forward.z * forward.z < 0.00001f) return;
        RefreshHeading(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);
    }

    public void RefreshHeading(float degrees)
    {
        Heading = Mathf.Repeat(degrees, 360f);
        if (tickSegments != null)
            for (int i = 0; i < tickSegments.Length; i++)
                if (tickSegments[i]) tickSegments[i].anchoredPosition = new Vector2(
                    Mathf.DeltaAngle(Heading, i * 45f + 22.5f) * pixelsPerDegree, 9f);
        if (bearingLabels != null)
            for (int i = 0; i < bearingLabels.Length; i++)
                if (bearingLabels[i]) bearingLabels[i].anchoredPosition = new Vector2(
                    Mathf.DeltaAngle(Heading, i * 15f) * pixelsPerDegree, -12f);
        if (headingLabel) headingLabel.SetText("{0:000}", Mathf.RoundToInt(Heading) % 360);
    }

    private void OnSlotChanged(PlayerItemSlot slot) => RefreshToolViews((int)slot);

    public void RefreshToolViews(int selected)
    {
        DisplayedSlot = WrappedSlot(selected);
        SetTool(previousIcon, previousFallback, previousName, PreviousSlot, "이전");
        SetTool(currentIcon, currentFallback, currentName, DisplayedSlot, "현재");
        SetTool(nextIcon, nextFallback, nextName, NextSlot, "다음");
    }

    private void SetTool(UnityEngine.UI.Image image, TMP_Text fallback, TMP_Text nameLabel, int slot, string role)
    {
        var sprite = toolSprites != null && slot < toolSprites.Length ? toolSprites[slot] : null;
        if (image)
        {
            image.sprite = sprite;
            image.enabled = sprite;
            image.preserveAspect = true;
        }
        if (fallback)
        {
            fallback.gameObject.SetActive(!sprite);
            fallback.SetText(ToolName(slot));
        }
        if (nameLabel) nameLabel.SetText(role + " · " + ToolName(slot));
    }

    private void Unbind()
    {
        if (boundItems) boundItems.SelectedSlotChanged -= OnSlotChanged;
        boundItems = null;
    }
    private void OnDisable() => Unbind();
}

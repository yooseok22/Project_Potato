using TMPro;
using UnityEngine;

public sealed class WateringInteractionView : MonoBehaviour
{
    private const string KoreanPromptFontPath = "Fonts/MalgunGothic-Dynamic";

    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TMP_Text promptLabel;
    [SerializeField] private TMP_FontAsset promptFont;

    public bool IsVisible => promptRoot && promptRoot.activeSelf;

    private void Awake()
    {
        ApplyPromptFont();
    }

    public void Configure(GameObject root, TMP_Text label)
    {
        promptRoot = root;
        promptLabel = label;
        ApplyPromptFont();
        if (promptLabel) promptLabel.text = "[ LMB ]  WATER";
        SetVisible(false);
    }

    public void SetVisible(bool visible)
    {
        if (promptRoot && promptRoot.activeSelf != visible) promptRoot.SetActive(visible);
    }

    public void SetPrompt(string text)
    {
        if (promptLabel && promptLabel.text != text) promptLabel.text = text;
        SetVisible(!string.IsNullOrEmpty(text));
    }

    private void ApplyPromptFont()
    {
        if (!promptLabel) return;
        if (!promptFont) promptFont = Resources.Load<TMP_FontAsset>(KoreanPromptFontPath);
        if (promptFont && promptFont.material) promptLabel.font = promptFont;
    }

    private void OnDisable()
    {
        if (promptRoot) promptRoot.SetActive(false);
    }
}

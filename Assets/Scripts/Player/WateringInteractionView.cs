using TMPro;
using UnityEngine;

public sealed class WateringInteractionView : MonoBehaviour
{
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TMP_Text promptLabel;

    public bool IsVisible => promptRoot && promptRoot.activeSelf;

    public void Configure(GameObject root, TMP_Text label)
    {
        promptRoot = root;
        promptLabel = label;
        if (promptLabel) promptLabel.text = "[ LMB ]  WATER";
        SetVisible(false);
    }

    public void SetVisible(bool visible)
    {
        if (promptRoot && promptRoot.activeSelf != visible) promptRoot.SetActive(visible);
    }

    private void OnDisable()
    {
        if (promptRoot) promptRoot.SetActive(false);
    }
}

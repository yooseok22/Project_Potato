using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Serialized UI action binding, including dropdowns and sliders, survives prefab saves.</summary>
public sealed class TabletControl : MonoBehaviour, IPointerEnterHandler
{
    public string action;
    public int index;
    public bool selectOnHover;
    private TabletView view;
    private UnityEngine.UI.Button button;
    private UnityEngine.UI.Slider slider;
    private TMP_Dropdown dropdown;
    private TMP_InputField input;
    private void OnEnable()
    {
        view = GetComponentInParent<TabletView>(true);
        button = GetComponent<UnityEngine.UI.Button>();
        slider = GetComponent<UnityEngine.UI.Slider>();
        dropdown = GetComponent<TMP_Dropdown>();
        input = GetComponent<TMP_InputField>();
        if (button) button.onClick.AddListener(Click);
        if (slider) slider.onValueChanged.AddListener(Value);
        if (dropdown) dropdown.onValueChanged.AddListener(Choice);
        if (input) input.onEndEdit.AddListener(Text);
    }
    private void Click() { if (view) view.Action(action, index); }
    private void Value(float value) { if (view) view.Value(action, value); }
    private void Choice(int value) => Value(value);
    private void Text(string value) { if (view) view.Text(action, value); }
    public void OnPointerEnter(PointerEventData _) { if (selectOnHover && view) view.Action(action, index); }
    private void OnDisable()
    {
        if (button) button.onClick.RemoveListener(Click);
        if (slider) slider.onValueChanged.RemoveListener(Value);
        if (dropdown) dropdown.onValueChanged.RemoveListener(Choice);
        if (input) input.onEndEdit.RemoveListener(Text);
    }
}

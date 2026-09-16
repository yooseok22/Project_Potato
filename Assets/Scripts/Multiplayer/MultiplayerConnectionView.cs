using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MultiplayerConnectionView : MonoBehaviour
{
    [SerializeField] private MultiplayerSessionController controller;
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private GameObject sessionPanel;
    [SerializeField] private TMP_InputField codeInput;
    [SerializeField] private Button createButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button copyButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private TMP_Text codeLabel;
    [SerializeField] private TMP_Text statusLabel;

    private void Start()
    {
        controller.Changed += Refresh;
        codeInput.onValueChanged.AddListener(OnCodeChanged);
        createButton.onClick.AddListener(controller.CreateSession);
        joinButton.onClick.AddListener(Join);
        copyButton.onClick.AddListener(Copy);
        leaveButton.onClick.AddListener(controller.LeaveSession);
        Refresh();
    }

    private void OnCodeChanged(string _) => Refresh();
    private void Join() => controller.JoinSession(codeInput.text);
    private void Copy() => GUIUtility.systemCopyBuffer = controller.Code;
    private void Refresh()
    {
        menuPanel.SetActive(!controller.InSession);
        sessionPanel.SetActive(controller.InSession);
        createButton.interactable = !controller.Busy && !controller.InSession;
        codeInput.interactable = !controller.Busy && !controller.InSession;
        joinButton.interactable = !controller.Busy && !controller.InSession && !string.IsNullOrWhiteSpace(codeInput.text);
        copyButton.interactable = !string.IsNullOrEmpty(controller.Code);
        leaveButton.interactable = !controller.Busy;
        codeLabel.text = "SESSION CODE   " + controller.Code;
        statusLabel.text = controller.Status;
    }

    private void OnDestroy()
    {
        if (!controller) return;
        controller.Changed -= Refresh;
        codeInput.onValueChanged.RemoveListener(OnCodeChanged);
        createButton.onClick.RemoveListener(controller.CreateSession);
        joinButton.onClick.RemoveListener(Join);
        copyButton.onClick.RemoveListener(Copy);
        leaveButton.onClick.RemoveListener(controller.LeaveSession);
    }
}

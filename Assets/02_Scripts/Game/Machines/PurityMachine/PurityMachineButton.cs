using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Interactable world button for an installed purity machine.
/// It mirrors the simple interaction pattern used by machine modules: player interaction presses the button, plays button feedback and asks the machine controller to start.
/// </summary>
public sealed class PurityMachineButton : MonoBehaviour, IPlayerInteractable
{
    [Header("References")]
    [Tooltip("Purity machine controller started by this button.")]
    [SerializeField] private PurityMachineController PurityMachineController;

    [Tooltip("Animator used by the button itself to show a physical press.")]
    [SerializeField] private Animator ButtonAnimator;

    [Header("Animator")]
    [Tooltip("Animator trigger fired every time the player presses the button.")]
    [SerializeField] private string PressTriggerName = "Press";

    [Header("Events")]
    [Tooltip("Invoked every time the button is pressed, before the machine validates whether processing can start.")]
    [SerializeField] private UnityEvent OnButtonPressed;

    [Tooltip("Invoked when pressing the button successfully starts purity processing.")]
    [SerializeField] private UnityEvent OnButtonAccepted;

    [Tooltip("Invoked when pressing the button fails because the purity machine inputs are invalid or already processing.")]
    [SerializeField] private UnityEvent OnButtonRejected;

    [Header("Debug")]
    [Tooltip("Logs button press results.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Handles the player's generic interact input.
    /// </summary>
    /// <returns>True because this button consumes the interaction even when the machine rejects processing.</returns>
    public bool TryInteract()
    {
        Press();
        return true;
    }

    /// <summary>
    /// Presses the button, plays local press animation and asks the machine controller to start processing.
    /// This can be called by an animation event, UnityEvent or debug button as well as player interaction.
    /// </summary>
    public void Press()
    {
        ResolveReferences();
        TriggerButtonPressAnimation();
        OnButtonPressed?.Invoke();

        if (PurityMachineController == null)
        {
            Log("Button rejected because no purity machine controller is assigned.");
            OnButtonRejected?.Invoke();
            return;
        }

        bool WasAccepted = PurityMachineController.TryStartProcessing();

        if (WasAccepted)
        {
            OnButtonAccepted?.Invoke();
            Log("Button accepted.");
            return;
        }

        OnButtonRejected?.Invoke();
        Log("Button rejected by machine validation.");
    }

    /// <summary>
    /// Resolves optional local references.
    /// </summary>
    private void ResolveReferences()
    {
        if (ButtonAnimator == null)
        {
            ButtonAnimator = GetComponentInChildren<Animator>(true);
        }

        if (PurityMachineController == null)
        {
            PurityMachineController = GetComponentInParent<PurityMachineController>();
        }
    }

    /// <summary>
    /// Plays the configured button press trigger.
    /// </summary>
    private void TriggerButtonPressAnimation()
    {
        if (ButtonAnimator == null || string.IsNullOrWhiteSpace(PressTriggerName))
        {
            return;
        }

        ButtonAnimator.SetTrigger(PressTriggerName);
    }

    /// <summary>
    /// Debug helper for testing the button from the inspector.
    /// </summary>
    [ContextMenu("Press Purity Machine Button")]
    private void DebugPress()
    {
        Press();
    }

    /// <summary>
    /// Logs button messages when debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to write.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PurityMachineButton] " + Message, this);
    }
}

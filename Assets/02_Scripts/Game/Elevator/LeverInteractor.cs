using UnityEngine;

/// <summary>
/// Player-side interaction driver that acquires a looked snap lever
/// and feeds mouse delta into it while the primary action is held.
/// </summary>
[DisallowMultipleComponent]
public sealed class LeverInteractor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to query looked levers.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Input reader that provides hold state and look delta.")]
    [SerializeField] private PlayerInputReader PlayerInputReader;

    [Tooltip("Player controller used to temporarily block look while dragging.")]
    [SerializeField] private PlayerController PlayerController;

    [Header("Raycast")]
    [Tooltip("Physics layers considered valid for lever interaction.")]
    [SerializeField] private LayerMask InteractionMask = ~0;

    [Tooltip("Default cast distance when the lever does not provide a custom one.")]
    [SerializeField] private float DefaultInteractionDistance = 3f;

    [Tooltip("Default sphere cast radius used to make acquisition more permissive.")]
    [SerializeField] private float DefaultSphereCastRadius = 0.2f;

    [Tooltip("Optional hotbar used to stop the currently equipped item while dragging a lever.")]
    [SerializeField] private HotbarController HotbarController;

    /// <summary>
    /// Returns whether the primary input is currently captured by a lever interaction.
    /// </summary>
    public bool IsCapturingPrimaryInput => CurrentLever != null;

    [Header("Debug")]
    [Tooltip("Logs runtime acquisition and release messages.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Lever currently controlled by the player.
    /// </summary>
    private SnapLever CurrentLever;

    /// <summary>
    /// Caches component references if they were not assigned explicitly.
    /// </summary>
    private void Awake()
    {
        if (PlayerCamera == null)
        {
            PlayerCamera = GetComponentInChildren<Camera>(true);
        }

        if (PlayerInputReader == null)
        {
            PlayerInputReader = GetComponent<PlayerInputReader>();
        }

        if (PlayerController == null)
        {
            PlayerController = GetComponent<PlayerController>();
        }

        if (HotbarController == null)
        {
            HotbarController = GetComponentInChildren<HotbarController>(true);
        }
    }

    /// <summary>
    /// Updates acquisition, dragging and release.
    /// </summary>
    private void Update()
    {
        if (PlayerCamera == null || PlayerInputReader == null)
        {
            return;
        }

        if (CurrentLever == null)
        {
            TryAcquireLever();
            return;
        }

        if (!PlayerInputReader.IsUsePrimaryHeld)
        {
            ReleaseCurrentLever();
            return;
        }

        if (CurrentLever != null && CurrentLever.GetIsExternallyLocked())
        {
            ReleaseCurrentLever();
            return;
        }

        CurrentLever.ProcessDrag(PlayerInputReader.Look);
    }

    /// <summary>
    /// Tries to acquire a lever currently in front of the player.
    /// </summary>
    private void TryAcquireLever()
    {
        if (!PlayerInputReader.IsUsePrimaryHeld)
        {
            return;
        }

        if (!TryGetLookedLever(out SnapLever Lever))
        {
            return;
        }

        CurrentLever = Lever;

        if (Lever.GetIsExternallyLocked())
        {
            return;
        }

        if (HotbarController != null)
        {
            EquippedItemBehaviour EquippedItemBehaviour = HotbarController.GetCurrentEquippedItemBehaviour();
            if (EquippedItemBehaviour != null)
            {
                EquippedItemBehaviour.ForceStopItemUsage();
            }
        }

        CurrentLever.BeginDrag();

        if (PlayerController != null)
        {
            PlayerController.SetExternalLookBlocked(true);
        }

        if (DebugLogs)
        {
            Debug.Log("[LeverInteractor] Acquired lever: " + CurrentLever.name, this);
        }
    }

    /// <summary>
    /// Releases the currently controlled lever.
    /// </summary>
    private void ReleaseCurrentLever()
    {
        if (CurrentLever == null)
        {
            return;
        }

        CurrentLever.EndDrag();

        if (DebugLogs)
        {
            Debug.Log("[LeverInteractor] Released lever: " + CurrentLever.name, this);
        }

        CurrentLever = null;

        if (PlayerController != null)
        {
            PlayerController.SetExternalLookBlocked(false);
        }
    }

    /// <summary>
    /// Resolves the most suitable lever from the current camera forward direction.
    /// </summary>
    /// <param name="Lever">Resolved lever.</param>
    /// <returns>True when a valid lever is found.</returns>
    private bool TryGetLookedLever(out SnapLever Lever)
    {
        Lever = null;

        Ray CameraRay = new Ray(PlayerCamera.transform.position, PlayerCamera.transform.forward);

        if (!Physics.SphereCast(
                CameraRay,
                DefaultSphereCastRadius,
                out RaycastHit Hit,
                DefaultInteractionDistance,
                InteractionMask,
                QueryTriggerInteraction.Collide))
        {
            return false;
        }

        SnapLever CandidateLever = Hit.collider.GetComponentInParent<SnapLever>();
        if (CandidateLever == null)
        {
            return false;
        }

        Collider LeverCollider = CandidateLever.GetInteractionCollider();
        float AllowedDistance = CandidateLever.GetInteractionDistance();
        float AllowedRadius = CandidateLever.GetInteractionRadius();

        if (Hit.distance > AllowedDistance)
        {
            return false;
        }

        if (LeverCollider != null)
        {
            Vector3 ClosestPoint = LeverCollider.ClosestPoint(PlayerCamera.transform.position);
            float DistanceToLeverBody = Vector3.Distance(PlayerCamera.transform.position, ClosestPoint);

            if (DistanceToLeverBody > AllowedDistance + AllowedRadius)
            {
                return false;
            }
        }

        Lever = CandidateLever;
        return true;
    }

    /// <summary>
    /// Ensures the lever is released if the interactor gets disabled.
    /// </summary>
    private void OnDisable()
    {
        ReleaseCurrentLever();
    }
}
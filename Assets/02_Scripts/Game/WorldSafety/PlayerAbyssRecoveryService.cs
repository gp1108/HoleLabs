using System.Collections;
using UnityEngine;

/// <summary>
/// Handles player death recovery caused by abyss hazards or future damage systems.
/// It teleports the player to a configured safe point, optionally resets the elevator and can clear loose abyss objects.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerAbyssRecoveryService : MonoBehaviour
{
    [Header("Player References")]
    [Tooltip("Player health component that raises death events. If empty, the first PlayerHealth in the scene is used.")]
    [SerializeField] private PlayerHealth PlayerHealth;

    [Tooltip("Player controller to reposition when recovery starts. If empty, the first PlayerController in the scene is used.")]
    [SerializeField] private PlayerController PlayerController;

    [Tooltip("Safe respawn point used when the player dies in the abyss.")]
    [SerializeField] private Transform PlayerRespawnPoint;

    [Header("Elevator Recovery")]
    [Tooltip("If true, the elevator motor is also reset to a configured safe travel distance.")]
    [SerializeField] private bool RecoverElevator = true;

    [Tooltip("Elevator motor moved back to the configured recovery distance.")]
    [SerializeField] private ElevatorPhysicalMotor ElevatorMotor;

    [Tooltip("Travel distance applied to the elevator during recovery. Usually 0 means laboratory/top.")]
    [SerializeField] private float ElevatorRecoveryDistance = 0f;

    [Tooltip("Optional transform used as the elevator recovery rotation. If empty, the current elevator rotation is preserved.")]
    [SerializeField] private Transform ElevatorRecoveryRotationReference;

    [Header("Abyss Cleanup")]
    [Tooltip("Optional abyss field used to clear loose objects below the current abyss surface when the player dies.")]
    [SerializeField] private AbyssFieldController AbyssField;

    [Tooltip("If true, loose ores below the abyss surface are removed when the player dies.")]
    [SerializeField] private bool ClearLooseOresOnDeath = true;

    [Tooltip("If true, loose money below the abyss surface is removed when the player dies.")]
    [SerializeField] private bool ClearLooseMoneyOnDeath = true;

    [Tooltip("If true, loose world items below the abyss surface are removed when the player dies.")]
    [SerializeField] private bool ClearLooseWorldItemsOnDeath = false;

    [Header("Timing")]
    [Tooltip("Delay before recovery is applied after death. Use this for fade-outs or death feedback.")]
    [SerializeField] private float RecoveryDelay = 0.25f;

    [Tooltip("If true, movement input is blocked while death recovery is pending.")]
    [SerializeField] private bool BlockMovementDuringRecovery = true;

    [Header("Debug")]
    [Tooltip("Logs recovery operations.")]
    [SerializeField] private bool DebugLogs = false;

    private Coroutine ActiveRecoveryRoutine;

    /// <summary>
    /// Resolves references and subscribes to health death events.
    /// </summary>
    private void OnEnable()
    {
        ResolveReferences();

        if (PlayerHealth != null)
        {
            PlayerHealth.OnDied += HandlePlayerDied;
        }
    }

    /// <summary>
    /// Unsubscribes from health death events.
    /// </summary>
    private void OnDisable()
    {
        if (PlayerHealth != null)
        {
            PlayerHealth.OnDied -= HandlePlayerDied;
        }
    }

    /// <summary>
    /// Starts the configured player recovery sequence.
    /// </summary>
    /// <param name="DeadPlayerHealth">Health component that died.</param>
    private void HandlePlayerDied(PlayerHealth DeadPlayerHealth)
    {
        if (ActiveRecoveryRoutine != null)
        {
            return;
        }

        ActiveRecoveryRoutine = StartCoroutine(RecoverPlayerRoutine());
    }

    /// <summary>
    /// Performs delayed recovery after player death.
    /// </summary>
    private IEnumerator RecoverPlayerRoutine()
    {
        ResolveReferences();

        if (PlayerController != null && BlockMovementDuringRecovery)
        {
            PlayerController.SetExternalMovementBlocked(true);
        }

        if (RecoveryDelay > 0f)
        {
            yield return new WaitForSeconds(RecoveryDelay);
        }

        if (AbyssField != null)
        {
            int RemovedCount = AbyssField.CleanRegisteredObjectsBelowSurface(
                ClearLooseOresOnDeath,
                ClearLooseMoneyOnDeath,
                ClearLooseWorldItemsOnDeath);
            Log("Abyss cleanup removed objects: " + RemovedCount);
        }

        RecoverElevatorIfNeeded();
        RecoverPlayerPose();

        if (PlayerHealth != null)
        {
            PlayerHealth.ReviveFull();
        }

        if (PlayerController != null && BlockMovementDuringRecovery)
        {
            PlayerController.SetExternalMovementBlocked(false);
        }

        ActiveRecoveryRoutine = null;
    }

    /// <summary>
    /// Moves the player controller to the configured respawn transform.
    /// </summary>
    private void RecoverPlayerPose()
    {
        if (PlayerController == null || PlayerRespawnPoint == null)
        {
            Log("Cannot recover player pose. Missing PlayerController or PlayerRespawnPoint.");
            return;
        }

        PlayerController.ApplySavedState(PlayerRespawnPoint.position, PlayerController.IsCrouching);
        Log("Player recovered to " + PlayerRespawnPoint.position);
    }

    /// <summary>
    /// Moves the elevator to the configured recovery distance when enabled.
    /// </summary>
    private void RecoverElevatorIfNeeded()
    {
        if (!RecoverElevator || ElevatorMotor == null)
        {
            return;
        }

        Quaternion RecoveryRotation = ElevatorRecoveryRotationReference != null
            ? ElevatorRecoveryRotationReference.rotation
            : ElevatorMotor.transform.rotation;

        ElevatorMotor.ApplySavedPose(ElevatorRecoveryDistance, RecoveryRotation);
        Log("Elevator recovered to distance " + ElevatorRecoveryDistance.ToString("0.##"));
    }

    /// <summary>
    /// Resolves missing scene references.
    /// </summary>
    private void ResolveReferences()
    {
        if (PlayerHealth == null)
        {
            PlayerHealth = FindFirstObjectByType<PlayerHealth>();
        }

        if (PlayerController == null)
        {
            PlayerController = FindFirstObjectByType<PlayerController>();
        }

        if (ElevatorMotor == null)
        {
            ElevatorMotor = FindFirstObjectByType<ElevatorPhysicalMotor>();
        }

        if (AbyssField == null)
        {
            AbyssField = FindFirstObjectByType<AbyssFieldController>();
        }
    }

    /// <summary>
    /// Logs debug messages if enabled.
    /// </summary>
    /// <param name="Message">Message body.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PlayerAbyssRecoveryService] " + Message, this);
    }
}

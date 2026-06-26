using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>
/// Handles player death recovery, world cleanup policy and laboratory respawn flow.
/// This service keeps death recovery separate from PlayerHealth so hazards only kill the player,
/// while this component decides what happens to loose objects, the elevator and the respawn sequence.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerAbyssRecoveryService : MonoBehaviour
{
    /// <summary>
    /// Unity event that sends the selected death message to future transition UI systems.
    /// </summary>
    [Serializable]
    public sealed class DeathMessageEvent : UnityEvent<string>
    {
    }

    [Header("Player References")]
    [Tooltip("Player health component that raises death events. If empty, the first PlayerHealth in the scene is used.")]
    [SerializeField] private PlayerHealth PlayerHealth;

    [Tooltip("Player controller to block and reposition during recovery. If empty, the first PlayerController in the scene is used.")]
    [SerializeField] private PlayerController PlayerController;

    [Tooltip("Safe laboratory respawn point used when the player dies.")]
    [SerializeField] private Transform PlayerRespawnPoint;

    [Header("Control Lock")]
    [Tooltip("If true, movement input is blocked while death recovery is running.")]
    [SerializeField] private bool BlockMovementDuringRecovery = true;

    [Tooltip("If true, camera look input is blocked while death recovery is running.")]
    [SerializeField] private bool BlockLookDuringRecovery = true;

    [Tooltip("If true, all currently held carryables are force-released before the world cleanup policy is applied.")]
    [SerializeField] private bool ReleaseHeldCarryablesOnDeath = true;

    [Header("Elevator Recovery")]
    [Tooltip("If true, the elevator motor is reset to the configured safe travel distance during death recovery.")]
    [SerializeField] private bool RecoverElevator = true;

    [Tooltip("Elevator motor moved back to the configured recovery distance.")]
    [SerializeField] private ElevatorPhysicalMotor ElevatorMotor;

    [Tooltip("Travel distance applied to the elevator during recovery. Usually 0 means laboratory/top.")]
    [SerializeField] private float ElevatorRecoveryDistance = 0f;

    [Tooltip("Optional transform used as the elevator recovery rotation. If empty, the current elevator rotation is preserved.")]
    [SerializeField] private Transform ElevatorRecoveryRotationReference;

    [Header("World Zones")]
    [Tooltip("If true, laboratory safety zones are discovered automatically when the recovery service resolves references.")]
    [SerializeField] private bool AutoDiscoverLaboratoryZones = true;

    [Tooltip("Explicit laboratory zones. Loose objects inside these volumes are preserved during death cleanup.")]
    [SerializeField] private List<WorldSafetyZone> LaboratoryZones = new();

    [Tooltip("If true, no object cleanup is performed when no laboratory zone exists. This prevents accidental total-world deletion during setup.")]
    [SerializeField] private bool TreatAllObjectsAsLaboratoryWhenNoZonesExist = true;

    [Header("Ore Death Policy")]
    [Tooltip("If true, loose ore pickups outside the laboratory are removed when the player dies.")]
    [SerializeField] private bool LoseOresOutsideLaboratoryOnDeath = true;

    [Tooltip("If true, ore pickups are returned to their pool when possible instead of being destroyed directly.")]
    [SerializeField] private bool ReturnLostOresToPoolWhenPossible = true;

    [Header("Money Death Policy")]
    [Tooltip("If true, loose physical money outside the laboratory is removed when the player dies. Wallet credits already collected are never touched.")]
    [SerializeField] private bool LoseMoneyOutsideLaboratoryOnDeath = true;

    [Tooltip("If true, money pickups are returned to their pool when possible instead of being destroyed directly.")]
    [SerializeField] private bool ReturnLostMoneyToPoolWhenPossible = true;

    [Header("World Item Death Policy")]
    [Tooltip("If true, loose world items outside the laboratory are moved to the configured item recovery point.")]
    [SerializeField] private bool RecoverWorldItemsOutsideLaboratoryOnDeath = true;

    [Tooltip("If true and item recovery is disabled, loose world items outside the laboratory are removed when the player dies.")]
    [SerializeField] private bool DestroyWorldItemsOutsideLaboratoryOnDeath = false;

    [Tooltip("If true, scene-placed world items that escaped the laboratory can also be moved to the recovery point.")]
    [SerializeField] private bool RecoverScenePlacedWorldItems = true;

    [Tooltip("If true, scene-placed world items may be hidden through their scene persistence component when item destruction is enabled.")]
    [SerializeField] private bool HideScenePlacedWorldItemsWhenDestroyed = false;

    [Header("World Item Recovery Placement")]
    [Tooltip("Point where recovered loose world items are placed. If empty, Player Respawn Point is used.")]
    [SerializeField] private Transform WorldItemRecoveryPoint;

    [Tooltip("Distance between recovered world items when several are moved to the same recovery point.")]
    [SerializeField] private float RecoveredWorldItemSpacing = 0.55f;

    [Tooltip("Maximum amount of recovered world items placed per row before starting a new row.")]
    [SerializeField] private int RecoveredWorldItemsPerRow = 4;

    [Tooltip("Vertical offset applied to recovered world items to avoid starting them partially inside the floor.")]
    [SerializeField] private float RecoveredWorldItemVerticalOffset = 0.15f;

    [Header("Legacy Abyss Cleanup Fallback")]
    [Tooltip("Optional abyss field kept for compatibility with the previous recovery flow. The main policy now uses laboratory zones.")]
    [SerializeField] private AbyssFieldController AbyssField;

    [Tooltip("If true, the old abyss-surface cleanup pass is also executed before the laboratory zone policy.")]
    [SerializeField] private bool RunAbyssSurfaceCleanupBeforeZonePolicy = false;

    [Tooltip("Legacy fallback: remove loose ores below the abyss surface when Run Abyss Surface Cleanup Before Zone Policy is enabled.")]
    [SerializeField] private bool ClearLooseOresOnDeath = true;

    [Tooltip("Legacy fallback: remove loose money below the abyss surface when Run Abyss Surface Cleanup Before Zone Policy is enabled.")]
    [SerializeField] private bool ClearLooseMoneyOnDeath = true;

    [Tooltip("Legacy fallback: remove loose world items below the abyss surface when Run Abyss Surface Cleanup Before Zone Policy is enabled.")]
    [SerializeField] private bool ClearLooseWorldItemsOnDeath = false;

    [Header("Transition Timing")]
    [Tooltip("Delay after death before world cleanup and respawn are applied. Use this as the future fade-in-to-black duration.")]
    [FormerlySerializedAs("RecoveryDelay")]
    [SerializeField] private float PreRecoveryDelay = 0.35f;

    [Tooltip("Delay after world cleanup and respawn before player control is restored. Use this as the future fade-out-from-black duration.")]
    [SerializeField] private float PostRecoveryDelay = 0.20f;

    [Header("Death Messages")]
    [Tooltip("Messages that can be sent to a future death transition UI while the screen fades to black.")]
    [SerializeField] private string[] DeathMessages =
    {
        "The darkness consumed you.",
        "You were swallowed by the abyss."
    };

    [Header("Transition Events")]
    [Tooltip("Invoked immediately when the death recovery sequence starts. Hook future fade-in effects here.")]
    [SerializeField] private UnityEvent OnRecoverySequenceStarted;

    [Tooltip("Invoked with the selected death message. Hook future title/subtitle UI here.")]
    [SerializeField] private DeathMessageEvent OnDeathMessageRequested;

    [Tooltip("Invoked right before the world cleanup and respawn mutation is applied.")]
    [SerializeField] private UnityEvent OnWorldRecoveryAboutToApply;

    [Tooltip("Invoked after world cleanup, elevator reset and player repositioning have been applied. Hook future fade-out effects here.")]
    [SerializeField] private UnityEvent OnWorldRecoveryApplied;

    [Tooltip("Invoked after the player has been revived and control has been restored.")]
    [SerializeField] private UnityEvent OnRecoverySequenceCompleted;

    [Header("Debug")]
    [Tooltip("Logs recovery operations and object policy counts.")]
    [SerializeField] private bool DebugLogs = false;

    private readonly List<OrePickup> OreBuffer = new();
    private readonly List<MoneyPickup> MoneyBuffer = new();
    private readonly List<WorldItem> WorldItemBuffer = new();
    private readonly List<WorldSafetyZone> DiscoveredZoneBuffer = new();
    private Coroutine ActiveRecoveryRoutine;
    private bool IsSubscribedToHealth;

    /// <summary>
    /// Resolves references and subscribes to health death events.
    /// </summary>
    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToHealth();
    }

    /// <summary>
    /// Unsubscribes from health death events.
    /// </summary>
    private void OnDisable()
    {
        UnsubscribeFromHealth();
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
        SetPlayerControlBlocked(true);

        string SelectedDeathMessage = SelectDeathMessage();
        OnRecoverySequenceStarted?.Invoke();
        OnDeathMessageRequested?.Invoke(SelectedDeathMessage);
        Log("Death recovery started. Message: " + SelectedDeathMessage);

        if (ReleaseHeldCarryablesOnDeath)
        {
            ReleaseHeldCarryables();
        }

        if (PreRecoveryDelay > 0f)
        {
            yield return new WaitForSeconds(PreRecoveryDelay);
        }

        OnWorldRecoveryAboutToApply?.Invoke();

        if (RunAbyssSurfaceCleanupBeforeZonePolicy && AbyssField != null)
        {
            int AbyssRemovedCount = AbyssField.CleanRegisteredObjectsBelowSurface(
                ClearLooseOresOnDeath,
                ClearLooseMoneyOnDeath,
                ClearLooseWorldItemsOnDeath);

            Log("Legacy abyss cleanup removed objects: " + AbyssRemovedCount);
        }

        ApplyDeathObjectPolicy();
        RecoverElevatorIfNeeded();
        RecoverPlayerPose();
        Physics.SyncTransforms();
        OnWorldRecoveryApplied?.Invoke();

        if (PostRecoveryDelay > 0f)
        {
            yield return new WaitForSeconds(PostRecoveryDelay);
        }

        if (PlayerHealth != null)
        {
            PlayerHealth.ReviveFull();
        }

        SetPlayerControlBlocked(false);
        OnRecoverySequenceCompleted?.Invoke();
        Log("Death recovery completed.");
        ActiveRecoveryRoutine = null;
    }

    /// <summary>
    /// Applies the loose object policy configured for player death.
    /// </summary>
    private void ApplyDeathObjectPolicy()
    {
        RuntimeWorldObjectRegistry Registry = ResolveRegistry();

        if (Registry == null)
        {
            Log("Skipped death object policy because no RuntimeWorldObjectRegistry exists.");
            return;
        }

        Registry.RebuildFromScene();

        int RemovedOres = LoseOresOutsideLaboratoryOnDeath ? RemoveOresOutsideLaboratory(Registry) : 0;
        int RemovedMoney = LoseMoneyOutsideLaboratoryOnDeath ? RemoveMoneyOutsideLaboratory(Registry) : 0;
        int RecoveredWorldItems = 0;
        int DestroyedWorldItems = 0;

        if (RecoverWorldItemsOutsideLaboratoryOnDeath)
        {
            RecoveredWorldItems = RecoverWorldItemsOutsideLaboratory(Registry);
        }
        else if (DestroyWorldItemsOutsideLaboratoryOnDeath)
        {
            DestroyedWorldItems = DestroyWorldItemsOutsideLaboratory(Registry);
        }

        Log(
            "Death object policy applied. Ores removed: " + RemovedOres +
            " | Money removed: " + RemovedMoney +
            " | World items recovered: " + RecoveredWorldItems +
            " | World items destroyed: " + DestroyedWorldItems);
    }

    /// <summary>
    /// Removes loose ore pickups outside the laboratory zones.
    /// </summary>
    /// <param name="Registry">Runtime registry used to enumerate active ore pickups.</param>
    /// <returns>Amount of removed ore pickups.</returns>
    private int RemoveOresOutsideLaboratory(RuntimeWorldObjectRegistry Registry)
    {
        Registry.CopyActiveOrePickups(OreBuffer);
        int RemovedCount = 0;

        for (int Index = 0; Index < OreBuffer.Count; Index++)
        {
            OrePickup Pickup = OreBuffer[Index];

            if (Pickup == null || Pickup.GetOreItemData() == null)
            {
                continue;
            }

            Transform RuntimeRoot = Pickup.GetRuntimeRoot();

            if (RuntimeRoot == null || !IsOutsideLaboratory(RuntimeRoot.position))
            {
                continue;
            }

            RemoveOrePickup(Pickup);
            RemovedCount++;
        }

        OreBuffer.Clear();
        return RemovedCount;
    }

    /// <summary>
    /// Removes loose money pickups outside the laboratory zones.
    /// </summary>
    /// <param name="Registry">Runtime registry used to enumerate active money pickups.</param>
    /// <returns>Amount of removed money pickups.</returns>
    private int RemoveMoneyOutsideLaboratory(RuntimeWorldObjectRegistry Registry)
    {
        Registry.CopyActiveMoneyPickups(MoneyBuffer);
        int RemovedCount = 0;

        for (int Index = 0; Index < MoneyBuffer.Count; Index++)
        {
            MoneyPickup Pickup = MoneyBuffer[Index];

            if (Pickup == null || Pickup.GetAmount() <= 0f)
            {
                continue;
            }

            Transform RuntimeRoot = Pickup.GetRuntimeRoot();

            if (RuntimeRoot == null || !IsOutsideLaboratory(RuntimeRoot.position))
            {
                continue;
            }

            RemoveMoneyPickup(Pickup);
            RemovedCount++;
        }

        MoneyBuffer.Clear();
        return RemovedCount;
    }

    /// <summary>
    /// Moves loose world items outside the laboratory back to the configured recovery point.
    /// </summary>
    /// <param name="Registry">Runtime registry used to enumerate active world items.</param>
    /// <returns>Amount of recovered world items.</returns>
    private int RecoverWorldItemsOutsideLaboratory(RuntimeWorldObjectRegistry Registry)
    {
        Registry.CopyActiveWorldItems(WorldItemBuffer);
        int RecoveredCount = 0;

        for (int Index = 0; Index < WorldItemBuffer.Count; Index++)
        {
            WorldItem Item = WorldItemBuffer[Index];

            if (!CanAffectWorldItem(Item, Registry))
            {
                continue;
            }

            if (!IsOutsideLaboratory(Item.GetWorldPosition()))
            {
                continue;
            }

            RecoverWorldItem(Item, RecoveredCount);
            RecoveredCount++;
        }

        WorldItemBuffer.Clear();
        return RecoveredCount;
    }

    /// <summary>
    /// Destroys or hides loose world items outside the laboratory when the destructive policy is enabled.
    /// </summary>
    /// <param name="Registry">Runtime registry used to enumerate active world items.</param>
    /// <returns>Amount of removed world items.</returns>
    private int DestroyWorldItemsOutsideLaboratory(RuntimeWorldObjectRegistry Registry)
    {
        Registry.CopyActiveWorldItems(WorldItemBuffer);
        int RemovedCount = 0;

        for (int Index = 0; Index < WorldItemBuffer.Count; Index++)
        {
            WorldItem Item = WorldItemBuffer[Index];

            if (!CanAffectWorldItem(Item, Registry))
            {
                continue;
            }

            if (!IsOutsideLaboratory(Item.GetWorldPosition()))
            {
                continue;
            }

            RemoveWorldItem(Item);
            RemovedCount++;
        }

        WorldItemBuffer.Clear();
        return RemovedCount;
    }

    /// <summary>
    /// Returns whether a world item is eligible for death recovery policy.
    /// Installed machines are not WorldItem objects and therefore never enter this path.
    /// </summary>
    /// <param name="Item">World item being evaluated.</param>
    /// <param name="Registry">Registry used to classify scene-placed items.</param>
    /// <returns>True when this world item can be recovered or destroyed by policy.</returns>
    private bool CanAffectWorldItem(WorldItem Item, RuntimeWorldObjectRegistry Registry)
    {
        if (Item == null || !Item.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (Registry != null && Registry.IsScenePlacedWorldItem(Item) && !RecoverScenePlacedWorldItems)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Removes one ore pickup according to the configured pooling preference.
    /// </summary>
    /// <param name="Pickup">Ore pickup to remove.</param>
    private void RemoveOrePickup(OrePickup Pickup)
    {
        if (Pickup == null)
        {
            return;
        }

        if (ReturnLostOresToPoolWhenPossible && Pickup.ReturnToPool())
        {
            return;
        }

        Transform RuntimeRoot = Pickup.GetRuntimeRoot();

        if (RuntimeRoot != null)
        {
            Destroy(RuntimeRoot.gameObject);
        }
    }

    /// <summary>
    /// Removes one money pickup according to the configured pooling preference.
    /// </summary>
    /// <param name="Pickup">Money pickup to remove.</param>
    private void RemoveMoneyPickup(MoneyPickup Pickup)
    {
        if (Pickup == null)
        {
            return;
        }

        if (ReturnLostMoneyToPoolWhenPossible && Pickup.ReturnToPool())
        {
            return;
        }

        Transform RuntimeRoot = Pickup.GetRuntimeRoot();

        if (RuntimeRoot != null)
        {
            Destroy(RuntimeRoot.gameObject);
        }
    }

    /// <summary>
    /// Moves one world item to the laboratory recovery point and clears transient physics state.
    /// </summary>
    /// <param name="Item">World item to recover.</param>
    /// <param name="RecoveryIndex">Sequential index used to offset multiple recovered items.</param>
    private void RecoverWorldItem(WorldItem Item, int RecoveryIndex)
    {
        if (Item == null)
        {
            return;
        }

        Transform RecoveryPoint = WorldItemRecoveryPoint != null ? WorldItemRecoveryPoint : PlayerRespawnPoint;
        Vector3 TargetPosition = RecoveryPoint != null ? RecoveryPoint.position : transform.position;
        Quaternion TargetRotation = RecoveryPoint != null ? RecoveryPoint.rotation : Quaternion.identity;
        TargetPosition += CalculateRecoveredWorldItemOffset(RecoveryIndex);

        Item.PrepareForInventoryPickup();

        Transform PhysicsRoot = Item.GetPhysicsRoot();

        if (PhysicsRoot == null)
        {
            PhysicsRoot = Item.transform;
        }

        PhysicsRoot.SetParent(null, true);

        Rigidbody RigidbodyComponent = Item.GetRigidbody();

        if (RigidbodyComponent != null)
        {
            RigidbodyComponent.position = TargetPosition;
            RigidbodyComponent.rotation = TargetRotation;
            RigidbodyComponent.linearVelocity = Vector3.zero;
            RigidbodyComponent.angularVelocity = Vector3.zero;
            RigidbodyComponent.Sleep();
        }

        PhysicsRoot.SetPositionAndRotation(TargetPosition, TargetRotation);
        Item.ApplyPhysicsState(Vector3.zero, Vector3.zero, false);
        Log("Recovered world item to laboratory: " + Item.name);
    }

    /// <summary>
    /// Removes one world item according to scene persistence rules.
    /// </summary>
    /// <param name="Item">World item to remove.</param>
    private void RemoveWorldItem(WorldItem Item)
    {
        if (Item == null)
        {
            return;
        }

        Item.PrepareForInventoryPickup();

        ScenePlacedWorldItemPersistence Persistence = Item.GetComponentInParent<ScenePlacedWorldItemPersistence>(true);

        if (Persistence != null)
        {
            if (HideScenePlacedWorldItemsWhenDestroyed && Persistence.ShouldPreserveAsScenePlacedItem())
            {
                Persistence.SetPresent(false);
                return;
            }

            Destroy(Persistence.gameObject);
            return;
        }

        GameObject RemovalRoot = Item.GetRuntimeRemovalRoot();

        if (RemovalRoot != null)
        {
            Destroy(RemovalRoot);
        }
    }

    /// <summary>
    /// Calculates a stable grid offset for recovered world items so they do not spawn on top of each other.
    /// </summary>
    /// <param name="RecoveryIndex">Sequential recovered item index.</param>
    /// <returns>World-space offset from the recovery point.</returns>
    private Vector3 CalculateRecoveredWorldItemOffset(int RecoveryIndex)
    {
        int SafeItemsPerRow = Mathf.Max(1, RecoveredWorldItemsPerRow);
        float SafeSpacing = Mathf.Max(0f, RecoveredWorldItemSpacing);
        int Column = RecoveryIndex % SafeItemsPerRow;
        int Row = RecoveryIndex / SafeItemsPerRow;
        float CenterOffset = (SafeItemsPerRow - 1) * 0.5f;
        Vector3 Right = WorldItemRecoveryPoint != null ? WorldItemRecoveryPoint.right : transform.right;
        Vector3 Forward = WorldItemRecoveryPoint != null ? WorldItemRecoveryPoint.forward : transform.forward;

        return Right * ((Column - CenterOffset) * SafeSpacing) +
               Forward * (Row * SafeSpacing) +
               Vector3.up * Mathf.Max(0f, RecoveredWorldItemVerticalOffset);
    }

    /// <summary>
    /// Force-releases all carryables currently held by the player interaction system.
    /// </summary>
    private void ReleaseHeldCarryables()
    {
        PhysicsCarryable[] Carryables = FindObjectsByType<PhysicsCarryable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int ReleasedCount = 0;

        for (int Index = 0; Index < Carryables.Length; Index++)
        {
            PhysicsCarryable Carryable = Carryables[Index];

            if (Carryable == null || !Carryable.GetIsHeld())
            {
                continue;
            }

            Carryable.EndHold();
            ReleasedCount++;
        }

        Log("Released held carryables: " + ReleasedCount);
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

        PlayerController.ApplySavedState(PlayerRespawnPoint.position, false);
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
    /// Blocks or restores player movement and look input.
    /// </summary>
    /// <param name="IsBlocked">True to block control, false to restore it.</param>
    private void SetPlayerControlBlocked(bool IsBlocked)
    {
        if (PlayerController == null)
        {
            return;
        }

        if (BlockMovementDuringRecovery)
        {
            PlayerController.SetExternalMovementBlocked(IsBlocked);
        }

        if (BlockLookDuringRecovery)
        {
            PlayerController.SetExternalLookBlocked(IsBlocked);
        }
    }

    /// <summary>
    /// Returns whether a world position is outside every configured laboratory safety zone.
    /// </summary>
    /// <param name="WorldPosition">Position to classify.</param>
    /// <returns>True when the position should be treated as outside the laboratory.</returns>
    private bool IsOutsideLaboratory(Vector3 WorldPosition)
    {
        int ValidLaboratoryZoneCount = GetValidLaboratoryZoneCount();

        if (ValidLaboratoryZoneCount <= 0)
        {
            return !TreatAllObjectsAsLaboratoryWhenNoZonesExist;
        }

        return !IsInsideAnyLaboratoryZone(WorldPosition);
    }

    /// <summary>
    /// Returns whether a world position is inside at least one laboratory safety zone.
    /// </summary>
    /// <param name="WorldPosition">Position to classify.</param>
    /// <returns>True when the position is inside a laboratory zone.</returns>
    private bool IsInsideAnyLaboratoryZone(Vector3 WorldPosition)
    {
        for (int Index = 0; Index < LaboratoryZones.Count; Index++)
        {
            WorldSafetyZone Zone = LaboratoryZones[Index];

            if (Zone == null || Zone.GetZoneKind() != WorldSafetyZone.SafetyZoneKind.Laboratory)
            {
                continue;
            }

            if (Zone.ContainsWorldPosition(WorldPosition))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Counts valid laboratory zones currently configured.
    /// </summary>
    /// <returns>Number of active laboratory zones available to the policy.</returns>
    private int GetValidLaboratoryZoneCount()
    {
        int Count = 0;

        for (int Index = LaboratoryZones.Count - 1; Index >= 0; Index--)
        {
            WorldSafetyZone Zone = LaboratoryZones[Index];

            if (Zone == null)
            {
                LaboratoryZones.RemoveAt(Index);
                continue;
            }

            if (Zone.GetZoneKind() == WorldSafetyZone.SafetyZoneKind.Laboratory && Zone.GetIsEnabledForPolicy())
            {
                Count++;
            }
        }

        return Count;
    }

    /// <summary>
    /// Selects a death message for the current recovery sequence.
    /// </summary>
    /// <returns>Selected death message, or an empty string.</returns>
    private string SelectDeathMessage()
    {
        if (DeathMessages == null || DeathMessages.Length <= 0)
        {
            return string.Empty;
        }

        int StartIndex = UnityEngine.Random.Range(0, DeathMessages.Length);

        for (int Offset = 0; Offset < DeathMessages.Length; Offset++)
        {
            string Message = DeathMessages[(StartIndex + Offset) % DeathMessages.Length];

            if (!string.IsNullOrWhiteSpace(Message))
            {
                return Message;
            }
        }

        return string.Empty;
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

        if (WorldItemRecoveryPoint == null)
        {
            WorldItemRecoveryPoint = PlayerRespawnPoint;
        }

        if (AutoDiscoverLaboratoryZones)
        {
            DiscoverLaboratoryZones();
        }
    }

    /// <summary>
    /// Discovers laboratory safety zones in the active scene and registers them without duplicating explicit assignments.
    /// </summary>
    private void DiscoverLaboratoryZones()
    {
        WorldSafetyZone[] Zones = FindObjectsByType<WorldSafetyZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        DiscoveredZoneBuffer.Clear();

        for (int Index = 0; Index < Zones.Length; Index++)
        {
            WorldSafetyZone Zone = Zones[Index];

            if (Zone == null || Zone.GetZoneKind() != WorldSafetyZone.SafetyZoneKind.Laboratory)
            {
                continue;
            }

            DiscoveredZoneBuffer.Add(Zone);
        }

        for (int Index = 0; Index < DiscoveredZoneBuffer.Count; Index++)
        {
            WorldSafetyZone Zone = DiscoveredZoneBuffer[Index];

            if (!LaboratoryZones.Contains(Zone))
            {
                LaboratoryZones.Add(Zone);
            }
        }

        DiscoveredZoneBuffer.Clear();
    }

    /// <summary>
    /// Resolves the runtime object registry.
    /// </summary>
    /// <returns>Registry instance, or null.</returns>
    private RuntimeWorldObjectRegistry ResolveRegistry()
    {
        RuntimeWorldObjectRegistry Registry = RuntimeWorldObjectRegistry.Instance;

        if (Registry == null)
        {
            Registry = FindFirstObjectByType<RuntimeWorldObjectRegistry>();
        }

        return Registry;
    }

    /// <summary>
    /// Subscribes to player health death events once.
    /// </summary>
    private void SubscribeToHealth()
    {
        if (PlayerHealth == null || IsSubscribedToHealth)
        {
            return;
        }

        PlayerHealth.OnDied += HandlePlayerDied;
        IsSubscribedToHealth = true;
    }

    /// <summary>
    /// Unsubscribes from player health death events once.
    /// </summary>
    private void UnsubscribeFromHealth()
    {
        if (PlayerHealth == null || !IsSubscribedToHealth)
        {
            return;
        }

        PlayerHealth.OnDied -= HandlePlayerDied;
        IsSubscribedToHealth = false;
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

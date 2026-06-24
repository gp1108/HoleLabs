using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls the dense gas or darkness field below the elevator.
/// The field tracks the deepest safe progress reached by the elevator, damages the player near the surface,
/// kills the player when they sink too far and removes loose physical objects that fall into the abyss.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class AbyssFieldController : MonoBehaviour
{
    private enum SurfaceTrackingMode
    {
        /// <summary>
        /// The abyss surface only moves downward as the elevator clears deeper space.
        /// </summary>
        KeepDeepestClearedProgress = 0,

        /// <summary>
        /// The abyss surface follows the elevator both downward and upward.
        /// </summary>
        FollowElevatorBothDirections = 1
    }

    private enum PickupDisposalMode
    {
        /// <summary>
        /// Attempt to return pooled pickups to their owner pool before falling back to destruction.
        /// </summary>
        ReturnToPoolWhenPossible = 0,

        /// <summary>
        /// Destroy the runtime root directly.
        /// </summary>
        DestroyRuntimeRoot = 1
    }

    [Header("Elevator Tracking")]
    [Tooltip("Elevator motor used to determine when the abyss starts and how far it has been cleared.")]
    [SerializeField] private ElevatorPhysicalMotor ElevatorMotor;

    [Tooltip("Reference transform used for the abyss surface. Usually the physical elevator root.")]
    [SerializeField] private Transform ElevatorReference;

    [Tooltip("Minimum elevator travel distance required before the abyss field becomes active.")]
    [SerializeField] private float MinimumElevatorDistanceToActivate = 0f;

    [Tooltip("Vertical space left between the elevator and the top surface of the gas or darkness.")]
    [SerializeField] private float SurfaceOffsetBelowElevator = 4f;

    [Tooltip("Controls whether the abyss surface remains at the deepest cleared position or follows the elevator upward again.")]
    [SerializeField] private SurfaceTrackingMode TrackingMode = SurfaceTrackingMode.KeepDeepestClearedProgress;

    [Tooltip("If true, the field activates permanently once the elevator reaches the activation distance.")]
    [SerializeField] private bool StayActivatedAfterFirstActivation = true;

    [Header("Field Volume")]
    [Tooltip("Horizontal reference used to position the abyss volume. If empty, the current object position is used.")]
    [SerializeField] private Transform HorizontalReference;

    [Tooltip("Horizontal size of the abyss box in world units.")]
    [SerializeField] private Vector2 HorizontalSize = new Vector2(80f, 80f);

    [Tooltip("Vertical height of the abyss kill volume below the current surface.")]
    [SerializeField] private float AbyssDepth = 300f;

    [Tooltip("Optional root that represents the visible gas/darkness surface. It is positioned at the current surface height.")]
    [SerializeField] private Transform VisualSurfaceRoot;

    [Tooltip("If true, Visual Surface Root is activated only while the abyss field is active.")]
    [SerializeField] private bool ToggleVisualSurfaceWithActivation = true;

    [Header("Player Damage")]
    [Tooltip("Player health damaged by shallow abyss immersion.")]
    [SerializeField] private PlayerHealth PlayerHealth;

    [Tooltip("Transform used to calculate player immersion depth. If empty, PlayerHealth transform is used.")]
    [SerializeField] private Transform PlayerDepthReference;

    [Tooltip("Immersion depth below the surface before damage starts.")]
    [SerializeField] private float DamageStartDepth = 0.25f;

    [Tooltip("Immersion depth below the surface that instantly kills the player.")]
    [SerializeField] private float DeathDepth = 2.5f;

    [Tooltip("Damage per second while the player is inside the damage band.")]
    [SerializeField] private float DamagePerSecond = 20f;

    [Tooltip("If true, shallow immersion damage is applied before the instant death depth is reached.")]
    [SerializeField] private bool ApplyShallowDamage = true;

    [Tooltip("Minimum time between repeated player damage feedback events. Damage itself is still continuous.")]
    [SerializeField] private float DamageFeedbackInterval = 0.5f;

    [Header("Object Disposal")]
    [Tooltip("If true, loose physical objects entering the abyss are removed according to their type.")]
    [SerializeField] private bool DestroyObjectsEnteringAbyss = true;

    [Tooltip("Layers allowed to be removed by the abyss. Keep this limited to ores, money and world item layers.")]
    [SerializeField] private LayerMask DisposableObjectLayers = ~0;

    [Tooltip("How ore pickups are disposed when they fall into the abyss.")]
    [SerializeField] private PickupDisposalMode OreDisposalMode = PickupDisposalMode.ReturnToPoolWhenPossible;

    [Tooltip("How money pickups are disposed if they ever fall into the abyss.")]
    [SerializeField] private PickupDisposalMode MoneyDisposalMode = PickupDisposalMode.ReturnToPoolWhenPossible;

    [Tooltip("If true, scene-placed world item persistence wrappers are hidden through SetPresent(false) instead of directly destroyed.")]
    [SerializeField] private bool PreserveSceneWorldItemAnchors = true;

    [Header("Feedback")]
    [Tooltip("Optional feedback emitter for abyss damage, object deletion and player death events.")]
    [SerializeField] private GameFeedbackEmitter FeedbackEmitter;

    [Header("Debug")]
    [Tooltip("Draws the abyss surface and volume in the Scene view.")]
    [SerializeField] private bool DrawDebugGizmos = true;

    [Tooltip("Logs abyss activation, object disposal and player damage events.")]
    [SerializeField] private bool DebugLogs = false;

    private BoxCollider AbyssCollider;
    private bool HasBeenActivated;
    private float CurrentSurfaceY;
    private readonly List<OrePickup> OreBuffer = new();
    private readonly List<MoneyPickup> MoneyBuffer = new();
    private readonly List<WorldItem> WorldItemBuffer = new();
    private float DamageFeedbackTimer;

    /// <summary>
    /// Gets whether the abyss field is currently active.
    /// </summary>
    public bool IsActive => HasBeenActivated && GetActivationConditionMet();

    /// <summary>
    /// Gets the current world Y position of the abyss surface.
    /// </summary>
    public float SurfaceY => CurrentSurfaceY;

    /// <summary>
    /// Initializes references and field dimensions.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        CurrentSurfaceY = CalculateTargetSurfaceY();
        RefreshFieldTransform();
        RefreshVisualState();
    }

    /// <summary>
    /// Updates abyss progress, damage and visual state.
    /// </summary>
    private void Update()
    {
        ResolveReferences();
        UpdateActivationAndSurface();
        RefreshFieldTransform();
        RefreshVisualState();
        UpdatePlayerDamage();
    }

    /// <summary>
    /// Removes objects that enter the abyss trigger.
    /// </summary>
    /// <param name="Other">Collider entering the abyss field.</param>
    private void OnTriggerEnter(Collider Other)
    {
        TryDisposeColliderObject(Other);
    }

    /// <summary>
    /// Removes objects that remain in the abyss trigger. This catches objects enabled or teleported inside it.
    /// </summary>
    /// <param name="Other">Collider staying inside the abyss field.</param>
    private void OnTriggerStay(Collider Other)
    {
        TryDisposeColliderObject(Other);
    }

    /// <summary>
    /// Gets a save payload for this abyss field.
    /// </summary>
    /// <returns>Serializable abyss state.</returns>
    public AbyssFieldSaveData CreateSaveSnapshot()
    {
        return new AbyssFieldSaveData(HasBeenActivated, CurrentSurfaceY);
    }

    /// <summary>
    /// Restores this abyss field from save data.
    /// </summary>
    /// <param name="SaveData">Saved abyss state.</param>
    public void ApplySaveState(AbyssFieldSaveData SaveData)
    {
        if (SaveData == null)
        {
            return;
        }

        HasBeenActivated = SaveData.GetHasBeenActivated();
        CurrentSurfaceY = SaveData.GetCurrentSurfaceY();
        RefreshFieldTransform();
        RefreshVisualState();
    }

    /// <summary>
    /// Removes registered runtime objects currently below the abyss surface.
    /// Used by player recovery to clear the hole after death.
    /// </summary>
    /// <param name="CleanOres">Whether active ore pickups should be removed.</param>
    /// <param name="CleanMoney">Whether active money pickups should be removed.</param>
    /// <param name="CleanWorldItems">Whether active world items should be removed.</param>
    /// <returns>Number of removed objects.</returns>
    public int CleanRegisteredObjectsBelowSurface(bool CleanOres, bool CleanMoney, bool CleanWorldItems)
    {
        RuntimeWorldObjectRegistry Registry = RuntimeWorldObjectRegistry.Instance;

        if (Registry == null)
        {
            Registry = FindFirstObjectByType<RuntimeWorldObjectRegistry>();
        }

        if (Registry == null)
        {
            return 0;
        }

        Registry.RebuildFromScene();
        int RemovedCount = 0;

        if (CleanOres)
        {
            Registry.CopyActiveOrePickups(OreBuffer);

            for (int Index = 0; Index < OreBuffer.Count; Index++)
            {
                OrePickup Pickup = OreBuffer[Index];

                if (Pickup == null || Pickup.GetRuntimeRoot().position.y > CurrentSurfaceY)
                {
                    continue;
                }

                DisposeOrePickup(Pickup);
                RemovedCount++;
            }

            OreBuffer.Clear();
        }

        if (CleanMoney)
        {
            Registry.CopyActiveMoneyPickups(MoneyBuffer);

            for (int Index = 0; Index < MoneyBuffer.Count; Index++)
            {
                MoneyPickup Pickup = MoneyBuffer[Index];

                if (Pickup == null || Pickup.GetRuntimeRoot().position.y > CurrentSurfaceY)
                {
                    continue;
                }

                DisposeMoneyPickup(Pickup);
                RemovedCount++;
            }

            MoneyBuffer.Clear();
        }

        if (CleanWorldItems)
        {
            Registry.CopyActiveWorldItems(WorldItemBuffer);

            for (int Index = 0; Index < WorldItemBuffer.Count; Index++)
            {
                WorldItem Item = WorldItemBuffer[Index];

                if (Item == null || Item.transform.position.y > CurrentSurfaceY)
                {
                    continue;
                }

                DisposeWorldItem(Item);
                RemovedCount++;
            }

            WorldItemBuffer.Clear();
        }

        return RemovedCount;
    }

    /// <summary>
    /// Updates activation and current surface height based on elevator progress.
    /// </summary>
    private void UpdateActivationAndSurface()
    {
        bool ActivationMet = GetActivationConditionMet();

        if (ActivationMet && !HasBeenActivated)
        {
            HasBeenActivated = true;
            PlayFeedback(GameFeedbackEventIds.AbyssActivated, new Vector3(transform.position.x, CurrentSurfaceY, transform.position.z), Vector3.up);
        }

        if (!HasBeenActivated)
        {
            return;
        }

        float TargetSurfaceY = CalculateTargetSurfaceY();

        if (TrackingMode == SurfaceTrackingMode.FollowElevatorBothDirections)
        {
            CurrentSurfaceY = TargetSurfaceY;
            return;
        }

        CurrentSurfaceY = Mathf.Min(CurrentSurfaceY, TargetSurfaceY);
    }

    /// <summary>
    /// Gets whether elevator progress currently allows the field to exist.
    /// </summary>
    private bool GetActivationConditionMet()
    {
        if (ElevatorMotor == null)
        {
            return true;
        }

        if (StayActivatedAfterFirstActivation && HasBeenActivated)
        {
            return true;
        }

        return ElevatorMotor.GetCurrentDistance() >= Mathf.Max(0f, MinimumElevatorDistanceToActivate);
    }

    /// <summary>
    /// Calculates target surface height from the elevator reference.
    /// </summary>
    private float CalculateTargetSurfaceY()
    {
        Transform Reference = ElevatorReference != null ? ElevatorReference : transform;
        return Reference.position.y - Mathf.Max(0f, SurfaceOffsetBelowElevator);
    }

    /// <summary>
    /// Positions and sizes the abyss trigger volume.
    /// </summary>
    private void RefreshFieldTransform()
    {
        if (AbyssCollider == null)
        {
            return;
        }

        Vector3 HorizontalPosition = HorizontalReference != null ? HorizontalReference.position : transform.position;
        float SafeDepth = Mathf.Max(1f, AbyssDepth);
        transform.position = new Vector3(HorizontalPosition.x, CurrentSurfaceY - SafeDepth * 0.5f, HorizontalPosition.z);
        transform.rotation = Quaternion.identity;

        AbyssCollider.isTrigger = true;
        AbyssCollider.center = Vector3.zero;
        AbyssCollider.size = new Vector3(
            Mathf.Max(1f, HorizontalSize.x),
            SafeDepth,
            Mathf.Max(1f, HorizontalSize.y));
    }

    /// <summary>
    /// Updates visual surface visibility and position.
    /// </summary>
    private void RefreshVisualState()
    {
        if (VisualSurfaceRoot == null)
        {
            return;
        }

        if (ToggleVisualSurfaceWithActivation)
        {
            VisualSurfaceRoot.gameObject.SetActive(IsActive);
        }

        Vector3 HorizontalPosition = HorizontalReference != null ? HorizontalReference.position : transform.position;
        VisualSurfaceRoot.position = new Vector3(HorizontalPosition.x, CurrentSurfaceY, HorizontalPosition.z);
    }

    /// <summary>
    /// Applies shallow damage or instant death to the player based on immersion depth.
    /// </summary>
    private void UpdatePlayerDamage()
    {
        if (!IsActive || PlayerHealth == null || PlayerHealth.IsDead)
        {
            return;
        }

        Transform DepthReference = PlayerDepthReference != null ? PlayerDepthReference : PlayerHealth.transform;
        float ImmersionDepth = CurrentSurfaceY - DepthReference.position.y;

        if (ImmersionDepth < DamageStartDepth)
        {
            return;
        }

        if (ImmersionDepth >= Mathf.Max(DamageStartDepth, DeathDepth))
        {
            PlayFeedback(GameFeedbackEventIds.AbyssPlayerDeath, DepthReference.position, Vector3.up);
            PlayerHealth.Kill(this);
            return;
        }

        if (!ApplyShallowDamage || DamagePerSecond <= 0f)
        {
            return;
        }

        PlayerHealth.ApplyDamage(DamagePerSecond * Time.deltaTime, this);

        DamageFeedbackTimer -= Time.deltaTime;

        if (DamageFeedbackTimer <= 0f)
        {
            DamageFeedbackTimer = Mathf.Max(0.05f, DamageFeedbackInterval);
            PlayFeedback(GameFeedbackEventIds.AbyssPlayerDamage, DepthReference.position, Vector3.up);
        }
    }

    /// <summary>
    /// Attempts to remove an object represented by a collider inside the abyss.
    /// </summary>
    /// <param name="Other">Collider to inspect.</param>
    private void TryDisposeColliderObject(Collider Other)
    {
        if (!IsActive || !DestroyObjectsEnteringAbyss || Other == null)
        {
            return;
        }

        if ((DisposableObjectLayers.value & (1 << Other.gameObject.layer)) == 0)
        {
            return;
        }

        OrePickup OrePickup = Other.GetComponentInParent<OrePickup>();

        if (OrePickup != null && OrePickup.GetOreItemData() != null)
        {
            DisposeOrePickup(OrePickup);
            return;
        }

        MoneyPickup MoneyPickup = Other.GetComponentInParent<MoneyPickup>();

        if (MoneyPickup != null && MoneyPickup.GetAmount() > 0f)
        {
            DisposeMoneyPickup(MoneyPickup);
            return;
        }

        WorldItem WorldItem = Other.GetComponentInParent<WorldItem>();

        if (WorldItem != null)
        {
            DisposeWorldItem(WorldItem);
        }
    }

    /// <summary>
    /// Disposes one ore pickup according to the configured abyss mode.
    /// </summary>
    /// <param name="Pickup">Ore pickup to dispose.</param>
    private void DisposeOrePickup(OrePickup Pickup)
    {
        if (Pickup == null)
        {
            return;
        }

        RuntimeWorldObjectRegistry.UnregisterOrePickup(Pickup);
        PlayFeedback(GameFeedbackEventIds.AbyssObjectDestroyed, Pickup.GetRuntimeRoot().position, Vector3.up);

        if (OreDisposalMode == PickupDisposalMode.ReturnToPoolWhenPossible && Pickup.ReturnToPool())
        {
            return;
        }

        Destroy(Pickup.GetRuntimeRoot().gameObject);
    }

    /// <summary>
    /// Disposes one money pickup according to the configured abyss mode.
    /// </summary>
    /// <param name="Pickup">Money pickup to dispose.</param>
    private void DisposeMoneyPickup(MoneyPickup Pickup)
    {
        if (Pickup == null)
        {
            return;
        }

        RuntimeWorldObjectRegistry.UnregisterMoneyPickup(Pickup);
        PlayFeedback(GameFeedbackEventIds.AbyssObjectDestroyed, Pickup.GetRuntimeRoot().position, Vector3.up);

        if (MoneyDisposalMode == PickupDisposalMode.ReturnToPoolWhenPossible && Pickup.ReturnToPool())
        {
            return;
        }

        Destroy(Pickup.GetRuntimeRoot().gameObject);
    }

    /// <summary>
    /// Disposes one world item when it falls into the abyss.
    /// </summary>
    /// <param name="Item">World item to dispose.</param>
    private void DisposeWorldItem(WorldItem Item)
    {
        if (Item == null)
        {
            return;
        }

        RuntimeWorldObjectRegistry.UnregisterWorldItem(Item);
        PlayFeedback(GameFeedbackEventIds.AbyssObjectDestroyed, Item.transform.position, Vector3.up);

        ScenePlacedWorldItemPersistence Persistence = Item.GetComponentInParent<ScenePlacedWorldItemPersistence>(true);

        if (Persistence != null)
        {
            if (PreserveSceneWorldItemAnchors && Persistence.ShouldPreserveAsScenePlacedItem())
            {
                Persistence.SetPresent(false);
                return;
            }

            Destroy(Persistence.gameObject);
            return;
        }

        Destroy(Item.gameObject);
    }

    /// <summary>
    /// Plays a feedback event if an emitter is assigned.
    /// </summary>
    /// <param name="EventId">Feedback event identifier.</param>
    /// <param name="Position">Feedback world position.</param>
    /// <param name="Normal">Feedback surface normal.</param>
    private void PlayFeedback(string EventId, Vector3 Position, Vector3 Normal)
    {
        if (FeedbackEmitter == null || string.IsNullOrWhiteSpace(EventId))
        {
            return;
        }

        FeedbackEmitter.Play(EventId, GameFeedbackContext.FromPosition(Position, transform, 1f));
    }

    /// <summary>
    /// Resolves optional scene references.
    /// </summary>
    private void ResolveReferences()
    {
        if (AbyssCollider == null)
        {
            AbyssCollider = GetComponent<BoxCollider>();
        }

        if (ElevatorMotor == null)
        {
            ElevatorMotor = FindFirstObjectByType<ElevatorPhysicalMotor>();
        }

        if (ElevatorReference == null && ElevatorMotor != null)
        {
            ElevatorReference = ElevatorMotor.transform;
        }

        if (PlayerHealth == null)
        {
            PlayerHealth = FindFirstObjectByType<PlayerHealth>();
        }

        if (FeedbackEmitter == null)
        {
            FeedbackEmitter = GetComponent<GameFeedbackEmitter>();
        }
    }

    /// <summary>
    /// Draws the abyss volume and top surface for editor tuning.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!DrawDebugGizmos)
        {
            return;
        }

        Gizmos.color = new Color(0.2f, 0.1f, 0.6f, 0.25f);
        Vector3 Center = transform.position;
        Vector3 Size = new Vector3(Mathf.Max(1f, HorizontalSize.x), Mathf.Max(1f, AbyssDepth), Mathf.Max(1f, HorizontalSize.y));
        Gizmos.DrawCube(Center, Size);

        Gizmos.color = new Color(0.6f, 0.1f, 1f, 0.8f);
        Gizmos.DrawWireCube(new Vector3(Center.x, CurrentSurfaceY, Center.z), new Vector3(Size.x, 0.05f, Size.z));
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

        Debug.Log("[AbyssFieldController] " + Message, this);
    }
}

/// <summary>
/// Serializable state for one abyss field.
/// </summary>
[Serializable]
public sealed class AbyssFieldSaveData
{
    [SerializeField] private bool HasBeenActivated;
    [SerializeField] private float CurrentSurfaceY;

    /// <summary>
    /// Creates a new abyss save payload.
    /// </summary>
    /// <param name="HasBeenActivatedValue">Whether the abyss field has been activated.</param>
    /// <param name="CurrentSurfaceYValue">Saved surface world Y position.</param>
    public AbyssFieldSaveData(bool HasBeenActivatedValue, float CurrentSurfaceYValue)
    {
        HasBeenActivated = HasBeenActivatedValue;
        CurrentSurfaceY = CurrentSurfaceYValue;
    }

    /// <summary>
    /// Gets whether the abyss field had already been activated.
    /// </summary>
    public bool GetHasBeenActivated()
    {
        return HasBeenActivated;
    }

    /// <summary>
    /// Gets the saved abyss surface world Y position.
    /// </summary>
    public float GetCurrentSurfaceY()
    {
        return CurrentSurfaceY;
    }
}

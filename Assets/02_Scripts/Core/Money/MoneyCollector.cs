using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Helper component that resolves and collects money pickups through player interaction.
/// The base mode collects one looked pickup through a raycast, while the area upgrade reuses that same looked hit as the center of an overlap sphere.
/// </summary>
public sealed class MoneyCollector : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to raycast the looked money pickup from the center of the screen.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Wallet credited when money pickups are collected.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Tooltip("Upgrade manager queried to know whether area collection is unlocked.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Optional feedback emitter used for money collection events.")]
    [SerializeField] private GameFeedbackEmitter FeedbackEmitter;

    [Header("Look Collection")]
    [Tooltip("Maximum raycast distance used to collect the money pickup directly looked at by the player.")]
    [SerializeField] private float CollectDistance = 4f;

    [Tooltip("Layers considered by the looked money raycast.")]
    [SerializeField] private LayerMask CollectionLayers = ~0;

    [Tooltip("Trigger handling mode used by the looked money raycast.")]
    [SerializeField] private QueryTriggerInteraction TriggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Area Collection Upgrade")]
    [Tooltip("If true, area collection is available without requiring an upgrade feature flag. Use this only for validation.")]
    [SerializeField] private bool AllowAreaCollectionWithoutUpgradeRequirement = false;

    [Tooltip("Feature flag required to collect all money pickups inside the overlap sphere created at the looked money hit point.")]
    [SerializeField] private string AreaCollectionFeatureFlagId = "Money.Unlock.AreaCollection";

    [Tooltip("Radius of the overlap sphere created at the looked money hit point when the area collection upgrade is unlocked.")]
    [SerializeField] private float AreaCollectionRadius = 3f;

    [Tooltip("Maximum collider hits evaluated by one area collection interaction. This prevents large physics queries from causing spikes.")]
    [SerializeField] private int AreaCollectionMaxColliders = 64;

    [Tooltip("Maximum money pickups collected by one area collection interaction after duplicate colliders have been resolved.")]
    [SerializeField] private int AreaCollectionMaxPickups = 32;

    [Tooltip("If true, area collection uses the same layers and trigger mode as the normal looked-pickup raycast.")]
    [SerializeField] private bool UseLookCollectionSettingsForAreaCollection = true;

    [Tooltip("Layers considered by area collection when Use Look Collection Settings For Area Collection is disabled.")]
    [SerializeField] private LayerMask AreaCollectionLayers = ~0;

    [Tooltip("Trigger handling mode used by area collection when Use Look Collection Settings For Area Collection is disabled.")]
    [SerializeField] private QueryTriggerInteraction AreaCollectionTriggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Feedback")]
    [Tooltip("Event fired when one looked money pickup is collected.")]
    [SerializeField] private string MoneyCollectedEventId = GameFeedbackEventIds.MoneyCollected;

    [Tooltip("Optional event fired once when one area collection interaction collects at least one money pickup. Leave empty to keep the upgrade purely functional.")]
    [SerializeField] private string MoneyAreaCollectedEventId = string.Empty;

    [Header("Debug")]
    [Tooltip("Logs collection operations.")]
    [SerializeField] private bool DebugLogs = false;

    [Tooltip("Draws the looked collection ray in the scene view.")]
    [SerializeField] private bool DrawDebugRay = false;

    [Tooltip("Draws the area collection sphere at the currently looked money hit point when the upgrade is unlocked.")]
    [SerializeField] private bool DrawDebugArea = false;

    /// <summary>
    /// Money pickup currently under the center-screen raycast.
    /// </summary>
    private MoneyPickup CurrentLookedMoneyPickup;

    /// <summary>
    /// Last valid money hit point resolved by the center-screen raycast.
    /// </summary>
    private Vector3 CurrentLookedMoneyHitPoint;

    /// <summary>
    /// Whether the current looked hit point is valid for area collection debug drawing.
    /// </summary>
    private bool HasCurrentLookedMoneyHitPoint;

    /// <summary>
    /// Whether collection should be blocked by an external modal/controller state.
    /// </summary>
    private bool IsExternalCollectionBlocked;

    /// <summary>
    /// Reusable collider buffer for non-alloc area collection queries.
    /// </summary>
    private Collider[] AreaCollectionBuffer = new Collider[0];

    /// <summary>
    /// Reusable list of unique money pickups resolved from the area query.
    /// </summary>
    private readonly List<MoneyPickup> AreaCollectionPickups = new List<MoneyPickup>(32);

    /// <summary>
    /// Reusable set used to remove duplicate pickups when one pickup has multiple colliders.
    /// </summary>
    private readonly HashSet<MoneyPickup> AreaCollectionUniqueSet = new HashSet<MoneyPickup>();

    /// <summary>
    /// Enables or disables collection from an external modal/controller state.
    /// </summary>
    /// <param name="IsBlocked">Whether collection should be blocked.</param>
    public void SetExternalCollectionBlocked(bool IsBlocked)
    {
        IsExternalCollectionBlocked = IsBlocked;
    }

    /// <summary>
    /// Resolves references and validates required dependencies.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        EnsureAreaCollectionBuffer();

        if (PlayerCamera == null)
        {
            Debug.LogError("[MoneyCollector] PlayerCamera is missing.", this);
            enabled = false;
            return;
        }

        if (CurrencyWallet == null)
        {
            Debug.LogError("[MoneyCollector] CurrencyWallet is missing.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Refreshes the looked money pickup cache for prompts and debug visualization.
    /// Actual collection also performs a fresh raycast when the interact input is pressed.
    /// </summary>
    private void Update()
    {
        if (IsExternalCollectionBlocked)
        {
            CurrentLookedMoneyPickup = null;
            HasCurrentLookedMoneyHitPoint = false;
            return;
        }

        CurrentLookedMoneyPickup = ResolveLookedMoneyPickup(out RaycastHit HitInfo);
        HasCurrentLookedMoneyHitPoint = CurrentLookedMoneyPickup != null;

        if (HasCurrentLookedMoneyHitPoint)
        {
            CurrentLookedMoneyHitPoint = HitInfo.point;
        }

        if (DrawDebugArea && IsAreaCollectionUnlocked() && HasCurrentLookedMoneyHitPoint)
        {
            DebugDrawAreaCollection(CurrentLookedMoneyHitPoint);
        }
    }

    /// <summary>
    /// Returns whether the collector currently has a valid money interaction target.
    /// Area collection intentionally still requires a looked money pickup because the overlap sphere is centered on the raycast hit point.
    /// </summary>
    /// <returns>True when a looked money pickup can be collected.</returns>
    public bool HasCurrentLookedMoneyPickup()
    {
        return CurrentLookedMoneyPickup != null;
    }

    /// <summary>
    /// Tries to collect money using the currently unlocked collection mode.
    /// When the area upgrade is active, the normal looked-pickup raycast provides the overlap sphere center.
    /// </summary>
    /// <returns>True when at least one money pickup was collected.</returns>
    public bool TryCollectCurrentLookedMoney()
    {
        if (IsExternalCollectionBlocked)
        {
            return false;
        }

        MoneyPickup LookedMoneyPickup = ResolveLookedMoneyPickup(out RaycastHit HitInfo);

        if (LookedMoneyPickup == null)
        {
            CurrentLookedMoneyPickup = null;
            HasCurrentLookedMoneyHitPoint = false;
            return false;
        }

        CurrentLookedMoneyPickup = null;
        HasCurrentLookedMoneyHitPoint = false;

        if (IsAreaCollectionUnlocked())
        {
            int CollectedCount = TryCollectMoneyInArea(HitInfo.point, LookedMoneyPickup);
            return CollectedCount > 0;
        }

        return CollectMoneyPickup(LookedMoneyPickup, MoneyCollectedEventId, false);
    }

    /// <summary>
    /// Resolves optional references from the player hierarchy and scene.
    /// </summary>
    private void ResolveReferences()
    {
        if (PlayerCamera == null)
        {
            PlayerController PlayerController = GetComponent<PlayerController>();

            if (PlayerController != null && PlayerController.PlayerCamera != null)
            {
                PlayerCamera = PlayerController.PlayerCamera;
            }
            else
            {
                PlayerCamera = Camera.main;
            }
        }

        if (CurrencyWallet == null)
        {
            CurrencyWallet = FindFirstObjectByType<CurrencyWallet>();
        }

        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }

        if (FeedbackEmitter == null)
        {
            FeedbackEmitter = GetComponent<GameFeedbackEmitter>() ?? GetComponentInChildren<GameFeedbackEmitter>(true);
        }
    }

    /// <summary>
    /// Collects every valid money pickup inside the configured area collection radius using the looked hit point as center.
    /// </summary>
    /// <param name="AreaCenter">World-space center obtained from the looked money raycast hit point.</param>
    /// <param name="SeedPickup">Looked money pickup that must always be collected even if the overlap buffer is saturated.</param>
    /// <returns>Number of money pickups collected.</returns>
    private int TryCollectMoneyInArea(Vector3 AreaCenter, MoneyPickup SeedPickup)
    {
        ResolveAreaMoneyPickups(AreaCollectionPickups, AreaCenter, SeedPickup);

        if (AreaCollectionPickups.Count <= 0)
        {
            return 0;
        }

        int CollectedCount = 0;
        float TotalAmount = 0f;

        for (int Index = 0; Index < AreaCollectionPickups.Count; Index++)
        {
            MoneyPickup MoneyPickup = AreaCollectionPickups[Index];

            if (MoneyPickup == null)
            {
                continue;
            }

            float Amount = MoneyPickup.GetAmount();

            if (!CollectMoneyPickup(MoneyPickup, string.Empty, true))
            {
                continue;
            }

            CollectedCount++;
            TotalAmount += Mathf.Max(0f, Amount);
        }

        AreaCollectionPickups.Clear();
        AreaCollectionUniqueSet.Clear();

        if (CollectedCount <= 0)
        {
            return 0;
        }

        PlayFeedback(MoneyAreaCollectedEventId, AreaCenter, Mathf.Max(1f, TotalAmount));
        Log("Area collected money pickups: " + CollectedCount + " | Total: " + TotalAmount.ToString("0.00") + " | Center: " + AreaCenter.ToString("F2"));
        return CollectedCount;
    }

    /// <summary>
    /// Resolves the money pickup currently looked at by the player.
    /// </summary>
    /// <param name="HitInfo">Raycast hit data when a money pickup is resolved.</param>
    /// <returns>Resolved looked money pickup, or null.</returns>
    private MoneyPickup ResolveLookedMoneyPickup(out RaycastHit HitInfo)
    {
        HitInfo = default(RaycastHit);

        if (PlayerCamera == null)
        {
            return null;
        }

        Ray ViewRay = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (DrawDebugRay)
        {
            Debug.DrawRay(ViewRay.origin, ViewRay.direction * CollectDistance, Color.green);
        }

        if (!Physics.Raycast(ViewRay, out HitInfo, CollectDistance, CollectionLayers, TriggerInteraction))
        {
            return null;
        }

        return ResolveMoneyPickup(HitInfo.collider, HitInfo.rigidbody);
    }

    /// <summary>
    /// Resolves unique money pickups inside the configured area collection sphere.
    /// </summary>
    /// <param name="Results">Reusable list that receives resolved pickups.</param>
    /// <param name="AreaCenter">World-space center obtained from the looked money raycast hit point.</param>
    /// <param name="SeedPickup">Looked money pickup to include first so the direct target is never missed.</param>
    private void ResolveAreaMoneyPickups(List<MoneyPickup> Results, Vector3 AreaCenter, MoneyPickup SeedPickup)
    {
        EnsureAreaCollectionBuffer();
        Results.Clear();
        AreaCollectionUniqueSet.Clear();

        int MaxResolvedPickups = Mathf.Max(1, AreaCollectionMaxPickups);

        if (SeedPickup != null && AreaCollectionUniqueSet.Add(SeedPickup))
        {
            Results.Add(SeedPickup);
        }

        if (Results.Count >= MaxResolvedPickups)
        {
            return;
        }

        float Radius = Mathf.Max(0f, AreaCollectionRadius);
        LayerMask QueryLayers = UseLookCollectionSettingsForAreaCollection ? CollectionLayers : AreaCollectionLayers;
        QueryTriggerInteraction QueryMode = UseLookCollectionSettingsForAreaCollection ? TriggerInteraction : AreaCollectionTriggerInteraction;

        int HitCount = Physics.OverlapSphereNonAlloc(AreaCenter, Radius, AreaCollectionBuffer, QueryLayers, QueryMode);

        for (int Index = 0; Index < HitCount; Index++)
        {
            Collider HitCollider = AreaCollectionBuffer[Index];

            if (HitCollider == null)
            {
                continue;
            }

            MoneyPickup MoneyPickup = ResolveMoneyPickup(HitCollider, HitCollider.attachedRigidbody);

            if (MoneyPickup == null)
            {
                continue;
            }

            if (!AreaCollectionUniqueSet.Add(MoneyPickup))
            {
                continue;
            }

            Results.Add(MoneyPickup);

            if (Results.Count >= MaxResolvedPickups)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Resolves a money pickup from a collider hierarchy.
    /// </summary>
    /// <param name="SourceCollider">Collider to evaluate.</param>
    /// <param name="SourceRigidbody">Optional rigidbody associated with the collider.</param>
    /// <returns>Resolved money pickup, or null.</returns>
    private MoneyPickup ResolveMoneyPickup(Collider SourceCollider, Rigidbody SourceRigidbody)
    {
        if (SourceCollider == null)
        {
            return null;
        }

        MoneyPickup MoneyPickup = SourceCollider.GetComponent<MoneyPickup>() ?? SourceCollider.GetComponentInParent<MoneyPickup>();

        if (MoneyPickup != null)
        {
            return MoneyPickup;
        }

        if (SourceRigidbody != null)
        {
            MoneyPickup = SourceRigidbody.GetComponent<MoneyPickup>() ?? SourceRigidbody.GetComponentInParent<MoneyPickup>();
        }

        return MoneyPickup;
    }

    /// <summary>
    /// Credits one money pickup to the wallet and returns it to its pool or destroys it safely when it has no pool owner.
    /// </summary>
    /// <param name="MoneyPickup">Money pickup to collect.</param>
    /// <param name="FeedbackEventId">Optional feedback event id.</param>
    /// <param name="SuppressIndividualFeedback">If true, no individual feedback is played for this pickup.</param>
    /// <returns>True when the pickup was collected or safely consumed.</returns>
    private bool CollectMoneyPickup(MoneyPickup MoneyPickup, string FeedbackEventId, bool SuppressIndividualFeedback)
    {
        if (MoneyPickup == null)
        {
            return false;
        }

        float Amount = Mathf.Max(0f, MoneyPickup.GetAmount());
        Vector3 FeedbackPosition = MoneyPickup.GetRuntimeRoot() != null ? MoneyPickup.GetRuntimeRoot().position : MoneyPickup.transform.position;

        if (Amount <= 0f)
        {
            Log("Money pickup amount was zero. Returning pickup to pool.");
            ReturnOrDestroyMoneyPickup(MoneyPickup);
            return true;
        }

        CurrencyWallet.AddCurrency(MoneyPickup.GetCurrencyType(), Amount);
        Log("Collected money pickup: " + MoneyPickup.name + " | Amount: " + Amount.ToString("0.00"));
        ReturnOrDestroyMoneyPickup(MoneyPickup);

        if (!SuppressIndividualFeedback)
        {
            PlayFeedback(FeedbackEventId, FeedbackPosition, Mathf.Max(1f, Amount));
        }

        return true;
    }

    /// <summary>
    /// Returns a pickup to its pool, or destroys its runtime root when it has no valid pool ownership.
    /// </summary>
    /// <param name="MoneyPickup">Money pickup to return or destroy.</param>
    private void ReturnOrDestroyMoneyPickup(MoneyPickup MoneyPickup)
    {
        if (MoneyPickup == null)
        {
            return;
        }

        if (MoneyPickup.ReturnToPool())
        {
            return;
        }

        Transform RuntimeRoot = MoneyPickup.GetRuntimeRoot();

        if (RuntimeRoot != null)
        {
            Destroy(RuntimeRoot.gameObject);
            return;
        }

        Destroy(MoneyPickup.gameObject);
    }

    /// <summary>
    /// Returns whether the area collection upgrade is currently available.
    /// </summary>
    /// <returns>True when the area collection feature can be used.</returns>
    private bool IsAreaCollectionUnlocked()
    {
        if (AllowAreaCollectionWithoutUpgradeRequirement)
        {
            return true;
        }

        if (UpgradeManager == null)
        {
            return false;
        }

        return UpgradeManager.IsFeatureUnlocked(AreaCollectionFeatureFlagId);
    }

    /// <summary>
    /// Ensures the non-alloc area collection buffer exists with the configured capacity.
    /// </summary>
    private void EnsureAreaCollectionBuffer()
    {
        int DesiredSize = Mathf.Max(1, AreaCollectionMaxColliders);

        if (AreaCollectionBuffer != null && AreaCollectionBuffer.Length == DesiredSize)
        {
            return;
        }

        AreaCollectionBuffer = new Collider[DesiredSize];
    }

    /// <summary>
    /// Plays optional feedback for money collection.
    /// </summary>
    /// <param name="EventId">Feedback event id to play.</param>
    /// <param name="Position">World position where feedback should play.</param>
    /// <param name="Intensity">Feedback intensity.</param>
    private void PlayFeedback(string EventId, Vector3 Position, float Intensity)
    {
        if (FeedbackEmitter == null || string.IsNullOrWhiteSpace(EventId))
        {
            return;
        }

        FeedbackEmitter.Play(EventId, GameFeedbackContext.FromPosition(Position, transform, Intensity));
    }

    /// <summary>
    /// Draws the exact area collection overlap sphere centered on the looked money raycast hit point.
    /// </summary>
    /// <param name="Center">World-space center of the debug sphere.</param>
    private void DebugDrawAreaCollection(Vector3 Center)
    {
        DrawDebugCircle(Center, Vector3.up, AreaCollectionRadius, Color.yellow);
        DrawDebugCircle(Center, Vector3.right, AreaCollectionRadius, Color.yellow);
        DrawDebugCircle(Center, Vector3.forward, AreaCollectionRadius, Color.yellow);
    }

    /// <summary>
    /// Draws one debug circle using line segments.
    /// </summary>
    /// <param name="Center">World-space circle center.</param>
    /// <param name="Normal">Circle normal.</param>
    /// <param name="Radius">Circle radius.</param>
    /// <param name="Color">Debug line color.</param>
    private void DrawDebugCircle(Vector3 Center, Vector3 Normal, float Radius, Color Color)
    {
        Radius = Mathf.Max(0f, Radius);

        if (Radius <= 0f)
        {
            return;
        }

        Vector3 NormalizedNormal = Normal.sqrMagnitude > 0.0001f ? Normal.normalized : Vector3.up;
        Vector3 Tangent = Vector3.Cross(NormalizedNormal, Vector3.up);

        if (Tangent.sqrMagnitude < 0.0001f)
        {
            Tangent = Vector3.Cross(NormalizedNormal, Vector3.right);
        }

        Tangent.Normalize();
        Vector3 Bitangent = Vector3.Cross(NormalizedNormal, Tangent).normalized;

        const int SegmentCount = 32;
        Vector3 PreviousPoint = Center + Tangent * Radius;

        for (int Index = 1; Index <= SegmentCount; Index++)
        {
            float Angle = (Index / (float)SegmentCount) * Mathf.PI * 2f;
            Vector3 CurrentPoint = Center + ((Tangent * Mathf.Cos(Angle)) + (Bitangent * Mathf.Sin(Angle))) * Radius;
            Debug.DrawLine(PreviousPoint, CurrentPoint, Color);
            PreviousPoint = CurrentPoint;
        }
    }

    /// <summary>
    /// Writes a debug message when logging is enabled.
    /// </summary>
    /// <param name="Message">Message to write.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[MoneyCollector] " + Message, this);
    }
}

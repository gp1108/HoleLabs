using System;
using System.Collections.Generic;
using DamageNumbersPro;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Trigger volume that receives ore pickups, queues them for machine processing,
/// converts processed ore values into exact fixed denominations
/// and ejects physical money pickups over time.
/// Currency is only added later when the player collects emitted money objects.
/// 
/// Float economy note:
/// This component keeps exact physical payout stability by converting every monetary value
/// to integer minor units (cents) internally. Public inspector values still use float currency.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class OreSellTrigger : MonoBehaviour
{
    private const int CurrencyMinorUnitFactor = 100;

    private enum MoneyVisualType
    {
        Coin = 0,
        Bill = 1
    }

    private enum MachineCycleState
    {
        Idle = 0,
        Crushing = 1,
        WaitingForPayout = 2,
        Paying = 3
    }

    [System.Serializable]
    private sealed class MoneyDenomination
    {
        [Tooltip("Unique label used only for inspector readability and debug logs.")]
        [SerializeField] private string Id = "Coin";

        [Tooltip("Money prefab emitted for this denomination.")]
        [SerializeField] private GameObject Prefab;

        [Tooltip("Fixed credit value represented by this denomination.")]
        [FormerlySerializedAs("GoldValue")]
        [SerializeField] private float CreditValue = 1f;

        [Tooltip("Relative random weight used when multiple prefabs share the same fixed value.")]
        [SerializeField] private int Weight = 1;

        [Tooltip("Visual/physical family used to choose the correct eject point and impulse profile.")]
        [SerializeField] private MoneyVisualType VisualType = MoneyVisualType.Coin;

        public string GetId()
        {
            return Id;
        }

        public GameObject GetPrefab()
        {
            return Prefab;
        }

        public float GetCreditValue()
        {
            return Mathf.Max(0.01f, CreditValue);
        }

        public int GetCreditValueMinorUnits()
        {
            return Mathf.Max(1, ToMinorUnits(CreditValue));
        }

        public int GetWeight()
        {
            return Mathf.Max(1, Weight);
        }

        public MoneyVisualType GetVisualType()
        {
            return VisualType;
        }
    }

    private sealed class PendingOreSale
    {
        public OrePickup OrePickup;
        public OreItemData OreItemData;
    }

    private sealed class PendingMoneyEmission
    {
        public MoneyDenomination Denomination;
        public int CreditMinorUnits;
        public int BatchId;
    }

    [Header("References")]
    [Tooltip("Legacy wallet reference kept only to preserve existing scene assignments. The selling machine now pays physical credits only.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Tooltip("Pool used to reuse money prefabs instead of instantiating and destroying them.")]
    [SerializeField] private MoneyPickupPool MoneyPickupPool;

    [Header("Eject Points")]
    [Tooltip("World point and orientation used to eject coins.")]
    [SerializeField] private Transform CoinEjectPoint;

    [Tooltip("World point and orientation used to eject bills.")]
    [SerializeField] private Transform BillEjectPoint;

    [Header("Machine Animation")]
    [Tooltip("Animators that should run only while the selling machine has valid ore pickups waiting to be consumed.")]
    [SerializeField] private Animator[] ProcessingAnimators = new Animator[0];

    [Tooltip("Playback speed applied to every processing animator while the machine is active.")]
    [SerializeField] private float ProcessingAnimationSpeed = 1f;

    [Tooltip("Normalized distance from the end of the current animation loop where the animator is allowed to pause cleanly when immediate stopping is disabled.")]
    [SerializeField][Range(0.001f, 0.1f)] private float AnimationLoopPauseWindow = 0.02f;

    [Tooltip("If true, processing animations snap to a stable rest pose as soon as there are no valid ore pickups waiting to be consumed.")]
    [SerializeField] private bool StopAnimationsImmediatelyWhenNoOre = true;

    [Header("Ore Processing")]
    [Tooltip("Time in seconds between each ore consumption tick while the machine has pending minerals.")]
    [SerializeField] private float OreConsumeInterval = 0.5f;

    [Tooltip("Maximum amount of valid ore pickups consumed on each ore consumption tick.")]
    [SerializeField] private int OresConsumedPerTick = 1;

    [Header("Dynamic Emission")]
    [Tooltip("Slowest possible interval used when the pending money queue is small.")]
    [SerializeField] private float MaxEmissionInterval = 0.5f;

    [Tooltip("Fastest possible interval used when the pending money queue is very large.")]
    [SerializeField] private float MinEmissionInterval = 0.03f;

    [Tooltip("Amount of pending emitted pieces required to reach the minimum emission interval.")]
    [SerializeField] private int FastEmissionQueueThreshold = 100;

    [Header("Coin Emission")]
    [Tooltip("Forward impulse applied to emitted coin rigidbodies.")]
    [SerializeField] private float CoinForwardImpulse = 5f;

    [Tooltip("Upward impulse applied to emitted coin rigidbodies.")]
    [SerializeField] private float CoinUpwardImpulse = 1.5f;

    [Tooltip("Random sphere impulse added to emitted coins.")]
    [SerializeField] private float CoinRandomImpulse = 0.75f;

    [Tooltip("Optional torque impulse added to emitted coins.")]
    [SerializeField] private float CoinRandomTorqueImpulse = 0.25f;

    [Header("Bill Emission")]
    [Tooltip("Forward impulse applied to emitted bill rigidbodies.")]
    [SerializeField] private float BillForwardImpulse = 2.25f;

    [Tooltip("Upward impulse applied to emitted bill rigidbodies.")]
    [SerializeField] private float BillUpwardImpulse = 0.75f;

    [Tooltip("Random sphere impulse added to emitted bills.")]
    [SerializeField] private float BillRandomImpulse = 0.25f;

    [Tooltip("Optional torque impulse added to emitted bills.")]
    [SerializeField] private float BillRandomTorqueImpulse = 0.05f;

    [Header("Payout Rules")]
    [Tooltip("Legacy option kept only to preserve serialized data during migration. Research currency is disabled and this value is ignored.")]
    [SerializeField, HideInInspector] private bool GrantResearchInstantly = false;

    [Tooltip("Available physical denominations used to compose the emitted credit value exactly.")]
    [SerializeField] private List<MoneyDenomination> MoneyDenominations = new();

    [Header("Emission Order")]
    [Tooltip("If true, emitted denomination order is shuffled slightly for visual variation after the optimal amount of pieces has been calculated.")]
    [SerializeField] private bool ShuffleEmissionOrder = true;

    [Header("Batch Display")]
    [Tooltip("Optional root object that is shown while the current batch display has valid values and hidden when the display is cleared.")]
    [SerializeField] private GameObject BatchDisplayRoot;

    [Tooltip("Primary display text. During crushing it shows the accumulated batch total; during payout it shows the remaining unpaid value.")]
    [SerializeField] private TextMeshProUGUI BatchTotalValueText;

    [Tooltip("Optional secondary text that displays the credit value already emitted as physical money during the current batch.")]
    [SerializeField] private TextMeshProUGUI BatchPaidValueText;

    [Tooltip("Optional secondary text that displays the credit value still pending to be emitted during the current batch.")]
    [SerializeField] private TextMeshProUGUI BatchRemainingValueText;

    [Tooltip("Seconds without receiving another unprocessed ore pickup required before the machine closes the current crushing batch and starts paying it out.")]
    [SerializeField] private float BatchInactivityThreshold = 3f;

    [Tooltip("Seconds waited after the current batch has been fully paid before clearing the display when no new ore is waiting.")]
    [SerializeField] private float BatchClearDelay = 1.5f;

    [Tooltip("Suffix appended to formatted batch currency values.")]
    [SerializeField] private string CurrencySuffix = " C";

    [Tooltip("If true, the batch display root is hidden when there is no active batch to show.")]
    [SerializeField] private bool HideBatchDisplayWhenEmpty = true;

    [Header("Damage Numbers Pro Display")]
    [Tooltip("If true, the primary batch value is rendered by Damage Numbers Pro instead of the legacy TextMeshProUGUI primary text.")]
    [SerializeField] private bool UseDamageNumberProBatchDisplay = true;

    [Tooltip("GUI Damage Numbers Pro prefab used as the main machine value display. Use a GUI prefab, not a mesh/worldspace prefab.")]
    [SerializeField] private DamageNumber BatchValueNumberPrefab;

    [Tooltip("RectTransform parent inside the canvas where the Damage Numbers Pro GUI value is spawned.")]
    [SerializeField] private RectTransform BatchValueNumberParent;

    [Tooltip("Anchored position used when spawning the Damage Numbers Pro GUI value under the configured parent.")]
    [SerializeField] private Vector2 BatchValueNumberAnchoredPosition = Vector2.zero;

    [Tooltip("If true, the legacy primary TextMeshProUGUI text is cleared while Damage Numbers Pro is available for the primary value.")]
    [SerializeField] private bool HideLegacyPrimaryTextWhenUsingDamageNumberPro = true;

    [Tooltip("If true, CurrencySuffix is written into Damage Numbers Pro rightText so the popup can show values like 120.00 C.")]
    [SerializeField] private bool UseCurrencySuffixAsDamageNumberRightText = true;

    [Tooltip("If true, the active Damage Numbers Pro value fades out when the batch display is cleared. If false, it is destroyed immediately. Ignored when KeepDamageNumberInstanceWhenCleared is enabled.")]
    [SerializeField] private bool FadeDamageNumberOnClear = true;

    [Tooltip("If true, the spawned Damage Numbers Pro instance is kept and disabled instead of being faded or destroyed when the display clears. This prevents editor inspector errors caused by selecting runtime TextMeshPro objects that get destroyed by the popup lifecycle.")]
    [SerializeField] private bool KeepDamageNumberInstanceWhenCleared = true;

    [Tooltip("If true, the spawned Damage Numbers Pro RectTransform is forced back under the configured parent and anchored position every time the value refreshes.")]
    [SerializeField] private bool ForceDamageNumberRectTransform = true;

    [Header("Debug")]
    [Tooltip("Logs sales, processed ore, denomination decomposition and money emissions.")]
    [SerializeField] private bool DebugLogs = false;

    private readonly Queue<PendingOreSale> PendingOreSales = new();
    private readonly Queue<PendingMoneyEmission> PendingMoneyEmissions = new();
    private readonly List<MoneyDenomination> SortedDenominations = new();
    private readonly HashSet<OrePickup> QueuedOrePickups = new();
    /// <summary>
    /// Tracks ore pickups currently inside the sale trigger volume.
    /// </summary>
    private readonly HashSet<OrePickup> OrePickupsInsideTrigger = new();

    /// <summary>
    /// Counts how many colliders from the same ore pickup are currently inside the trigger.
    /// This prevents one child collider exit from removing a pickup that is still visually inside.
    /// </summary>
    private readonly Dictionary<OrePickup, int> OrePickupTriggerOverlapCounts = new();

    private float OreConsumeTimer;
    private float MoneyEmissionTimer;
    private MachineCycleState CurrentCycleState = MachineCycleState.Idle;
    private float LastOreQueuedTime = -1f;
    private bool HasPayoutCompositionFailure;
    private int CurrentBatchId;
    private bool HasActiveBatch;
    private int CurrentBatchTotalMinorUnits;
    private int CurrentBatchPaidMinorUnits;
    private float LastBatchOreProcessedTime = -1f;
    private float BatchCompletedTime = -1f;
    private bool IsProcessingAnimationActive;
    private bool IsProcessingAnimationStopRequested;
    private bool[] ProcessingAnimatorPausedStates = new bool[0];
    private float[] ProcessingAnimatorPauseTargets = new float[0];
    private DamageNumber ActiveBatchValueNumber;
    private int LastDamageNumberPrimaryMinorUnits = int.MinValue;

    public int PendingSaleCount => PendingOreSales.Count;
    public int PendingMoneyEmissionCount => PendingMoneyEmissions.Count;

    /// <summary>
    /// Resolves the money prefab associated with a denomination id.
    /// This is used by the save system so physical money can be restored
    /// from the same authoritative denomination configuration used by the seller.
    /// </summary>
    /// <param name="DenominationId">Stable denomination id configured in the inspector.</param>
    /// <returns>Matching money prefab, or null if the id is unknown.</returns>
    public GameObject GetMoneyPrefabByDenominationId(string DenominationId)
    {
        if (string.IsNullOrWhiteSpace(DenominationId))
        {
            return null;
        }

        for (int Index = 0; Index < MoneyDenominations.Count; Index++)
        {
            MoneyDenomination Denomination = MoneyDenominations[Index];

            if (Denomination == null)
            {
                continue;
            }

            if (!string.Equals(Denomination.GetId(), DenominationId, StringComparison.Ordinal))
            {
                continue;
            }

            return Denomination.GetPrefab();
        }

        return null;
    }

    /// <summary>
    /// Validates required references and caches the sorted denomination list.
    /// </summary>
    private void Awake()
    {
        CacheSortedDenominations();

        if (CoinEjectPoint == null)
        {
            CoinEjectPoint = transform;
        }

        if (BillEjectPoint == null)
        {
            BillEjectPoint = CoinEjectPoint != null ? CoinEjectPoint : transform;
        }

        InitializeProcessingAnimationState();
        RefreshBatchDisplay();
    }

    /// <summary>
    /// Updates the sell cycle. The machine crushes ore first, waits for the input inactivity window,
    /// pays the closed batch, and only resumes crushing once the payout queue is empty.
    /// </summary>
    private void Update()
    {
        UpdateOreConsumption();
        UpdatePayoutStartGate();
        UpdateMoneyEmission();
        UpdateProcessingAnimationState();
        UpdateBatchDisplayLifecycle();
    }

    /// <summary>
    /// Initializes animation helper arrays and forces every configured processing animator into a safe paused state.
    /// </summary>
    private void InitializeProcessingAnimationState()
    {
        int AnimatorCount = ProcessingAnimators != null ? ProcessingAnimators.Length : 0;
        ProcessingAnimatorPausedStates = new bool[AnimatorCount];
        ProcessingAnimatorPauseTargets = new float[AnimatorCount];

        for (int Index = 0; Index < AnimatorCount; Index++)
        {
            ProcessingAnimatorPausedStates[Index] = true;
            ProcessingAnimatorPauseTargets[Index] = 0f;

            if (ProcessingAnimators[Index] == null)
            {
                continue;
            }

            PauseProcessingAnimatorAtRestPose(ProcessingAnimators[Index]);
        }
    }

    /// <summary>
    /// Starts, keeps alive or cleanly stops the configured processing animations according to machine activity.
    /// </summary>
    private void UpdateProcessingAnimationState()
    {
        if (ShouldProcessingAnimationRun())
        {
            StartProcessingAnimations();
            return;
        }

        if (StopAnimationsImmediatelyWhenNoOre)
        {
            StopProcessingAnimationsImmediately();
            return;
        }

        RequestProcessingAnimationStop();
        UpdateRequestedProcessingAnimationStop();
    }

    /// <summary>
    /// Returns whether the machine is actively allowed to crush valid ore pickups.
    /// Pending payout emissions intentionally do not keep the processing animations alive.
    /// </summary>
    private bool ShouldProcessingAnimationRun()
    {
        return CurrentCycleState != MachineCycleState.Paying && GetValidQueuedOrePickupCount() > 0;
    }

    /// <summary>
    /// Resumes every configured processing animator immediately when the machine becomes active.
    /// </summary>
    private void StartProcessingAnimations()
    {
        EnsureProcessingAnimationArrays();

        IsProcessingAnimationActive = true;
        IsProcessingAnimationStopRequested = false;

        int AnimatorCount = ProcessingAnimators != null ? ProcessingAnimators.Length : 0;

        for (int Index = 0; Index < AnimatorCount; Index++)
        {
            Animator ProcessingAnimator = ProcessingAnimators[Index];

            ProcessingAnimatorPausedStates[Index] = false;
            ProcessingAnimatorPauseTargets[Index] = 0f;

            if (ProcessingAnimator == null)
            {
                continue;
            }

            ProcessingAnimator.speed = Mathf.Max(0.01f, ProcessingAnimationSpeed);
        }
    }

    /// <summary>
    /// Stops every configured processing animator immediately by snapping it to a stable rest pose.
    /// </summary>
    private void StopProcessingAnimationsImmediately()
    {
        if (!IsProcessingAnimationActive && !IsProcessingAnimationStopRequested)
        {
            return;
        }

        EnsureProcessingAnimationArrays();

        IsProcessingAnimationActive = false;
        IsProcessingAnimationStopRequested = false;

        int AnimatorCount = ProcessingAnimators != null ? ProcessingAnimators.Length : 0;

        for (int Index = 0; Index < AnimatorCount; Index++)
        {
            ProcessingAnimatorPausedStates[Index] = true;
            ProcessingAnimatorPauseTargets[Index] = 0f;
            PauseProcessingAnimatorAtRestPose(ProcessingAnimators[Index]);
        }
    }

    /// <summary>
    /// Requests a clean animation stop at the next loop boundary without freezing clips mid-pose.
    /// </summary>
    private void RequestProcessingAnimationStop()
    {
        if (!IsProcessingAnimationActive || IsProcessingAnimationStopRequested)
        {
            return;
        }

        EnsureProcessingAnimationArrays();
        IsProcessingAnimationStopRequested = true;

        int AnimatorCount = ProcessingAnimators != null ? ProcessingAnimators.Length : 0;

        for (int Index = 0; Index < AnimatorCount; Index++)
        {
            ProcessingAnimatorPausedStates[Index] = false;
            ProcessingAnimatorPauseTargets[Index] = ResolveProcessingAnimatorPauseTarget(ProcessingAnimators[Index]);
        }
    }

    /// <summary>
    /// Advances active animators until each one reaches its safe pause target.
    /// </summary>
    private void UpdateRequestedProcessingAnimationStop()
    {
        if (!IsProcessingAnimationStopRequested)
        {
            return;
        }

        bool AllAnimatorsPaused = true;

        int AnimatorCount = ProcessingAnimators != null ? ProcessingAnimators.Length : 0;

        for (int Index = 0; Index < AnimatorCount; Index++)
        {
            if (ProcessingAnimatorPausedStates[Index])
            {
                continue;
            }

            Animator ProcessingAnimator = ProcessingAnimators[Index];

            if (ProcessingAnimator == null || IsProcessingAnimatorAtPauseTarget(ProcessingAnimator, ProcessingAnimatorPauseTargets[Index]))
            {
                PauseProcessingAnimatorAtRestPose(ProcessingAnimator);
                ProcessingAnimatorPausedStates[Index] = true;
            }

            if (!ProcessingAnimatorPausedStates[Index])
            {
                AllAnimatorsPaused = false;
            }
        }

        if (!AllAnimatorsPaused)
        {
            return;
        }

        IsProcessingAnimationActive = false;
        IsProcessingAnimationStopRequested = false;
    }

    /// <summary>
    /// Calculates the normalized animation time where an animator may be paused cleanly.
    /// </summary>
    /// <param name="ProcessingAnimator">Animator that is being evaluated.</param>
    /// <returns>Normalized time target used to pause the animator.</returns>
    private float ResolveProcessingAnimatorPauseTarget(Animator ProcessingAnimator)
    {
        if (ProcessingAnimator == null || ProcessingAnimator.layerCount == 0 || ProcessingAnimator.IsInTransition(0))
        {
            return 0f;
        }

        AnimatorStateInfo StateInfo = ProcessingAnimator.GetCurrentAnimatorStateInfo(0);
        float PauseWindow = Mathf.Clamp(AnimationLoopPauseWindow, 0.001f, 0.1f);

        if (!StateInfo.loop)
        {
            return Mathf.Max(StateInfo.normalizedTime, 1f - PauseWindow);
        }

        float CurrentNormalizedTime = Mathf.Max(0f, StateInfo.normalizedTime);
        float CurrentLoopEndTarget = Mathf.Floor(CurrentNormalizedTime) + 1f - PauseWindow;

        return CurrentNormalizedTime >= CurrentLoopEndTarget
            ? CurrentNormalizedTime
            : CurrentLoopEndTarget;
    }

    /// <summary>
    /// Returns whether the animator has reached the normalized time where it may be paused safely.
    /// </summary>
    /// <param name="ProcessingAnimator">Animator that is being evaluated.</param>
    /// <param name="PauseTarget">Normalized time target generated when the stop was requested.</param>
    /// <returns>True when the animator can be paused without freezing mid-loop.</returns>
    private bool IsProcessingAnimatorAtPauseTarget(Animator ProcessingAnimator, float PauseTarget)
    {
        if (ProcessingAnimator == null || ProcessingAnimator.layerCount == 0)
        {
            return true;
        }

        if (ProcessingAnimator.IsInTransition(0))
        {
            return false;
        }

        AnimatorStateInfo StateInfo = ProcessingAnimator.GetCurrentAnimatorStateInfo(0);

        if (!StateInfo.loop)
        {
            return StateInfo.normalizedTime >= PauseTarget;
        }

        return StateInfo.normalizedTime >= PauseTarget;
    }

    /// <summary>
    /// Pauses an animator on the first frame of its current state so the next resume starts from a stable pose.
    /// </summary>
    /// <param name="ProcessingAnimator">Animator that should be paused.</param>
    private void PauseProcessingAnimatorAtRestPose(Animator ProcessingAnimator)
    {
        if (ProcessingAnimator == null || ProcessingAnimator.layerCount == 0)
        {
            return;
        }

        if (!ProcessingAnimator.isActiveAndEnabled)
        {
            ProcessingAnimator.speed = 0f;
            return;
        }

        AnimatorStateInfo StateInfo = ProcessingAnimator.GetCurrentAnimatorStateInfo(0);
        ProcessingAnimator.Play(StateInfo.fullPathHash, 0, 0f);
        ProcessingAnimator.Update(0f);
        ProcessingAnimator.speed = 0f;
    }

    /// <summary>
    /// Keeps animation helper arrays synchronized with the inspector animator list.
    /// </summary>
    private void EnsureProcessingAnimationArrays()
    {
        int AnimatorCount = ProcessingAnimators != null ? ProcessingAnimators.Length : 0;

        if (ProcessingAnimatorPausedStates != null &&
            ProcessingAnimatorPauseTargets != null &&
            ProcessingAnimatorPausedStates.Length == AnimatorCount &&
            ProcessingAnimatorPauseTargets.Length == AnimatorCount)
        {
            return;
        }

        ProcessingAnimatorPausedStates = new bool[AnimatorCount];
        ProcessingAnimatorPauseTargets = new float[AnimatorCount];
    }

    private void OnTriggerEnter(Collider Other)
    {
        if (Other == null)
        {
            return;
        }

        OrePickup OrePickup = Other.GetComponent<OrePickup>();

        if (OrePickup == null)
        {
            OrePickup = Other.GetComponentInParent<OrePickup>();
        }

        if (OrePickup == null)
        {
            return;
        }

        RegisterOrePickupTriggerEnter(OrePickup);
        TryQueueSalePickup(OrePickup);
    }

    private void OnTriggerExit(Collider Other)
    {
        if (Other == null)
        {
            return;
        }

        OrePickup OrePickup = Other.GetComponent<OrePickup>();

        if (OrePickup == null)
        {
            OrePickup = Other.GetComponentInParent<OrePickup>();
        }

        if (OrePickup == null)
        {
            return;
        }

        RegisterOrePickupTriggerExit(OrePickup);
    }

    /// <summary>
    /// Registers that one collider from an ore pickup has entered the trigger volume.
    /// </summary>
    /// <param name="OrePickup">Ore pickup owning the collider that entered.</param>
    private void RegisterOrePickupTriggerEnter(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
            return;
        }

        if (!OrePickupTriggerOverlapCounts.TryGetValue(OrePickup, out int OverlapCount))
        {
            OverlapCount = 0;
        }

        OrePickupTriggerOverlapCounts[OrePickup] = OverlapCount + 1;
        OrePickupsInsideTrigger.Add(OrePickup);
    }

    /// <summary>
    /// Registers that one collider from an ore pickup has left the trigger volume.
    /// The pickup is removed only when all of its colliders are outside.
    /// </summary>
    /// <param name="OrePickup">Ore pickup owning the collider that exited.</param>
    private void RegisterOrePickupTriggerExit(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
            return;
        }

        if (!OrePickupTriggerOverlapCounts.TryGetValue(OrePickup, out int OverlapCount))
        {
            RemoveOrePickupFromActiveQueues(OrePickup, "left the trigger without a registered overlap count");
            return;
        }

        OverlapCount--;

        if (OverlapCount > 0)
        {
            OrePickupTriggerOverlapCounts[OrePickup] = OverlapCount;
            return;
        }

        OrePickupTriggerOverlapCounts.Remove(OrePickup);
        RemoveOrePickupFromActiveQueues(OrePickup, "left the trigger");
    }

    /// <summary>
    /// Removes one ore pickup from the active trigger and sale tracking collections.
    /// </summary>
    /// <param name="OrePickup">Ore pickup to remove.</param>
    /// <param name="Reason">Diagnostic reason used by debug logs.</param>
    private void RemoveOrePickupFromActiveQueues(OrePickup OrePickup, string Reason)
    {
        if (OrePickup == null)
        {
            return;
        }

        OrePickupsInsideTrigger.Remove(OrePickup);
        QueuedOrePickups.Remove(OrePickup);

        Log("Removed ore pickup from active sale queue because it " + Reason + ": " + OrePickup.name);
    }

    /// <summary>
    /// Returns the amount of queued ore pickups that still exist, are active and remain inside the trigger.
    /// Invalid references are pruned before the count is returned.
    /// </summary>
    /// <returns>Amount of valid ore pickups waiting to be consumed.</returns>
    private int GetValidQueuedOrePickupCount()
    {
        PruneInvalidQueuedOrePickups();
        return QueuedOrePickups.Count;
    }

    /// <summary>
    /// Removes stale ore pickup references that can no longer be consumed visually or physically.
    /// </summary>
    private void PruneInvalidQueuedOrePickups()
    {
        if (QueuedOrePickups.Count == 0)
        {
            return;
        }

        List<OrePickup> InvalidOrePickups = null;

        foreach (OrePickup QueuedOrePickup in QueuedOrePickups)
        {
            if (IsQueuedOrePickupStillValid(QueuedOrePickup))
            {
                continue;
            }

            if (InvalidOrePickups == null)
            {
                InvalidOrePickups = new List<OrePickup>();
            }

            InvalidOrePickups.Add(QueuedOrePickup);
        }

        if (InvalidOrePickups == null)
        {
            return;
        }

        for (int Index = 0; Index < InvalidOrePickups.Count; Index++)
        {
            OrePickup InvalidOrePickup = InvalidOrePickups[Index];

            if (InvalidOrePickup != null)
            {
                OrePickupsInsideTrigger.Remove(InvalidOrePickup);
                OrePickupTriggerOverlapCounts.Remove(InvalidOrePickup);
            }

            QueuedOrePickups.Remove(InvalidOrePickup);
        }
    }

    /// <summary>
    /// Returns whether a queued ore pickup can still be consumed by the machine.
    /// </summary>
    /// <param name="OrePickup">Ore pickup being evaluated.</param>
    /// <returns>True when the pickup is still valid, active and inside the trigger.</returns>
    private bool IsQueuedOrePickupStillValid(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
            return false;
        }

        Transform RuntimeRoot = OrePickup.GetRuntimeRoot();

        if (RuntimeRoot == null || !RuntimeRoot.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (!OrePickupsInsideTrigger.Contains(OrePickup))
        {
            return false;
        }

        return OrePickup.GetOreItemData() != null;
    }

    /// <summary>
    /// Drains invalid pending sale entries so old queue entries cannot keep internal counters alive forever.
    /// </summary>
    private void DrainInvalidPendingOreSales()
    {
        if (PendingOreSales.Count == 0)
        {
            return;
        }

        int EntriesToInspect = PendingOreSales.Count;

        for (int Index = 0; Index < EntriesToInspect; Index++)
        {
            PendingOreSale PendingOreSale = PendingOreSales.Dequeue();

            if (PendingOreSale == null || !IsQueuedOrePickupStillValid(PendingOreSale.OrePickup))
            {
                continue;
            }

            PendingOreSales.Enqueue(PendingOreSale);
        }
    }

    public bool TryQueueSaleFromCollider(Collider Other)
    {
        if (Other == null)
        {
            return false;
        }

        OrePickup OrePickup = Other.GetComponent<OrePickup>();

        if (OrePickup == null)
        {
            OrePickup = Other.GetComponentInParent<OrePickup>();
        }

        if (OrePickup == null)
        {
            return false;
        }

        return TryQueueSalePickup(OrePickup);
    }

    public bool TryQueueSalePickup(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
            return false;
        }

        if (!OrePickupsInsideTrigger.Contains(OrePickup))
        {
            Log("Ignored queue request because ore pickup is not inside the trigger: " + OrePickup.name);
            return false;
        }

        if (QueuedOrePickups.Contains(OrePickup))
        {
            Log("Ignored duplicate queue request for ore pickup: " + OrePickup.name);
            return false;
        }

        OreItemData OreItemData = OrePickup.GetOreItemData();

        if (OreItemData == null)
        {
            Log("Ignored ore pickup without valid OreItemData: " + OrePickup.name);
            return false;
        }

        PendingOreSale PendingOreSale = new PendingOreSale
        {
            OrePickup = OrePickup,
            OreItemData = OreItemData
        };

        PendingOreSales.Enqueue(PendingOreSale);
        QueuedOrePickups.Add(OrePickup);
        LastOreQueuedTime = Time.time;
        HasPayoutCompositionFailure = false;

        if (CurrentCycleState != MachineCycleState.Paying)
        {
            CurrentCycleState = MachineCycleState.Crushing;
        }

        LogQueuedOre(OreItemData);
        return true;
    }

    private void UpdateOreConsumption()
    {
        if (CurrentCycleState == MachineCycleState.Paying)
        {
            return;
        }

        if (PendingOreSales.Count == 0)
        {
            if (HasActiveBatch && CurrentCycleState == MachineCycleState.Crushing)
            {
                CurrentCycleState = MachineCycleState.WaitingForPayout;
            }

            return;
        }

        if (GetValidQueuedOrePickupCount() == 0)
        {
            DrainInvalidPendingOreSales();

            if (HasActiveBatch)
            {
                CurrentCycleState = MachineCycleState.WaitingForPayout;
            }

            return;
        }

        CurrentCycleState = MachineCycleState.Crushing;
        OreConsumeTimer -= Time.deltaTime;

        if (OreConsumeTimer > 0f)
        {
            return;
        }

        OreConsumeTimer = Mathf.Max(0.01f, OreConsumeInterval);
        ConsumeQueuedOreTick();
    }

    /// <summary>
    /// Consumes up to the configured amount of valid ore pickups during one processing tick.
    /// </summary>
    private void ConsumeQueuedOreTick()
    {
        int MaxOreConsumedThisTick = Mathf.Max(1, OresConsumedPerTick);

        for (int Index = 0; Index < MaxOreConsumedThisTick; Index++)
        {
            if (GetValidQueuedOrePickupCount() == 0)
            {
                return;
            }

            ConsumeNextQueuedOre();
        }
    }

    private void UpdateMoneyEmission()
    {
        if (CurrentCycleState != MachineCycleState.Paying)
        {
            return;
        }

        if (PendingMoneyEmissions.Count == 0)
        {
            CompleteCurrentPayoutCycle();
            return;
        }

        MoneyEmissionTimer -= Time.deltaTime;

        if (MoneyEmissionTimer > 0f)
        {
            return;
        }

        MoneyEmissionTimer = GetCurrentEmissionInterval();
        EmitNextMoneyPiece();

        if (PendingMoneyEmissions.Count == 0)
        {
            CompleteCurrentPayoutCycle();
        }
    }

    private void ConsumeNextQueuedOre()
    {
        if (PendingOreSales.Count == 0)
        {
            return;
        }

        PendingOreSale PendingOreSale = null;

        while (PendingOreSales.Count > 0)
        {
            PendingOreSale Candidate = PendingOreSales.Dequeue();

            if (Candidate == null)
            {
                continue;
            }

            OrePickup CandidateOrePickup = Candidate.OrePickup;

            if (CandidateOrePickup == null)
            {
                continue;
            }

            bool IsStillQueued = QueuedOrePickups.Contains(CandidateOrePickup);
            bool IsStillInsideTrigger = OrePickupsInsideTrigger.Contains(CandidateOrePickup);

            if (!IsStillQueued || !IsStillInsideTrigger)
            {
                Log("Skipped queued ore because it is no longer valid for sale: " + CandidateOrePickup.name);
                continue;
            }

            PendingOreSale = Candidate;
            break;
        }

        if (PendingOreSale == null)
        {
            return;
        }

        OrePickup OrePickup = PendingOreSale.OrePickup;
        OreItemData OreItemData = PendingOreSale.OreItemData;

        if (OrePickup != null)
        {
            QueuedOrePickups.Remove(OrePickup);
            OrePickupsInsideTrigger.Remove(OrePickup);
            OrePickupTriggerOverlapCounts.Remove(OrePickup);

            if (!OrePickup.ReturnToPool())
            {
                Destroy(OrePickup.GetRuntimeRoot().gameObject);
            }
        }

        if (OreItemData == null)
        {
            Log("Consumed ore entry with null OreItemData.");
            return;
        }

        float CreditValue = Mathf.Max(0f, OreItemData.GetCreditValue());
        int CreditMinorUnits = ToMinorUnits(CreditValue);

        if (CreditMinorUnits > 0)
        {
            RegisterBatchProcessedValue(CreditMinorUnits);
        }


        LogProcessedOre(OreItemData);
    }

    /// <summary>
    /// Converts a closed batch value into exact physical money emissions assigned to the provided batch.
    /// The composition is always optimal, meaning the smallest possible amount of physical pieces is used.
    /// </summary>
    /// <param name="TargetMinorUnits">Credits value in minor currency units that must be emitted physically.</param>
    /// <param name="BatchId">Batch identifier that owns the emitted money pieces.</param>
    /// <returns>True when an exact optimal payout composition was queued.</returns>
    private bool TryEnqueueExactMoneyPayout(int TargetMinorUnits, int BatchId)
    {
        int ClampedTargetMinorUnits = Mathf.Max(0, TargetMinorUnits);

        if (ClampedTargetMinorUnits <= 0)
        {
            return true;
        }

        List<MoneyDenomination> Result = BuildExactDenominationComposition(ClampedTargetMinorUnits);

        if (Result == null || Result.Count == 0)
        {
            return false;
        }

        if (ShuffleEmissionOrder && Result.Count > 1)
        {
            ShuffleDenominationList(Result);
        }

        for (int Index = 0; Index < Result.Count; Index++)
        {
            MoneyDenomination Denomination = Result[Index];

            PendingMoneyEmissions.Enqueue(new PendingMoneyEmission
            {
                Denomination = Denomination,
                CreditMinorUnits = Denomination != null ? Denomination.GetCreditValueMinorUnits() : 0,
                BatchId = BatchId
            });
        }

        Log(
            "Queued optimal exact payout for batch value " + FromMinorUnits(ClampedTargetMinorUnits).ToString("0.00") +
            " | Pieces: " + Result.Count);

        return true;
    }

    private void EmitNextMoneyPiece()
    {
        if (PendingMoneyEmissions.Count == 0)
        {
            return;
        }

        PendingMoneyEmission PendingMoneyEmission = PendingMoneyEmissions.Dequeue();

        if (PendingMoneyEmission == null || PendingMoneyEmission.Denomination == null)
        {
            return;
        }

        bool WasEmitted = EmitMoneyDenomination(PendingMoneyEmission.Denomination);

        if (WasEmitted)
        {
            RegisterBatchPaidValue(PendingMoneyEmission.CreditMinorUnits, PendingMoneyEmission.BatchId);
        }
    }

    /// <summary>
    /// Emits one physical money pickup for the provided denomination.
    /// </summary>
    /// <param name="Denomination">Denomination that should be emitted.</param>
    /// <returns>True when a money pickup was created and initialized.</returns>
    private bool EmitMoneyDenomination(MoneyDenomination Denomination)
    {
        if (Denomination == null || Denomination.GetPrefab() == null)
        {
            Log("Skipped money emission because the denomination or its prefab is invalid.");
            return false;
        }

        Transform EjectPoint = ResolveEjectPoint(Denomination.GetVisualType());
        MoneyPickup MoneyPickup = null;

        if (MoneyPickupPool != null)
        {
            MoneyPickup = MoneyPickupPool.GetPickup(
                Denomination.GetPrefab(),
                EjectPoint.position,
                EjectPoint.rotation);
        }

        if (MoneyPickup == null)
        {
            GameObject Instance = Instantiate(Denomination.GetPrefab(), EjectPoint.position, EjectPoint.rotation);
            MoneyPickup = Instance.GetComponent<MoneyPickup>();

            if (MoneyPickup == null)
            {
                MoneyPickup = Instance.GetComponentInChildren<MoneyPickup>(true);
            }

            if (MoneyPickup != null)
            {
                MoneyPickup.BindPool(null, Denomination.GetPrefab());
            }
        }

        if (MoneyPickup == null)
        {
            Log("Failed to create money pickup for denomination " + Denomination.GetId() + ".");
            return false;
        }

        MoneyPickup.Initialize(Denomination.GetCreditValue(), global::CurrencyWallet.CurrencyType.Credits);
        MoneyPickup.SetSaveMoneyId(Denomination.GetId());
        ApplyEmissionImpulse(MoneyPickup, Denomination.GetVisualType(), EjectPoint);

        Log(
            "Emitted denomination | Id: " + Denomination.GetId() +
            " | Value: " + Denomination.GetCreditValue().ToString("0.00") +
            " | VisualType: " + Denomination.GetVisualType() +
            " | Remaining pending pieces: " + PendingMoneyEmissions.Count);

        return true;
    }

    private void ApplyEmissionImpulse(MoneyPickup MoneyPickup, MoneyVisualType VisualType, Transform EjectPoint)
    {
        Rigidbody MoneyRigidbody = MoneyPickup.GetCachedRigidbody();

        if (MoneyRigidbody == null || EjectPoint == null)
        {
            return;
        }

        float ForwardImpulse;
        float UpwardImpulse;
        float RandomImpulse;
        float RandomTorqueImpulse;

        if (VisualType == MoneyVisualType.Bill)
        {
            ForwardImpulse = BillForwardImpulse;
            UpwardImpulse = BillUpwardImpulse;
            RandomImpulse = BillRandomImpulse;
            RandomTorqueImpulse = BillRandomTorqueImpulse;
        }
        else
        {
            ForwardImpulse = CoinForwardImpulse;
            UpwardImpulse = CoinUpwardImpulse;
            RandomImpulse = CoinRandomImpulse;
            RandomTorqueImpulse = CoinRandomTorqueImpulse;
        }

        Vector3 Impulse = EjectPoint.forward * ForwardImpulse;
        Impulse += EjectPoint.up * UpwardImpulse;
        Impulse += UnityEngine.Random.insideUnitSphere * RandomImpulse;

        MoneyRigidbody.AddForce(Impulse, ForceMode.Impulse);

        if (RandomTorqueImpulse > 0f)
        {
            MoneyRigidbody.AddTorque(UnityEngine.Random.insideUnitSphere * RandomTorqueImpulse, ForceMode.Impulse);
        }
    }

    private Transform ResolveEjectPoint(MoneyVisualType VisualType)
    {
        if (VisualType == MoneyVisualType.Bill)
        {
            return BillEjectPoint != null ? BillEjectPoint : (CoinEjectPoint != null ? CoinEjectPoint : transform);
        }

        return CoinEjectPoint != null ? CoinEjectPoint : transform;
    }

    private List<MoneyDenomination> BuildExactDenominationComposition(int TargetMinorUnits)
    {
        if (TargetMinorUnits <= 0)
        {
            return new List<MoneyDenomination>();
        }

        if (SortedDenominations.Count == 0)
        {
            return null;
        }

        Dictionary<int, List<MoneyDenomination>> DenominationsByMinorUnits = new();
        List<int> UniqueMinorUnitValues = new();

        for (int Index = 0; Index < SortedDenominations.Count; Index++)
        {
            MoneyDenomination Denomination = SortedDenominations[Index];
            int Value = Denomination.GetCreditValueMinorUnits();

            if (!DenominationsByMinorUnits.TryGetValue(Value, out List<MoneyDenomination> Bucket))
            {
                Bucket = new List<MoneyDenomination>();
                DenominationsByMinorUnits.Add(Value, Bucket);
                UniqueMinorUnitValues.Add(Value);
            }

            Bucket.Add(Denomination);
        }

        UniqueMinorUnitValues.Sort((Left, Right) => Right.CompareTo(Left));

        List<int> OptimalValueComposition = BuildOptimalValueComposition(TargetMinorUnits, UniqueMinorUnitValues);

        if (OptimalValueComposition == null || OptimalValueComposition.Count == 0)
        {
            return null;
        }

        List<int> FinalValueComposition = new(OptimalValueComposition);
        List<MoneyDenomination> Result = new();

        for (int Index = 0; Index < FinalValueComposition.Count; Index++)
        {
            int Value = FinalValueComposition[Index];

            if (!DenominationsByMinorUnits.TryGetValue(Value, out List<MoneyDenomination> Bucket) || Bucket.Count == 0)
            {
                return null;
            }

            MoneyDenomination SelectedDenomination = PickWeightedDenomination(Bucket);

            if (SelectedDenomination == null)
            {
                return null;
            }

            Result.Add(SelectedDenomination);
        }

        return Result;
    }

    private List<int> BuildOptimalValueComposition(int TargetMinorUnits, List<int> AvailableValues)
    {
        if (TargetMinorUnits <= 0)
        {
            return new List<int>();
        }

        if (AvailableValues == null || AvailableValues.Count == 0)
        {
            return null;
        }

        int[] BestCountForValue = new int[TargetMinorUnits + 1];
        int[] PreviousAmount = new int[TargetMinorUnits + 1];
        int[] ChosenValue = new int[TargetMinorUnits + 1];

        for (int Amount = 0; Amount <= TargetMinorUnits; Amount++)
        {
            BestCountForValue[Amount] = int.MaxValue;
            PreviousAmount[Amount] = -1;
            ChosenValue[Amount] = -1;
        }

        BestCountForValue[0] = 0;

        for (int Amount = 1; Amount <= TargetMinorUnits; Amount++)
        {
            for (int Index = 0; Index < AvailableValues.Count; Index++)
            {
                int Value = AvailableValues[Index];

                if (Value > Amount)
                {
                    continue;
                }

                int Previous = Amount - Value;

                if (BestCountForValue[Previous] == int.MaxValue)
                {
                    continue;
                }

                int CandidateCount = BestCountForValue[Previous] + 1;

                bool IsBetter = CandidateCount < BestCountForValue[Amount];
                bool SameCountButLargerDenomination =
                    CandidateCount == BestCountForValue[Amount] &&
                    Value > ChosenValue[Amount];

                if (IsBetter || SameCountButLargerDenomination)
                {
                    BestCountForValue[Amount] = CandidateCount;
                    PreviousAmount[Amount] = Previous;
                    ChosenValue[Amount] = Value;
                }
            }
        }

        if (BestCountForValue[TargetMinorUnits] == int.MaxValue)
        {
            return null;
        }

        List<int> Result = new();
        int CurrentAmount = TargetMinorUnits;

        while (CurrentAmount > 0)
        {
            int Value = ChosenValue[CurrentAmount];

            if (Value <= 0)
            {
                return null;
            }

            Result.Add(Value);
            CurrentAmount = PreviousAmount[CurrentAmount];
        }

        return Result;
    }

    private MoneyDenomination PickWeightedDenomination(List<MoneyDenomination> Bucket)
    {
        if (Bucket == null || Bucket.Count == 0)
        {
            return null;
        }

        int TotalWeight = 0;

        for (int Index = 0; Index < Bucket.Count; Index++)
        {
            TotalWeight += Bucket[Index].GetWeight();
        }

        if (TotalWeight <= 0)
        {
            return Bucket[0];
        }

        int Roll = UnityEngine.Random.Range(0, TotalWeight);
        int CumulativeWeight = 0;

        for (int Index = 0; Index < Bucket.Count; Index++)
        {
            CumulativeWeight += Bucket[Index].GetWeight();

            if (Roll < CumulativeWeight)
            {
                return Bucket[Index];
            }
        }

        return Bucket[Bucket.Count - 1];
    }

    private float GetCurrentEmissionInterval()
    {
        if (PendingMoneyEmissions.Count <= 0)
        {
            return Mathf.Max(0.01f, MaxEmissionInterval);
        }

        int Threshold = Mathf.Max(1, FastEmissionQueueThreshold);
        float T = Mathf.Clamp01((float)PendingMoneyEmissions.Count / Threshold);

        return Mathf.Lerp(
            Mathf.Max(0.01f, MaxEmissionInterval),
            Mathf.Max(0.01f, MinEmissionInterval),
            T);
    }

    private void CacheSortedDenominations()
    {
        SortedDenominations.Clear();

        for (int Index = 0; Index < MoneyDenominations.Count; Index++)
        {
            MoneyDenomination Denomination = MoneyDenominations[Index];

            if (Denomination == null || Denomination.GetPrefab() == null)
            {
                continue;
            }

            SortedDenominations.Add(Denomination);
        }

        SortedDenominations.Sort(
            (Left, Right) => Right.GetCreditValueMinorUnits().CompareTo(Left.GetCreditValueMinorUnits()));
    }

    private void ShuffleDenominationList(List<MoneyDenomination> Denominations)
    {
        if (Denominations == null || Denominations.Count <= 1)
        {
            return;
        }

        for (int Index = Denominations.Count - 1; Index > 0; Index--)
        {
            int SwapIndex = UnityEngine.Random.Range(0, Index + 1);
            MoneyDenomination Cached = Denominations[Index];
            Denominations[Index] = Denominations[SwapIndex];
            Denominations[SwapIndex] = Cached;
        }
    }

    /// <summary>
    /// Adds crushed ore value into the current visible batch.
    /// A new batch is created only when there is no active unpaid or waiting batch.
    /// </summary>
    /// <param name="CreditMinorUnits">Processed credits value in minor currency units.</param>
    /// <returns>Identifier of the batch that owns this processed value.</returns>
    private int RegisterBatchProcessedValue(int CreditMinorUnits)
    {
        int ClampedCreditMinorUnits = Mathf.Max(0, CreditMinorUnits);

        if (ClampedCreditMinorUnits <= 0)
        {
            return CurrentBatchId;
        }

        bool ShouldForceNewBatch = HasActiveBatch &&
            CurrentCycleState != MachineCycleState.Paying &&
            CurrentBatchTotalMinorUnits > 0 &&
            GetCurrentBatchRemainingMinorUnits() <= 0;

        if (!HasActiveBatch || ShouldForceNewBatch)
        {
            StartNewBatch();
        }

        HasActiveBatch = true;
        CurrentBatchTotalMinorUnits += ClampedCreditMinorUnits;
        LastBatchOreProcessedTime = Time.time;
        BatchCompletedTime = -1f;
        HasPayoutCompositionFailure = false;

        RefreshBatchDisplay();
        return CurrentBatchId;
    }

    /// <summary>
    /// Starts payout for the current batch when the machine has stopped receiving unprocessed ore
    /// for the configured inactivity window and no valid ore remains available to crush.
    /// </summary>
    private void UpdatePayoutStartGate()
    {
        if (!HasActiveBatch || CurrentCycleState == MachineCycleState.Paying)
        {
            return;
        }

        if (CurrentBatchTotalMinorUnits <= 0 || GetCurrentBatchRemainingMinorUnits() <= 0)
        {
            return;
        }

        if (GetValidQueuedOrePickupCount() > 0)
        {
            CurrentCycleState = MachineCycleState.Crushing;
            return;
        }

        DrainInvalidPendingOreSales();

        if (GetValidQueuedOrePickupCount() > 0)
        {
            CurrentCycleState = MachineCycleState.Crushing;
            return;
        }

        CurrentCycleState = MachineCycleState.WaitingForPayout;

        if (LastOreQueuedTime < 0f)
        {
            return;
        }

        if (Time.time - LastOreQueuedTime < Mathf.Max(0f, BatchInactivityThreshold))
        {
            return;
        }

        TryStartCurrentBatchPayout();
    }

    /// <summary>
    /// Builds the optimal physical payout queue for the current closed batch and switches the machine into payout mode.
    /// </summary>
    private void TryStartCurrentBatchPayout()
    {
        if (!HasActiveBatch ||
            CurrentBatchTotalMinorUnits <= 0 ||
            GetCurrentBatchRemainingMinorUnits() <= 0 ||
            HasPayoutCompositionFailure)
        {
            return;
        }

        PendingMoneyEmissions.Clear();
        CurrentBatchPaidMinorUnits = 0;

        bool CouldBuildExactPayout = TryEnqueueExactMoneyPayout(CurrentBatchTotalMinorUnits, CurrentBatchId);

        if (!CouldBuildExactPayout)
        {
            HasPayoutCompositionFailure = true;

            Log(
                "Failed to build exact optimal payout for batch value " +
                FromMinorUnits(CurrentBatchTotalMinorUnits).ToString("0.00") +
                ". Check configured denominations. At least one denomination combination is missing.");

            RefreshBatchDisplay();
            return;
        }

        CurrentCycleState = MachineCycleState.Paying;
        MoneyEmissionTimer = 0f;
        BatchCompletedTime = -1f;
        RefreshBatchDisplay();
    }

    /// <summary>
    /// Registers emitted physical money value for the batch that owns the emitted piece.
    /// </summary>
    /// <param name="CreditMinorUnits">Emitted credits value in minor currency units.</param>
    /// <param name="BatchId">Batch identifier assigned to the emitted money piece.</param>
    private void RegisterBatchPaidValue(int CreditMinorUnits, int BatchId)
    {
        if (!HasActiveBatch || BatchId != CurrentBatchId)
        {
            return;
        }

        CurrentBatchPaidMinorUnits = Mathf.Min(
            CurrentBatchTotalMinorUnits,
            CurrentBatchPaidMinorUnits + Mathf.Max(0, CreditMinorUnits));

        if (GetCurrentBatchRemainingMinorUnits() <= 0 && BatchCompletedTime < 0f)
        {
            BatchCompletedTime = Time.time;
        }

        RefreshBatchDisplay();
    }

    /// <summary>
    /// Completes the active payout cycle. New ore that arrived during payout remains queued
    /// and will be crushed only after the current batch display has been reset.
    /// </summary>
    private void CompleteCurrentPayoutCycle()
    {
        if (CurrentCycleState != MachineCycleState.Paying)
        {
            return;
        }

        CurrentBatchPaidMinorUnits = CurrentBatchTotalMinorUnits;
        BatchCompletedTime = Time.time;
        HasPayoutCompositionFailure = false;

        if (GetValidQueuedOrePickupCount() > 0)
        {
            ClearBatchDisplayState();
            CurrentCycleState = MachineCycleState.Crushing;
            OreConsumeTimer = 0f;
            return;
        }

        CurrentCycleState = MachineCycleState.Idle;
        RefreshBatchDisplay();
    }

    /// <summary>
    /// Updates clearing rules for the current batch display once payout has fully completed.
    /// </summary>
    private void UpdateBatchDisplayLifecycle()
    {
        if (!HasActiveBatch)
        {
            RefreshBatchDisplay();
            return;
        }

        if (CurrentCycleState == MachineCycleState.Paying || GetCurrentBatchRemainingMinorUnits() > 0)
        {
            return;
        }

        if (GetValidQueuedOrePickupCount() > 0)
        {
            ClearBatchDisplayState();
            CurrentCycleState = MachineCycleState.Crushing;
            OreConsumeTimer = 0f;
            return;
        }

        if (BatchCompletedTime < 0f)
        {
            BatchCompletedTime = Time.time;
        }

        if (Time.time - BatchCompletedTime < Mathf.Max(0f, BatchClearDelay))
        {
            return;
        }

        ClearBatchDisplayState();
        CurrentCycleState = MachineCycleState.Idle;
    }

    /// <summary>
    /// Starts a new visible processing batch and resets all displayed monetary counters.
    /// </summary>
    private void StartNewBatch()
    {
        CurrentBatchId++;
        HasActiveBatch = true;
        CurrentBatchTotalMinorUnits = 0;
        CurrentBatchPaidMinorUnits = 0;
        LastBatchOreProcessedTime = -1f;
        BatchCompletedTime = -1f;
        HasPayoutCompositionFailure = false;

        RefreshBatchDisplay();
    }

    /// <summary>
    /// Clears the current visible batch counters and hides or empties the assigned display texts.
    /// </summary>
    private void ClearBatchDisplayState()
    {
        HasActiveBatch = false;
        CurrentBatchTotalMinorUnits = 0;
        CurrentBatchPaidMinorUnits = 0;
        LastBatchOreProcessedTime = -1f;
        BatchCompletedTime = -1f;
        HasPayoutCompositionFailure = false;

        RefreshBatchDisplay();
    }

    /// <summary>
    /// Gets the remaining unpaid value for the current visible batch.
    /// </summary>
    /// <returns>Remaining unpaid value in minor currency units.</returns>
    private int GetCurrentBatchRemainingMinorUnits()
    {
        return Mathf.Max(0, CurrentBatchTotalMinorUnits - CurrentBatchPaidMinorUnits);
    }

    /// <summary>
    /// Writes current batch values into the configured display outputs.
    /// The primary value uses Damage Numbers Pro when configured, otherwise it falls back to TextMeshProUGUI.
    /// The primary value behaves as the single machine number: total while crushing/waiting, remaining while paying.
    /// </summary>
    private void RefreshBatchDisplay()
    {
        bool ShouldShowDisplay = HasActiveBatch && CurrentBatchTotalMinorUnits > 0;

        if (BatchDisplayRoot != null && HideBatchDisplayWhenEmpty)
        {
            BatchDisplayRoot.SetActive(ShouldShowDisplay);
        }

        if (!ShouldShowDisplay)
        {
            ClearDamageNumberProBatchDisplay();
            SetBatchText(BatchTotalValueText, string.Empty);
            SetBatchText(BatchPaidValueText, string.Empty);
            SetBatchText(BatchRemainingValueText, string.Empty);
            return;
        }

        bool ShouldDisplayRemainingAsPrimary = CurrentCycleState == MachineCycleState.Paying ||
            (BatchCompletedTime >= 0f && GetCurrentBatchRemainingMinorUnits() <= 0);

        int PrimaryDisplayedMinorUnits = ShouldDisplayRemainingAsPrimary
            ? GetCurrentBatchRemainingMinorUnits()
            : CurrentBatchTotalMinorUnits;

        bool IsDamageNumberDisplayAvailable = CanUseDamageNumberProBatchDisplay();

        if (IsDamageNumberDisplayAvailable)
        {
            UpdateDamageNumberProBatchDisplay(PrimaryDisplayedMinorUnits);
        }
        else
        {
            ClearDamageNumberProBatchDisplay();
        }

        bool ShouldWriteLegacyPrimaryText = !IsDamageNumberDisplayAvailable || !HideLegacyPrimaryTextWhenUsingDamageNumberPro;
        SetBatchText(BatchTotalValueText, ShouldWriteLegacyPrimaryText ? FormatCurrency(PrimaryDisplayedMinorUnits) : string.Empty);
        SetBatchText(BatchPaidValueText, FormatCurrency(CurrentBatchPaidMinorUnits));
        SetBatchText(BatchRemainingValueText, FormatCurrency(GetCurrentBatchRemainingMinorUnits()));
    }

    /// <summary>
    /// Returns whether the primary batch value can currently be rendered by Damage Numbers Pro.
    /// </summary>
    /// <returns>True when the feature is enabled and all required references are configured.</returns>
    private bool CanUseDamageNumberProBatchDisplay()
    {
        return UseDamageNumberProBatchDisplay &&
            BatchValueNumberPrefab != null &&
            GetBatchValueNumberParent() != null;
    }

    /// <summary>
    /// Gets the configured Damage Numbers Pro GUI parent, falling back to the batch display root RectTransform when possible.
    /// </summary>
    /// <returns>RectTransform used as the GUI popup parent.</returns>
    private RectTransform GetBatchValueNumberParent()
    {
        if (BatchValueNumberParent != null)
        {
            return BatchValueNumberParent;
        }

        if (BatchDisplayRoot == null)
        {
            return null;
        }

        return BatchDisplayRoot.GetComponent<RectTransform>();
    }

    /// <summary>
    /// Spawns or updates the persistent Damage Numbers Pro GUI value used as the main batch display.
    /// </summary>
    /// <param name="PrimaryMinorUnits">Primary monetary value in minor currency units.</param>
    private void UpdateDamageNumberProBatchDisplay(int PrimaryMinorUnits)
    {
        if (!CanUseDamageNumberProBatchDisplay())
        {
            return;
        }

        RectTransform Parent = GetBatchValueNumberParent();
        float PrimaryValue = FromMinorUnits(PrimaryMinorUnits);

        if (ActiveBatchValueNumber == null)
        {
            ActiveBatchValueNumber = BatchValueNumberPrefab.SpawnGUI(
                Parent,
                BatchValueNumberAnchoredPosition,
                PrimaryValue);

            if (ActiveBatchValueNumber != null)
            {
                ActiveBatchValueNumber.permanent = true;
                LastDamageNumberPrimaryMinorUnits = int.MinValue;
            }
        }

        if (ActiveBatchValueNumber == null)
        {
            return;
        }

        if (!ActiveBatchValueNumber.gameObject.activeSelf)
        {
            ActiveBatchValueNumber.gameObject.SetActive(true);
            LastDamageNumberPrimaryMinorUnits = int.MinValue;
        }

        ApplyDamageNumberRectTransform(Parent);

        if (LastDamageNumberPrimaryMinorUnits == PrimaryMinorUnits)
        {
            return;
        }

        ActiveBatchValueNumber.number = PrimaryValue;
        ActiveBatchValueNumber.enableNumber = true;

        if (UseCurrencySuffixAsDamageNumberRightText)
        {
            ActiveBatchValueNumber.rightText = CurrencySuffix;
            ActiveBatchValueNumber.enableRightText = !string.IsNullOrEmpty(CurrencySuffix);
        }

        ActiveBatchValueNumber.UpdateText();
        LastDamageNumberPrimaryMinorUnits = PrimaryMinorUnits;
    }

    /// <summary>
    /// Forces the active Damage Numbers Pro GUI instance to stay under the expected canvas parent.
    /// This avoids invisible numbers caused by prefab-side anchors, scale or runtime reparenting.
    /// </summary>
    /// <param name="Parent">RectTransform that should own the GUI popup.</param>
    private void ApplyDamageNumberRectTransform(RectTransform Parent)
    {
        if (!ForceDamageNumberRectTransform || ActiveBatchValueNumber == null || Parent == null)
        {
            return;
        }

        RectTransform ActiveRectTransform = ActiveBatchValueNumber.GetComponent<RectTransform>();

        if (ActiveRectTransform == null)
        {
            return;
        }

        if (ActiveRectTransform.parent != Parent)
        {
            ActiveRectTransform.SetParent(Parent, false);
        }

        ActiveRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        ActiveRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        ActiveRectTransform.pivot = new Vector2(0.5f, 0.5f);
        ActiveRectTransform.anchoredPosition = BatchValueNumberAnchoredPosition;
        ActiveRectTransform.localRotation = Quaternion.identity;
        ActiveRectTransform.localScale = Vector3.one;
    }

    /// <summary>
    /// Clears the active Damage Numbers Pro batch value when the machine display is reset.
    /// </summary>
    private void ClearDamageNumberProBatchDisplay()
    {
        if (ActiveBatchValueNumber == null)
        {
            LastDamageNumberPrimaryMinorUnits = int.MinValue;
            return;
        }

        if (KeepDamageNumberInstanceWhenCleared)
        {
            ActiveBatchValueNumber.gameObject.SetActive(false);
            LastDamageNumberPrimaryMinorUnits = int.MinValue;
            return;
        }

        if (FadeDamageNumberOnClear)
        {
            ActiveBatchValueNumber.FadeOut();
        }
        else
        {
            Destroy(ActiveBatchValueNumber.gameObject);
        }

        ActiveBatchValueNumber = null;
        LastDamageNumberPrimaryMinorUnits = int.MinValue;
    }

    /// <summary>
    /// Assigns text safely when a TextMeshPro reference is configured.
    /// </summary>
    /// <param name="TargetText">Target TextMeshPro component.</param>
    /// <param name="Value">String value to display.</param>
    private void SetBatchText(TextMeshProUGUI TargetText, string Value)
    {
        if (TargetText == null)
        {
            return;
        }

        TargetText.text = Value;
    }

    /// <summary>
    /// Formats a minor-unit currency value for the machine display.
    /// </summary>
    /// <param name="MinorUnits">Currency value in minor units.</param>
    /// <returns>Formatted currency text.</returns>
    private string FormatCurrency(int MinorUnits)
    {
        return FromMinorUnits(MinorUnits).ToString("0.00") + CurrencySuffix;
    }

    private void LogQueuedOre(OreItemData OreItemData)
    {
        string OreName = OreItemData != null && OreItemData.GetOreDefinition() != null
            ? OreItemData.GetOreDefinition().GetDisplayName()
            : "UnknownOre";

        Log(
            "Queued ore sale: " + OreName +
            " | Credits: " + (OreItemData != null ? OreItemData.GetCreditValue().ToString("0.00") : "0.00") +
            " | Pending ore queue: " + PendingOreSales.Count);
    }

    private void LogProcessedOre(OreItemData OreItemData)
    {
        string OreName = OreItemData != null && OreItemData.GetOreDefinition() != null
            ? OreItemData.GetOreDefinition().GetDisplayName()
            : "UnknownOre";

        Log(
            "Processed ore sale: " + OreName +
            " | Credits queued: " + (OreItemData != null ? OreItemData.GetCreditValue().ToString("0.00") : "0.00") +
            " | LegacyResearchDisabled: 0.00" +
            " | Pending money pieces: " + PendingMoneyEmissions.Count);
    }

    /// <summary>
    /// Forces configured processing animators to a safe paused pose when this component is disabled.
    /// </summary>
    private void OnDisable()
    {
        IsProcessingAnimationActive = false;
        IsProcessingAnimationStopRequested = false;
        ClearDamageNumberProBatchDisplay();

        if (ProcessingAnimators == null)
        {
            return;
        }

        for (int Index = 0; Index < ProcessingAnimators.Length; Index++)
        {
            PauseProcessingAnimatorAtRestPose(ProcessingAnimators[Index]);
        }
    }

    private static int ToMinorUnits(float Value)
    {
        return Mathf.Max(0, Mathf.RoundToInt(Value * CurrencyMinorUnitFactor));
    }

    private static float FromMinorUnits(int MinorUnits)
    {
        return Mathf.Max(0f, MinorUnits / (float)CurrencyMinorUnitFactor);
    }

    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[OreSellTrigger] " + Message, this);
    }
}
using System.Collections.Generic;
using UnityEngine;

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
    /// <summary>
    /// Runtime precision used for every exact payout decomposition.
    /// 100 means the smallest supported physical unit is 0.01 currency.
    /// </summary>
    private const int CurrencyMinorUnitFactor = 100;

    [System.Serializable]
    private sealed class MoneyDenomination
    {
        [Tooltip("Unique label used only for inspector readability and debug logs.")]
        [SerializeField] private string Id = "Coin";

        [Tooltip("Money prefab emitted for this denomination.")]
        [SerializeField] private GameObject Prefab;

        [Tooltip("Fixed gold value represented by this denomination.")]
        [SerializeField] private float GoldValue = 1f;

        [Tooltip("Relative random weight used when multiple prefabs share the same fixed value.")]
        [SerializeField] private int Weight = 1;

        /// <summary>
        /// Gets the denomination display id.
        /// </summary>
        public string GetId()
        {
            return Id;
        }

        /// <summary>
        /// Gets the emitted prefab.
        /// </summary>
        public GameObject GetPrefab()
        {
            return Prefab;
        }

        /// <summary>
        /// Gets the fixed gold value of this denomination.
        /// </summary>
        public float GetGoldValue()
        {
            return Mathf.Max(0.01f, GoldValue);
        }

        /// <summary>
        /// Gets the fixed gold value converted to exact minor currency units.
        /// </summary>
        public int GetGoldValueMinorUnits()
        {
            return Mathf.Max(1, ToMinorUnits(GoldValue));
        }

        /// <summary>
        /// Gets the random selection weight.
        /// </summary>
        public int GetWeight()
        {
            return Mathf.Max(1, Weight);
        }
    }

    /// <summary>
    /// Runtime entry describing one queued ore waiting to be processed by the machine.
    /// </summary>
    private sealed class PendingOreSale
    {
        /// <summary>
        /// Original ore pickup still physically stored in the machine until consumed.
        /// </summary>
        public OrePickup OrePickup;

        /// <summary>
        /// Runtime ore payload used to calculate money and research.
        /// </summary>
        public OreItemData OreItemData;
    }

    /// <summary>
    /// Runtime entry describing one exact money denomination waiting to be emitted.
    /// </summary>
    private sealed class PendingMoneyEmission
    {
        /// <summary>
        /// Denomination selected for this exact payout piece.
        /// </summary>
        public MoneyDenomination Denomination;
    }

    [Header("References")]
    [Tooltip("Wallet optionally used for non-physical research payouts.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Tooltip("Pool used to reuse money prefabs instead of instantiating and destroying them.")]
    [SerializeField] private MoneyPickupPool MoneyPickupPool;

    [Tooltip("World point and orientation used to eject coins and bills.")]
    [SerializeField] private Transform MoneyEjectPoint;

    [Header("Ore Processing")]
    [Tooltip("Time in seconds between each consumed ore while the machine has pending minerals.")]
    [SerializeField] private float OreConsumeInterval = 0.5f;

    [Header("Dynamic Emission")]
    [Tooltip("Slowest possible interval used when the pending money queue is small.")]
    [SerializeField] private float MaxEmissionInterval = 0.5f;

    [Tooltip("Fastest possible interval used when the pending money queue is very large.")]
    [SerializeField] private float MinEmissionInterval = 0.03f;

    [Tooltip("Amount of pending emitted pieces required to reach the minimum emission interval.")]
    [SerializeField] private int FastEmissionQueueThreshold = 100;

    [Header("Emission")]
    [Tooltip("Forward impulse applied to emitted money rigidbodies.")]
    [SerializeField] private float ForwardImpulse = 5f;

    [Tooltip("Upward impulse applied to emitted money rigidbodies.")]
    [SerializeField] private float UpwardImpulse = 1.5f;

    [Tooltip("Random sphere impulse added to make the cashier output feel less robotic.")]
    [SerializeField] private float RandomImpulse = 0.75f;

    [Tooltip("Optional torque impulse added to emitted money rigidbodies.")]
    [SerializeField] private float RandomTorqueImpulse = 0.25f;

    [Header("Payout Rules")]
    [Tooltip("If true, research is still granted instantly when the ore is consumed by the machine.")]
    [SerializeField] private bool GrantResearchInstantly = false;

    [Tooltip("Available physical denominations used to compose the emitted gold value exactly.")]
    [SerializeField] private List<MoneyDenomination> MoneyDenominations = new();

    [Header("Payout Variation")]
    [Tooltip("Chance to use a valid alternative composition instead of the fully optimal one.")]
    [SerializeField][Range(0f, 1f)] private float AlternativeCompositionChance = 0.2f;

    [Tooltip("If true, emitted denomination order is shuffled slightly for visual variation.")]
    [SerializeField] private bool ShuffleEmissionOrder = true;

    [Header("Debug")]
    [Tooltip("Logs sales, processed ore, denomination decomposition and money emissions.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Ore waiting to be consumed by the machine.
    /// </summary>
    private readonly Queue<PendingOreSale> PendingOreSales = new();

    /// <summary>
    /// Exact physical denomination pieces waiting to be emitted.
    /// </summary>
    private readonly Queue<PendingMoneyEmission> PendingMoneyEmissions = new();

    /// <summary>
    /// Cached valid denominations sorted from higher to lower fixed value.
    /// </summary>
    private readonly List<MoneyDenomination> SortedDenominations = new();

    /// <summary>
    /// Tracks ore pickups already queued to avoid duplicate trigger entries.
    /// </summary>
    private readonly HashSet<OrePickup> QueuedOrePickups = new();

    /// <summary>
    /// Timer controlling the next ore consumption step.
    /// </summary>
    private float OreConsumeTimer;

    /// <summary>
    /// Timer controlling the next money emission step.
    /// </summary>
    private float MoneyEmissionTimer;

    /// <summary>
    /// Current amount of ore payloads waiting to be processed by the machine.
    /// </summary>
    public int PendingSaleCount => PendingOreSales.Count;

    /// <summary>
    /// Current amount of physical money pieces waiting to be emitted.
    /// </summary>
    public int PendingMoneyEmissionCount => PendingMoneyEmissions.Count;

    /// <summary>
    /// Validates required references and caches the sorted denomination list.
    /// </summary>
    private void Awake()
    {
        CacheSortedDenominations();

        if (MoneyEjectPoint == null)
        {
            MoneyEjectPoint = transform;
        }
    }

    /// <summary>
    /// Processes ore consumption and money emission with separate timers.
    /// </summary>
    private void Update()
    {
        UpdateOreConsumption();
        UpdateMoneyEmission();
    }

    /// <summary>
    /// Receives ore pickups entering the sale trigger.
    /// </summary>
    private void OnTriggerEnter(Collider Other)
    {
        TryQueueSaleFromCollider(Other);
    }

    /// <summary>
    /// Attempts to queue one ore pickup resolved from the provided collider.
    /// </summary>
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

    /// <summary>
    /// Queues a physical ore pickup for delayed machine consumption.
    /// The ore object is not returned to pool yet. That only happens when the machine consumes it.
    /// </summary>
    public bool TryQueueSalePickup(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
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

        LogQueuedOre(OreItemData);

        return true;
    }

    /// <summary>
    /// Updates the ore consumption timer and processes one queued ore when ready.
    /// </summary>
    private void UpdateOreConsumption()
    {
        if (PendingOreSales.Count == 0)
        {
            return;
        }

        OreConsumeTimer -= Time.deltaTime;

        if (OreConsumeTimer > 0f)
        {
            return;
        }

        OreConsumeTimer = Mathf.Max(0.01f, OreConsumeInterval);
        ConsumeNextQueuedOre();
    }

    /// <summary>
    /// Updates the money emission timer and emits one pending denomination when ready.
    /// </summary>
    private void UpdateMoneyEmission()
    {
        if (PendingMoneyEmissions.Count == 0)
        {
            return;
        }

        MoneyEmissionTimer -= Time.deltaTime;

        if (MoneyEmissionTimer > 0f)
        {
            return;
        }

        MoneyEmissionTimer = GetCurrentEmissionInterval();
        EmitNextMoneyPiece();
    }

    /// <summary>
    /// Consumes one ore from the machine queue, returns the ore pickup to its pool,
    /// grants optional research and enqueues exact denomination payouts.
    /// </summary>
    private void ConsumeNextQueuedOre()
    {
        if (PendingOreSales.Count == 0)
        {
            return;
        }

        PendingOreSale PendingOreSale = PendingOreSales.Dequeue();

        if (PendingOreSale == null)
        {
            return;
        }

        OrePickup OrePickup = PendingOreSale.OrePickup;
        OreItemData OreItemData = PendingOreSale.OreItemData;

        if (OrePickup != null)
        {
            QueuedOrePickups.Remove(OrePickup);

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

        float GoldValue = Mathf.Max(0f, OreItemData.GetGoldValue());

        if (GoldValue > 0f)
        {
            bool CouldBuildExactPayout = TryEnqueueExactMoneyPayout(GoldValue);

            if (!CouldBuildExactPayout)
            {
                Log(
                    "Failed to build exact payout for ore value " + GoldValue.ToString("0.00") +
                    ". Check configured denominations. At least one denomination combination is missing."
                );
            }
        }

        if (GrantResearchInstantly && CurrencyWallet != null)
        {
            float ResearchValue = Mathf.Max(0f, OreItemData.GetResearchValue());

            if (ResearchValue > 0f)
            {
                CurrencyWallet.AddCurrency(global::CurrencyWallet.CurrencyType.Research, ResearchValue);
            }
        }

        LogProcessedOre(OreItemData);
    }

    /// <summary>
    /// Attempts to decompose an exact gold value into configured fixed denominations
    /// and enqueue every resulting physical money piece.
    /// </summary>
    private bool TryEnqueueExactMoneyPayout(float GoldValue)
    {
        int TargetMinorUnits = ToMinorUnits(GoldValue);

        if (TargetMinorUnits <= 0)
        {
            return true;
        }

        List<MoneyDenomination> Result = BuildExactDenominationComposition(TargetMinorUnits);

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
            PendingMoneyEmissions.Enqueue(new PendingMoneyEmission
            {
                Denomination = Result[Index]
            });
        }

        Log(
            "Queued exact payout for gold value " + GoldValue.ToString("0.00") +
            " | Pieces: " + Result.Count
        );

        return true;
    }

    /// <summary>
    /// Emits the next queued physical denomination.
    /// </summary>
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

        EmitMoneyDenomination(PendingMoneyEmission.Denomination);
    }

    /// <summary>
    /// Emits one physical money object using the provided fixed denomination.
    /// </summary>
    private void EmitMoneyDenomination(MoneyDenomination Denomination)
    {
        if (Denomination == null || Denomination.GetPrefab() == null)
        {
            Log("Skipped money emission because the denomination or its prefab is invalid.");
            return;
        }

        MoneyPickup MoneyPickup = null;

        if (MoneyPickupPool != null)
        {
            MoneyPickup = MoneyPickupPool.GetPickup(
                Denomination.GetPrefab(),
                MoneyEjectPoint.position,
                MoneyEjectPoint.rotation);
        }

        if (MoneyPickup == null)
        {
            GameObject Instance = Instantiate(Denomination.GetPrefab(), MoneyEjectPoint.position, MoneyEjectPoint.rotation);
            MoneyPickup = Instance.GetComponent<MoneyPickup>();

            if (MoneyPickup == null)
            {
                MoneyPickup = Instance.GetComponentInChildren<MoneyPickup>(true);
            }
        }

        if (MoneyPickup == null)
        {
            Log("Failed to create money pickup for denomination " + Denomination.GetId() + ".");
            return;
        }

        MoneyPickup.Initialize(Denomination.GetGoldValue(), CurrencyWallet.CurrencyType.Gold);
        ApplyEmissionImpulse(MoneyPickup);

        Log(
            "Emitted denomination | Id: " + Denomination.GetId() +
            " | Value: " + Denomination.GetGoldValue().ToString("0.00") +
            " | Remaining pending pieces: " + PendingMoneyEmissions.Count
        );
    }

    /// <summary>
    /// Applies the configured launch impulse to a newly emitted money pickup.
    /// </summary>
    private void ApplyEmissionImpulse(MoneyPickup MoneyPickup)
    {
        Rigidbody MoneyRigidbody = MoneyPickup.GetCachedRigidbody();

        if (MoneyRigidbody == null)
        {
            return;
        }

        Vector3 Impulse = MoneyEjectPoint.forward * ForwardImpulse;
        Impulse += MoneyEjectPoint.up * UpwardImpulse;
        Impulse += Random.insideUnitSphere * RandomImpulse;

        MoneyRigidbody.AddForce(Impulse, ForceMode.Impulse);

        if (RandomTorqueImpulse > 0f)
        {
            MoneyRigidbody.AddTorque(Random.insideUnitSphere * RandomTorqueImpulse, ForceMode.Impulse);
        }
    }

    /// <summary>
    /// Builds an exact payout composition using the configured fixed denominations.
    /// Most of the time it returns the optimal composition.
    /// Sometimes it expands one denomination into several smaller exact pieces for variation.
    /// </summary>
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

        Dictionary<int, List<MoneyDenomination>> DenominationsByMinorUnits = new Dictionary<int, List<MoneyDenomination>>();
        List<int> UniqueMinorUnitValues = new List<int>();

        for (int Index = 0; Index < SortedDenominations.Count; Index++)
        {
            MoneyDenomination Denomination = SortedDenominations[Index];
            int Value = Denomination.GetGoldValueMinorUnits();

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

        List<int> FinalValueComposition = new List<int>(OptimalValueComposition);

        if (AlternativeCompositionChance > 0f && Random.value < AlternativeCompositionChance)
        {
            if (TryApplyAlternativeExpansion(FinalValueComposition, UniqueMinorUnitValues))
            {
                Log(
                    "Using alternative payout composition for value " +
                    FromMinorUnits(TargetMinorUnits).ToString("0.00") +
                    " | Piece count: " + FinalValueComposition.Count
                );
            }
        }

        List<MoneyDenomination> Result = new List<MoneyDenomination>();

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

    /// <summary>
    /// Builds the optimal exact composition by minimizing total piece count.
    /// </summary>
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

        List<int> Result = new List<int>();
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

    /// <summary>
    /// Tries to expand one optimal denomination into several smaller exact denominations.
    /// This keeps the payout exact while adding controlled visual variation.
    /// </summary>
    private bool TryApplyAlternativeExpansion(List<int> CompositionValues, List<int> UniqueValues)
    {
        if (CompositionValues == null || CompositionValues.Count == 0)
        {
            return false;
        }

        List<int> CandidateIndices = new List<int>();

        for (int Index = 0; Index < CompositionValues.Count; Index++)
        {
            if (CompositionValues[Index] > 1)
            {
                CandidateIndices.Add(Index);
            }
        }

        if (CandidateIndices.Count == 0)
        {
            return false;
        }

        ShuffleIntList(CandidateIndices);

        for (int CandidateIndex = 0; CandidateIndex < CandidateIndices.Count; CandidateIndex++)
        {
            int ReplaceIndex = CandidateIndices[CandidateIndex];
            int ValueToExpand = CompositionValues[ReplaceIndex];

            List<int> SmallerValues = new List<int>();

            for (int ValueIndex = 0; ValueIndex < UniqueValues.Count; ValueIndex++)
            {
                int CandidateValue = UniqueValues[ValueIndex];

                if (CandidateValue < ValueToExpand)
                {
                    SmallerValues.Add(CandidateValue);
                }
            }

            if (SmallerValues.Count == 0)
            {
                continue;
            }

            List<int> ExpandedValues = BuildOptimalValueComposition(ValueToExpand, SmallerValues);

            if (ExpandedValues == null || ExpandedValues.Count <= 1)
            {
                continue;
            }

            CompositionValues.RemoveAt(ReplaceIndex);
            CompositionValues.InsertRange(ReplaceIndex, ExpandedValues);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Selects one denomination from a bucket of equal-value prefabs using weights.
    /// </summary>
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

        int Roll = Random.Range(0, TotalWeight);
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

    /// <summary>
    /// Returns the dynamic emission interval based on the amount of pending money pieces.
    /// Larger queues emit faster so large payouts do not take too long.
    /// </summary>
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

    /// <summary>
    /// Rebuilds the internal denomination cache sorted from higher to lower value.
    /// </summary>
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
            (Left, Right) => Right.GetGoldValueMinorUnits().CompareTo(Left.GetGoldValueMinorUnits())
        );
    }

    /// <summary>
    /// Shuffles a denomination list in place using Fisher-Yates.
    /// </summary>
    private void ShuffleDenominationList(List<MoneyDenomination> Denominations)
    {
        if (Denominations == null || Denominations.Count <= 1)
        {
            return;
        }

        for (int Index = Denominations.Count - 1; Index > 0; Index--)
        {
            int SwapIndex = Random.Range(0, Index + 1);
            MoneyDenomination Cached = Denominations[Index];
            Denominations[Index] = Denominations[SwapIndex];
            Denominations[SwapIndex] = Cached;
        }
    }

    /// <summary>
    /// Shuffles an integer list in place using Fisher-Yates.
    /// </summary>
    private void ShuffleIntList(List<int> Values)
    {
        if (Values == null || Values.Count <= 1)
        {
            return;
        }

        for (int Index = Values.Count - 1; Index > 0; Index--)
        {
            int SwapIndex = Random.Range(0, Index + 1);
            int Cached = Values[Index];
            Values[Index] = Values[SwapIndex];
            Values[SwapIndex] = Cached;
        }
    }

    /// <summary>
    /// Logs one ore payload that has entered the machine queue.
    /// </summary>
    private void LogQueuedOre(OreItemData OreItemData)
    {
        string OreName = OreItemData != null && OreItemData.GetOreDefinition() != null
            ? OreItemData.GetOreDefinition().GetDisplayName()
            : "UnknownOre";

        Log(
            "Queued ore sale: " + OreName +
            " | Gold: " + (OreItemData != null ? OreItemData.GetGoldValue().ToString("0.00") : "0.00") +
            " | Research: " + (OreItemData != null ? OreItemData.GetResearchValue().ToString("0.00") : "0.00") +
            " | Pending ore queue: " + PendingOreSales.Count
        );
    }

    /// <summary>
    /// Logs one ore payload after the machine has consumed it.
    /// </summary>
    private void LogProcessedOre(OreItemData OreItemData)
    {
        string OreName = OreItemData != null && OreItemData.GetOreDefinition() != null
            ? OreItemData.GetOreDefinition().GetDisplayName()
            : "UnknownOre";

        Log(
            "Processed ore sale: " + OreName +
            " | Gold queued: " + (OreItemData != null ? OreItemData.GetGoldValue().ToString("0.00") : "0.00") +
            " | Research granted instantly: " +
            (GrantResearchInstantly && OreItemData != null ? OreItemData.GetResearchValue().ToString("0.00") : "0.00") +
            " | Pending money pieces: " + PendingMoneyEmissions.Count
        );
    }

    /// <summary>
    /// Converts a float currency amount to exact minor units using the configured precision.
    /// </summary>
    private static int ToMinorUnits(float Value)
    {
        return Mathf.Max(0, Mathf.RoundToInt(Value * CurrencyMinorUnitFactor));
    }

    /// <summary>
    /// Converts integer minor units back to float currency amount.
    /// </summary>
    private static float FromMinorUnits(int MinorUnits)
    {
        return Mathf.Max(0f, MinorUnits / (float)CurrencyMinorUnitFactor);
    }

    /// <summary>
    /// Logs machine messages if debug logging is enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[OreSellTrigger] " + Message, this);
    }
}
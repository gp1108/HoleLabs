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
    private const int CurrencyMinorUnitFactor = 100;

    private enum MoneyVisualType
    {
        Coin = 0,
        Bill = 1
    }

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

        public float GetGoldValue()
        {
            return Mathf.Max(0.01f, GoldValue);
        }

        public int GetGoldValueMinorUnits()
        {
            return Mathf.Max(1, ToMinorUnits(GoldValue));
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
    }

    [Header("References")]
    [Tooltip("Wallet optionally used for non-physical research payouts.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Tooltip("Pool used to reuse money prefabs instead of instantiating and destroying them.")]
    [SerializeField] private MoneyPickupPool MoneyPickupPool;

    [Header("Eject Points")]
    [Tooltip("World point and orientation used to eject coins.")]
    [SerializeField] private Transform CoinEjectPoint;

    [Tooltip("World point and orientation used to eject bills.")]
    [SerializeField] private Transform BillEjectPoint;

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

    private readonly Queue<PendingOreSale> PendingOreSales = new();
    private readonly Queue<PendingMoneyEmission> PendingMoneyEmissions = new();
    private readonly List<MoneyDenomination> SortedDenominations = new();
    private readonly HashSet<OrePickup> QueuedOrePickups = new();
    /// <summary>
    /// Tracks ore pickups currently inside the sale trigger volume.
    /// </summary>
    private readonly HashSet<OrePickup> OrePickupsInsideTrigger = new();

    private float OreConsumeTimer;
    private float MoneyEmissionTimer;

    public int PendingSaleCount => PendingOreSales.Count;
    public int PendingMoneyEmissionCount => PendingMoneyEmissions.Count;

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
    }

    /// <summary>
    /// Processes ore consumption and money emission with separate timers.
    /// </summary>
    private void Update()
    {
        UpdateOreConsumption();
        UpdateMoneyEmission();
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

        OrePickupsInsideTrigger.Add(OrePickup);
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

        OrePickupsInsideTrigger.Remove(OrePickup);
        QueuedOrePickups.Remove(OrePickup);

        Log("Removed ore pickup from active sale queue because it left the trigger: " + OrePickup.name);
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

        LogQueuedOre(OreItemData);
        return true;
    }

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
                    ". Check configured denominations. At least one denomination combination is missing.");
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

        EmitMoneyDenomination(PendingMoneyEmission.Denomination);
    }

    private void EmitMoneyDenomination(MoneyDenomination Denomination)
    {
        if (Denomination == null || Denomination.GetPrefab() == null)
        {
            Log("Skipped money emission because the denomination or its prefab is invalid.");
            return;
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
        }

        if (MoneyPickup == null)
        {
            Log("Failed to create money pickup for denomination " + Denomination.GetId() + ".");
            return;
        }

        MoneyPickup.Initialize(Denomination.GetGoldValue(), CurrencyWallet.CurrencyType.Gold);
        ApplyEmissionImpulse(MoneyPickup, Denomination.GetVisualType(), EjectPoint);

        Log(
            "Emitted denomination | Id: " + Denomination.GetId() +
            " | Value: " + Denomination.GetGoldValue().ToString("0.00") +
            " | VisualType: " + Denomination.GetVisualType() +
            " | Remaining pending pieces: " + PendingMoneyEmissions.Count);
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
        Impulse += Random.insideUnitSphere * RandomImpulse;

        MoneyRigidbody.AddForce(Impulse, ForceMode.Impulse);

        if (RandomTorqueImpulse > 0f)
        {
            MoneyRigidbody.AddTorque(Random.insideUnitSphere * RandomTorqueImpulse, ForceMode.Impulse);
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

        List<int> FinalValueComposition = new(OptimalValueComposition);

        if (AlternativeCompositionChance > 0f && Random.value < AlternativeCompositionChance)
        {
            if (TryApplyAlternativeExpansion(FinalValueComposition, UniqueMinorUnitValues))
            {
                Log(
                    "Using alternative payout composition for value " +
                    FromMinorUnits(TargetMinorUnits).ToString("0.00") +
                    " | Piece count: " + FinalValueComposition.Count);
            }
        }

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

    private bool TryApplyAlternativeExpansion(List<int> CompositionValues, List<int> UniqueValues)
    {
        if (CompositionValues == null || CompositionValues.Count == 0)
        {
            return false;
        }

        List<int> CandidateIndices = new();

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

            List<int> SmallerValues = new();

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
            (Left, Right) => Right.GetGoldValueMinorUnits().CompareTo(Left.GetGoldValueMinorUnits()));
    }

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

    private void LogQueuedOre(OreItemData OreItemData)
    {
        string OreName = OreItemData != null && OreItemData.GetOreDefinition() != null
            ? OreItemData.GetOreDefinition().GetDisplayName()
            : "UnknownOre";

        Log(
            "Queued ore sale: " + OreName +
            " | Gold: " + (OreItemData != null ? OreItemData.GetGoldValue().ToString("0.00") : "0.00") +
            " | Research: " + (OreItemData != null ? OreItemData.GetResearchValue().ToString("0.00") : "0.00") +
            " | Pending ore queue: " + PendingOreSales.Count);
    }

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
            " | Pending money pieces: " + PendingMoneyEmissions.Count);
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
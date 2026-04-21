using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEngine;

/// <summary>
/// Debug-only save/load entry point for the current gameplay scene.
/// This implementation stores only pure data and avoids saving Unity object references
/// inside the Easy Save payload.
/// </summary>
public sealed class GameSaveDebugController : MonoBehaviour
{
    private const string SaveFileName = "save_debug.es3";
    private const string SaveRootKey = "debug_save_root";
    private static bool HasPendingLoadRequest;
    private static string PendingLoadFileName;
    private static string PendingLoadRootKey;

    [Serializable]
    private sealed class ItemInstanceData
    {
        [SerializeField] private string ItemId;
        [SerializeField] private int Amount;
        [SerializeField] private int UpgradeLevel;
        [SerializeField] private float Durability;

        public static ItemInstanceData FromRuntime(ItemInstance RuntimeItem)
        {
            if (RuntimeItem == null || RuntimeItem.GetDefinition() == null)
            {
                return null;
            }

            return new ItemInstanceData
            {
                ItemId = RuntimeItem.GetDefinition().GetItemId(),
                Amount = RuntimeItem.GetAmount(),
                UpgradeLevel = RuntimeItem.GetUpgradeLevel(),
                Durability = RuntimeItem.GetDurability()
            };
        }

        public ItemInstance ToRuntime(Dictionary<string, ItemDefinition> DefinitionsById)
        {
            if (DefinitionsById == null || string.IsNullOrWhiteSpace(ItemId))
            {
                return null;
            }

            if (!DefinitionsById.TryGetValue(ItemId, out ItemDefinition Definition) || Definition == null)
            {
                return null;
            }

            return new ItemInstance(Definition, Amount, UpgradeLevel, Durability);
        }
    }

    [Serializable]
    private sealed class OrePropertyValueData
    {
        [SerializeField] private OrePropertyType PropertyType;
        [SerializeField] private float Value;

        public OrePropertyType GetPropertyType() => PropertyType;
        public float GetValue() => Value;

        public OrePropertyValueData(OrePropertyType PropertyTypeValue, float ValueValue)
        {
            PropertyType = PropertyTypeValue;
            Value = ValueValue;
        }
    }

    [Serializable]
    private sealed class OreItemDataSaveData
    {
        [SerializeField] private string OreId;
        [SerializeField] private float GoldValue;
        [SerializeField] private float ResearchValue;
        [SerializeField] private float WeightValue;
        [SerializeField] private List<OrePropertyValueData> Properties = new();

        public static OreItemDataSaveData FromRuntime(OreItemData RuntimeData)
        {
            if (RuntimeData == null || RuntimeData.GetOreDefinition() == null)
            {
                return null;
            }

            OreItemDataSaveData Result = new OreItemDataSaveData
            {
                OreId = RuntimeData.GetOreDefinition().GetOreId(),
                GoldValue = RuntimeData.GetGoldValue(),
                ResearchValue = RuntimeData.GetResearchValue(),
                WeightValue = RuntimeData.GetWeightValue()
            };

            IReadOnlyList<OreItemData.OrePropertyValue> RuntimeProperties = RuntimeData.GetProperties();

            for (int Index = 0; Index < RuntimeProperties.Count; Index++)
            {
                OreItemData.OrePropertyValue Property = RuntimeProperties[Index];

                if (Property == null)
                {
                    continue;
                }

                Result.Properties.Add(new OrePropertyValueData(
                    Property.GetPropertyType(),
                    Property.GetValue()));
            }

            return Result;
        }

        public OreItemData ToRuntime(Dictionary<string, OreDefinition> DefinitionsById)
        {
            if (DefinitionsById == null || string.IsNullOrWhiteSpace(OreId))
            {
                return null;
            }

            if (!DefinitionsById.TryGetValue(OreId, out OreDefinition Definition) || Definition == null)
            {
                return null;
            }

            OreItemData Result = new OreItemData(Definition);
            Result.SetGoldValue(GoldValue);
            Result.SetResearchValue(ResearchValue);
            Result.SetWeightValue(WeightValue);

            for (int Index = 0; Index < Properties.Count; Index++)
            {
                OrePropertyValueData Property = Properties[Index];

                if (Property == null)
                {
                    continue;
                }

                Result.SetProperty(Property.GetPropertyType(), Property.GetValue());
            }

            return Result;
        }
    }

    [Serializable]
    private sealed class PlayerSaveData
    {
        [SerializeField] private Vector3 Position;
        [SerializeField] private bool IsCrouching;

        public PlayerSaveData(Vector3 PositionValue, bool IsCrouchingValue)
        {
            Position = PositionValue;
            IsCrouching = IsCrouchingValue;
        }

        public Vector3 GetPosition() => Position;
        public bool GetIsCrouching() => IsCrouching;
    }

    [Serializable]
    private sealed class WalletSaveData
    {
        [SerializeField] private float Gold;
        [SerializeField] private float Research;

        public WalletSaveData(float GoldValue, float ResearchValue)
        {
            Gold = GoldValue;
            Research = ResearchValue;
        }

        public float GetGold() => Gold;
        public float GetResearch() => Research;
    }

    [Serializable]
    private sealed class HotbarSaveData
    {
        [SerializeField] private List<ItemInstanceData> Slots = new();
        [SerializeField] private int SelectedIndex;

        public List<ItemInstanceData> GetSlots() => Slots;
        public int GetSelectedIndex() => SelectedIndex;

        public static HotbarSaveData FromRuntime(HotbarController HotbarController)
        {
            HotbarSaveData Result = new HotbarSaveData();

            if (HotbarController == null)
            {
                return Result;
            }

            int SlotCount = HotbarController.GetSlotCount();

            for (int Index = 0; Index < SlotCount; Index++)
            {
                Result.Slots.Add(ItemInstanceData.FromRuntime(HotbarController.GetItemAtSlot(Index)));
            }

            Result.SelectedIndex = HotbarController.GetSelectedIndex();
            return Result;
        }

        public List<ItemInstance> ToRuntime(Dictionary<string, ItemDefinition> DefinitionsById)
        {
            List<ItemInstance> Result = new List<ItemInstance>(Slots.Count);

            for (int Index = 0; Index < Slots.Count; Index++)
            {
                Result.Add(Slots[Index] != null ? Slots[Index].ToRuntime(DefinitionsById) : null);
            }

            return Result;
        }
    }

    [Serializable]
    private sealed class ElevatorSaveData
    {
        [SerializeField] private float CurrentDistance;
        [SerializeField] private Quaternion Rotation;
        [SerializeField] private int VerticalLeverIndex;
        [SerializeField] private int RotationLeverIndex;

        public ElevatorSaveData(float CurrentDistanceValue, Quaternion RotationValue, int VerticalLeverIndexValue, int RotationLeverIndexValue)
        {
            CurrentDistance = CurrentDistanceValue;
            Rotation = RotationValue;
            VerticalLeverIndex = VerticalLeverIndexValue;
            RotationLeverIndex = RotationLeverIndexValue;
        }

        public float GetCurrentDistance() => CurrentDistance;
        public Quaternion GetRotation() => Rotation;
        public int GetVerticalLeverIndex() => VerticalLeverIndex;
        public int GetRotationLeverIndex() => RotationLeverIndex;
    }

    [Serializable]
    private sealed class SceneWorldItemState
    {
        [SerializeField] private string SceneId;
        [SerializeField] private bool IsPresent;
        [SerializeField] private ItemInstanceData ItemData;
        [SerializeField] private Vector3 Position;
        [SerializeField] private Quaternion Rotation;

        public SceneWorldItemState(string SceneIdValue, bool IsPresentValue, ItemInstanceData ItemDataValue, Vector3 PositionValue, Quaternion RotationValue)
        {
            SceneId = SceneIdValue;
            IsPresent = IsPresentValue;
            ItemData = ItemDataValue;
            Position = PositionValue;
            Rotation = RotationValue;
        }

        public string GetSceneId() => SceneId;
        public bool GetIsPresent() => IsPresent;
        public ItemInstanceData GetItemData() => ItemData;
        public Vector3 GetPosition() => Position;
        public Quaternion GetRotation() => Rotation;
    }

    [Serializable]
    private sealed class RuntimeWorldItemState
    {
        [SerializeField] private ItemInstanceData ItemData;
        [SerializeField] private Vector3 Position;
        [SerializeField] private Quaternion Rotation;

        public RuntimeWorldItemState(ItemInstanceData ItemDataValue, Vector3 PositionValue, Quaternion RotationValue)
        {
            ItemData = ItemDataValue;
            Position = PositionValue;
            Rotation = RotationValue;
        }

        public ItemInstanceData GetItemData() => ItemData;
        public Vector3 GetPosition() => Position;
        public Quaternion GetRotation() => Rotation;
    }

    [Serializable]
    private sealed class MoneyPickupState
    {
        [SerializeField] private string MoneyId;
        [SerializeField] private CurrencyWallet.CurrencyType CurrencyType;
        [SerializeField] private float Amount;
        [SerializeField] private Vector3 Position;
        [SerializeField] private Quaternion Rotation;

        public MoneyPickupState(string MoneyIdValue, CurrencyWallet.CurrencyType CurrencyTypeValue, float AmountValue, Vector3 PositionValue, Quaternion RotationValue)
        {
            MoneyId = MoneyIdValue;
            CurrencyType = CurrencyTypeValue;
            Amount = AmountValue;
            Position = PositionValue;
            Rotation = RotationValue;
        }

        public string GetMoneyId() => MoneyId;
        public CurrencyWallet.CurrencyType GetCurrencyType() => CurrencyType;
        public float GetAmount() => Amount;
        public Vector3 GetPosition() => Position;
        public Quaternion GetRotation() => Rotation;
    }

    [Serializable]
    private sealed class OrePickupState
    {
        [SerializeField] private string SourcePrefabName;
        [SerializeField] private OreItemDataSaveData OreData;
        [SerializeField] private Vector3 Position;
        [SerializeField] private Quaternion Rotation;

        public OrePickupState(string SourcePrefabNameValue, OreItemDataSaveData OreDataValue, Vector3 PositionValue, Quaternion RotationValue)
        {
            SourcePrefabName = SourcePrefabNameValue;
            OreData = OreDataValue;
            Position = PositionValue;
            Rotation = RotationValue;
        }

        public string GetSourcePrefabName() => SourcePrefabName;
        public OreItemDataSaveData GetOreData() => OreData;
        public Vector3 GetPosition() => Position;
        public Quaternion GetRotation() => Rotation;
    }

    [Serializable]
    private sealed class OreSpawnPointState
    {
        [SerializeField] private string SceneId;
        [SerializeField] private bool IsActive;
        [SerializeField] private string OreId;
        [SerializeField] private bool IsGrowing;
        [SerializeField] private int HitsRemaining;
        [SerializeField] private float RespawnTimerRemaining;

        public OreSpawnPointState(
            string SceneIdValue,
            bool IsActiveValue,
            string OreIdValue,
            bool IsGrowingValue,
            int HitsRemainingValue,
            float RespawnTimerRemainingValue)
        {
            SceneId = SceneIdValue;
            IsActive = IsActiveValue;
            OreId = OreIdValue;
            IsGrowing = IsGrowingValue;
            HitsRemaining = HitsRemainingValue;
            RespawnTimerRemaining = RespawnTimerRemainingValue;
        }

        public string GetSceneId() => SceneId;
        public bool GetIsActive() => IsActive;
        public string GetOreId() => OreId;
        public bool GetIsGrowing() => IsGrowing;
        public int GetHitsRemaining() => HitsRemaining;
        public float GetRespawnTimerRemaining() => RespawnTimerRemaining;
    }

    [Serializable]
    private sealed class SaveData
    {
        [SerializeField] private PlayerSaveData Player;
        [SerializeField] private WalletSaveData Wallet;
        [SerializeField] private HotbarSaveData Hotbar;
        [SerializeField] private ElevatorSaveData Elevator;
        [SerializeField] private List<UpgradeManager.UpgradeSaveEntry> UpgradeEntries = new();
        [SerializeField] private List<SceneWorldItemState> SceneWorldItems = new();
        [SerializeField] private List<RuntimeWorldItemState> RuntimeWorldItems = new();
        [SerializeField] private List<MoneyPickupState> MoneyPickups = new();
        [SerializeField] private List<OrePickupState> OrePickups = new();
        [SerializeField] private List<OreSpawnPointState> OreSpawnPoints = new();

        public PlayerSaveData GetPlayer() => Player;
        public void SetPlayer(PlayerSaveData Value) => Player = Value;

        public WalletSaveData GetWallet() => Wallet;
        public void SetWallet(WalletSaveData Value) => Wallet = Value;

        public HotbarSaveData GetHotbar() => Hotbar;
        public void SetHotbar(HotbarSaveData Value) => Hotbar = Value;

        public ElevatorSaveData GetElevator() => Elevator;
        public void SetElevator(ElevatorSaveData Value) => Elevator = Value;

        public List<UpgradeManager.UpgradeSaveEntry> GetUpgradeEntries() => UpgradeEntries;
        public void SetUpgradeEntries(List<UpgradeManager.UpgradeSaveEntry> Value) => UpgradeEntries = Value ?? new List<UpgradeManager.UpgradeSaveEntry>();

        public List<SceneWorldItemState> GetSceneWorldItems() => SceneWorldItems;
        public void SetSceneWorldItems(List<SceneWorldItemState> Value) => SceneWorldItems = Value ?? new List<SceneWorldItemState>();

        public List<RuntimeWorldItemState> GetRuntimeWorldItems() => RuntimeWorldItems;
        public void SetRuntimeWorldItems(List<RuntimeWorldItemState> Value) => RuntimeWorldItems = Value ?? new List<RuntimeWorldItemState>();

        public List<MoneyPickupState> GetMoneyPickups() => MoneyPickups;
        public void SetMoneyPickups(List<MoneyPickupState> Value) => MoneyPickups = Value ?? new List<MoneyPickupState>();

        public List<OrePickupState> GetOrePickups() => OrePickups;
        public void SetOrePickups(List<OrePickupState> Value) => OrePickups = Value ?? new List<OrePickupState>();

        public List<OreSpawnPointState> GetOreSpawnPoints() => OreSpawnPoints;
        public void SetOreSpawnPoints(List<OreSpawnPointState> Value) => OreSpawnPoints = Value ?? new List<OreSpawnPointState>();


    }

    [Header("References")]
    [Tooltip("Player controller restored from save.")]
    [SerializeField] private PlayerController PlayerController;

    [Tooltip("Hotbar runtime state restored from save.")]
    [SerializeField] private HotbarController HotbarController;

    [Tooltip("Wallet restored from save.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Tooltip("Upgrade manager restored from save.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Authoritative elevator motor restored from save.")]
    [SerializeField] private ElevatorPhysicalMotor ElevatorPhysicalMotor;

    [Tooltip("Vertical lever visual state restored from save.")]
    [SerializeField] private SnapLever VerticalSnapLever;

    [Tooltip("Rotation lever visual state restored from save.")]
    [SerializeField] private SnapLever RotationSnapLever;

    [Tooltip("Runtime ore service used to recreate ore pickups and veins.")]
    [SerializeField] private OreRuntimeService OreRuntimeService;

    [Tooltip("Ore pickup pool reused when restoring ore pickups.")]
    [SerializeField] private OrePickupPool OrePickupPool;

    [Tooltip("Money pickup pool reused when restoring money pickups.")]
    [SerializeField] private MoneyPickupPool MoneyPickupPool;

    [Header("Definition Lookup")]
    [Tooltip("All item definitions available in this gameplay scene.")]
    [SerializeField] private List<ItemDefinition> ItemDefinitions = new();

    [Tooltip("All ore definitions available in this gameplay scene.")]
    [SerializeField] private List<OreDefinition> OreDefinitions = new();

    [Tooltip("Authoritative sell trigger used to resolve money denomination prefabs from saved money ids.")]
    [SerializeField] private OreSellTrigger OreSellTrigger;

    [Header("Debug")]
    [Tooltip("Logs save and load operations.")]
    [SerializeField] private bool DebugLogs = true;

    private readonly Dictionary<string, ItemDefinition> ItemDefinitionsById = new();
    private readonly Dictionary<string, OreDefinition> OreDefinitionsById = new();
    private readonly Dictionary<string, ScenePlacedWorldItemPersistence> SceneWorldItemsById = new();
    private readonly Dictionary<string, OreSpawnPoint> OreSpawnPointsById = new();

    /// <summary>
    /// Resolves missing scene references and builds lookup caches.
    /// </summary>
    private void Awake()
    {
        if (PlayerController == null)
        {
            PlayerController = FindFirstObjectByType<PlayerController>();
        }

        if (HotbarController == null)
        {
            HotbarController = FindFirstObjectByType<HotbarController>();
        }

        if (CurrencyWallet == null)
        {
            CurrencyWallet = FindFirstObjectByType<CurrencyWallet>();
        }

        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }

        if (ElevatorPhysicalMotor == null)
        {
            ElevatorPhysicalMotor = FindFirstObjectByType<ElevatorPhysicalMotor>();
        }

        if (OreRuntimeService == null)
        {
            OreRuntimeService = FindFirstObjectByType<OreRuntimeService>();
        }

        if (OrePickupPool == null)
        {
            OrePickupPool = FindFirstObjectByType<OrePickupPool>();
        }

        if (MoneyPickupPool == null)
        {
            MoneyPickupPool = FindFirstObjectByType<MoneyPickupPool>();
        }

        if (OreSellTrigger == null)
        {
            OreSellTrigger = FindFirstObjectByType<OreSellTrigger>();
        }

        RebuildLookupCaches();
    }

    /// <summary>
    /// Applies a pending deferred load after the scene has been recreated from a clean state.
    /// This avoids restoring save data on top of a live runtime scene.
    /// </summary>
    private void Start()
    {
        if (!HasPendingLoadRequest)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(PendingLoadFileName) || string.IsNullOrWhiteSpace(PendingLoadRootKey))
        {
            HasPendingLoadRequest = false;
            PendingLoadFileName = string.Empty;
            PendingLoadRootKey = string.Empty;
            return;
        }

        if (!ES3.KeyExists(PendingLoadRootKey, PendingLoadFileName))
        {
            HasPendingLoadRequest = false;
            PendingLoadFileName = string.Empty;
            PendingLoadRootKey = string.Empty;
            return;
        }

        SaveData Data = ES3.Load<SaveData>(PendingLoadRootKey, filePath: PendingLoadFileName);

        HasPendingLoadRequest = false;
        PendingLoadFileName = string.Empty;
        PendingLoadRootKey = string.Empty;

        if (Data == null)
        {
            Log("Deferred load failed because loaded save data was null.");
            return;
        }

        ApplySaveData(Data);
        Physics.SyncTransforms();

        Log("Deferred load applied after clean scene reload.");
    }

    /// <summary>
    /// Debug keyboard entry points.
    /// </summary>
    private void Update()
    {
        //@TODO:QUITAR ESTAS TECLAS PARA QUE NO SALTEN ERROR
        if (Input.GetKeyDown(KeyCode.F5))
        {
            SaveGame();
        }

        if (Input.GetKeyDown(KeyCode.F4))
        {
            LoadGame();
        }
    }

    /// <summary>
    /// Saves the current gameplay state into the fixed debug slot.
    /// </summary>
    [ContextMenu("Save Debug Game")]
    public void SaveGame()
    {
        NormalizeTransientCarryStatesForSaveLoad();
        Physics.SyncTransforms();
        SaveData Data = BuildSaveData();
        ES3.Save(SaveRootKey, Data, SaveFileName);
        Log("Saved debug slot to file: " + SaveFileName);
    }

    /// <summary>
    /// Requests a clean scene reload and applies the saved snapshot only after the new scene boots.
    /// This avoids restoring save data on top of a dirty live runtime world.
    /// </summary>
    [ContextMenu("Load Debug Game")]
    public void LoadGame()
    {
        if (!ES3.KeyExists(SaveRootKey, SaveFileName))
        {
            Log("No debug save found in file: " + SaveFileName);
            return;
        }

        HasPendingLoadRequest = true;
        PendingLoadFileName = SaveFileName;
        PendingLoadRootKey = SaveRootKey;

        Scene ActiveScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(ActiveScene.name);
    }

    /// <summary>
    /// Builds the root save payload from the current gameplay state.
    /// </summary>
    private SaveData BuildSaveData()
    {
        SaveData Data = new SaveData();

        if (PlayerController != null)
        {
            Data.SetPlayer(new PlayerSaveData(
                PlayerController.transform.position,
                PlayerController.IsCrouching));
        }

        if (CurrencyWallet != null)
        {
            Data.SetWallet(new WalletSaveData(
                CurrencyWallet.GetBalance(CurrencyWallet.CurrencyType.Gold),
                CurrencyWallet.GetBalance(CurrencyWallet.CurrencyType.Research)));
        }

        if (HotbarController != null)
        {
            Data.SetHotbar(HotbarSaveData.FromRuntime(HotbarController));
        }

        if (ElevatorPhysicalMotor != null)
        {
            Data.SetElevator(new ElevatorSaveData(
                ElevatorPhysicalMotor.GetCurrentDistance(),
                ElevatorPhysicalMotor.transform.rotation,
                VerticalSnapLever != null ? VerticalSnapLever.CurrentSnapIndex : 0,
                RotationSnapLever != null ? RotationSnapLever.CurrentSnapIndex : 0));
        }

        if (UpgradeManager != null)
        {
            Data.SetUpgradeEntries(UpgradeManager.CreateSaveEntries());
        }

        Data.SetSceneWorldItems(CaptureSceneWorldItems());
        Data.SetRuntimeWorldItems(CaptureRuntimeWorldItems());
        Data.SetMoneyPickups(CaptureMoneyPickups());
        Data.SetOrePickups(CaptureOrePickups());
        Data.SetOreSpawnPoints(CaptureOreSpawnPoints());

        return Data;
    }

    /// <summary>
    /// Applies a previously loaded gameplay save.
    /// </summary>
    private void ApplySaveData(SaveData Data)
    {
        if (Data == null)
        {
            return;
        }

        if (CurrencyWallet != null && Data.GetWallet() != null)
        {
            CurrencyWallet.SetBalance(CurrencyWallet.CurrencyType.Gold, Data.GetWallet().GetGold());
            CurrencyWallet.SetBalance(CurrencyWallet.CurrencyType.Research, Data.GetWallet().GetResearch());
        }

        if (UpgradeManager != null)
        {
            UpgradeManager.ApplySaveEntries(Data.GetUpgradeEntries());
        }

        if (ElevatorPhysicalMotor != null && Data.GetElevator() != null)
        {
            ElevatorPhysicalMotor.ApplySavedPose(
                Data.GetElevator().GetCurrentDistance(),
                Data.GetElevator().GetRotation());
        }

        if (VerticalSnapLever != null && Data.GetElevator() != null)
        {
            VerticalSnapLever.SetSnapIndexWithoutNotify(Data.GetElevator().GetVerticalLeverIndex());
        }

        if (RotationSnapLever != null && Data.GetElevator() != null)
        {
            RotationSnapLever.SetSnapIndexWithoutNotify(Data.GetElevator().GetRotationLeverIndex());
        }

        RestoreOreSpawnPoints(Data.GetOreSpawnPoints());
        RestoreSceneWorldItems(Data.GetSceneWorldItems());

        if (PlayerController != null && Data.GetPlayer() != null)
        {
            PlayerController.ApplySavedState(
                Data.GetPlayer().GetPosition(),
                Data.GetPlayer().GetIsCrouching());
        }

        if (HotbarController != null && Data.GetHotbar() != null)
        {
            HotbarController.ApplySaveState(
                Data.GetHotbar().ToRuntime(ItemDefinitionsById),
                Data.GetHotbar().GetSelectedIndex());
        }

        RestoreRuntimeWorldItems(Data.GetRuntimeWorldItems());
        RestoreMoneyPickups(Data.GetMoneyPickups());
        RestoreOrePickups(Data.GetOrePickups());
    }

    /// <summary>
    /// Rebuilds definition and scene object lookup caches.
    /// </summary>
    private void RebuildLookupCaches()
    {
        ItemDefinitionsById.Clear();
        OreDefinitionsById.Clear();
        SceneWorldItemsById.Clear();
        OreSpawnPointsById.Clear();

        for (int Index = 0; Index < ItemDefinitions.Count; Index++)
        {
            ItemDefinition Definition = ItemDefinitions[Index];

            if (Definition == null || string.IsNullOrWhiteSpace(Definition.GetItemId()))
            {
                continue;
            }

            ItemDefinitionsById[Definition.GetItemId()] = Definition;
        }

        for (int Index = 0; Index < OreDefinitions.Count; Index++)
        {
            OreDefinition Definition = OreDefinitions[Index];

            if (Definition == null || string.IsNullOrWhiteSpace(Definition.GetOreId()))
            {
                continue;
            }

            OreDefinitionsById[Definition.GetOreId()] = Definition;
        }

        ScenePlacedWorldItemPersistence[] SceneWorldItems = FindObjectsByType<ScenePlacedWorldItemPersistence>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int Index = 0; Index < SceneWorldItems.Length; Index++)
        {
            ScenePlacedWorldItemPersistence SceneItem = SceneWorldItems[Index];

            if (SceneItem == null)
            {
                continue;
            }

            SceneSaveId SaveId = SceneItem.GetComponent<SceneSaveId>();

            if (SaveId == null || string.IsNullOrWhiteSpace(SaveId.GetId()))
            {
                continue;
            }

            SceneWorldItemsById[SaveId.GetId()] = SceneItem;
        }

        OreSpawnPoint[] SpawnPoints = FindObjectsByType<OreSpawnPoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int Index = 0; Index < SpawnPoints.Length; Index++)
        {
            OreSpawnPoint SpawnPoint = SpawnPoints[Index];

            if (SpawnPoint == null)
            {
                continue;
            }

            SceneSaveId SaveId = SpawnPoint.GetComponent<SceneSaveId>();

            if (SaveId == null || string.IsNullOrWhiteSpace(SaveId.GetId()))
            {
                continue;
            }

            OreSpawnPointsById[SaveId.GetId()] = SpawnPoint;
        }
    }

    /// <summary>
    /// Forces transient interaction-driven carry states to end before save or load.
    /// Save data should only capture stable world state, never live hold or magnet runtime state.
    /// </summary>
    private void NormalizeTransientCarryStatesForSaveLoad()
    {
        PlayerInteractionController InteractionController = FindFirstObjectByType<PlayerInteractionController>();

        if (InteractionController != null)
        {
            InteractionController.ForceReleaseHeldCarryableForSave();
        }

        MagnetItemBehaviour[] MagnetBehaviours = FindObjectsByType<MagnetItemBehaviour>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int Index = 0; Index < MagnetBehaviours.Length; Index++)
        {
            if (MagnetBehaviours[Index] == null)
            {
                continue;
            }

            MagnetBehaviours[Index].ForceStopMagnetForSave();
        }
    }

    /// <summary>
    /// Captures every scene-placed world item state.
    /// </summary>
    private List<SceneWorldItemState> CaptureSceneWorldItems()
    {
        List<SceneWorldItemState> Result = new List<SceneWorldItemState>();

        foreach (KeyValuePair<string, ScenePlacedWorldItemPersistence> Pair in SceneWorldItemsById)
        {
            ScenePlacedWorldItemPersistence SceneItem = Pair.Value;

            if (SceneItem == null)
            {
                continue;
            }

            bool IsPresent = SceneItem.GetIsPresent();
            WorldItem WorldItem = SceneItem.GetWorldItem();

            ItemInstanceData ItemData = null;
            Vector3 Position = SceneItem.transform.position;
            Quaternion Rotation = SceneItem.transform.rotation;

            if (IsPresent && WorldItem != null)
            {
                ItemData = ItemInstanceData.FromRuntime(WorldItem.CreateItemInstance());
                Position = WorldItem.GetWorldPosition();
                Rotation = WorldItem.GetWorldRotation();
            }

            Result.Add(new SceneWorldItemState(
                Pair.Key,
                IsPresent,
                ItemData,
                Position,
                Rotation));
        }

        return Result;
    }

    /// <summary>
    /// Captures every runtime world item not owned by a scene persistence wrapper.
    /// </summary>
    private List<RuntimeWorldItemState> CaptureRuntimeWorldItems()
    {
        List<RuntimeWorldItemState> Result = new List<RuntimeWorldItemState>();

        WorldItem[] WorldItems = FindObjectsByType<WorldItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int Index = 0; Index < WorldItems.Length; Index++)
        {
            WorldItem WorldItem = WorldItems[Index];

            if (WorldItem == null)
            {
                continue;
            }

            if (WorldItem.GetComponentInParent<ScenePlacedWorldItemPersistence>() != null)
            {
                continue;
            }

            ItemInstanceData ItemData = ItemInstanceData.FromRuntime(WorldItem.CreateItemInstance());

            if (ItemData == null)
            {
                continue;
            }

            Result.Add(new RuntimeWorldItemState(
                ItemData,
                WorldItem.GetWorldPosition(),
                WorldItem.GetWorldRotation()));
        }

        return Result;
    }

    /// <summary>
    /// Captures every active money pickup in the world.
    /// </summary>
    private List<MoneyPickupState> CaptureMoneyPickups()
    {
        List<MoneyPickupState> Result = new List<MoneyPickupState>();

        MoneyPickup[] Pickups = FindObjectsByType<MoneyPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int Index = 0; Index < Pickups.Length; Index++)
        {
            MoneyPickup Pickup = Pickups[Index];

            if (Pickup == null || !Pickup.gameObject.activeInHierarchy)
            {
                continue;
            }

            string MoneyId = Pickup.GetSaveMoneyId();

            if (string.IsNullOrWhiteSpace(MoneyId))
            {
                continue;
            }

            Result.Add(new MoneyPickupState(
                MoneyId,
                Pickup.GetCurrencyType(),
                Pickup.GetAmount(),
                Pickup.GetRuntimeRoot().position,
                Pickup.GetRuntimeRoot().rotation));
        }

        return Result;
    }

    /// <summary>
    /// Captures every active ore pickup in the world.
    /// </summary>
    private List<OrePickupState> CaptureOrePickups()
    {
        List<OrePickupState> Result = new List<OrePickupState>();

        OrePickup[] Pickups = FindObjectsByType<OrePickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int Index = 0; Index < Pickups.Length; Index++)
        {
            OrePickup Pickup = Pickups[Index];

            if (Pickup == null || !Pickup.gameObject.activeInHierarchy)
            {
                continue;
            }

            string SourcePrefabName = Pickup.GetSourcePrefabName();
            OreItemDataSaveData OreData = OreItemDataSaveData.FromRuntime(Pickup.GetOreItemData());

            if (string.IsNullOrWhiteSpace(SourcePrefabName) || OreData == null)
            {
                continue;
            }

            Result.Add(new OrePickupState(
                SourcePrefabName,
                OreData,
                Pickup.GetRuntimeRoot().position,
                Pickup.GetRuntimeRoot().rotation));
        }

        return Result;
    }

    /// <summary>
    /// Captures every ore spawn point state in the scene.
    /// </summary>
    private List<OreSpawnPointState> CaptureOreSpawnPoints()
    {
        List<OreSpawnPointState> Result = new List<OreSpawnPointState>();

        foreach (KeyValuePair<string, OreSpawnPoint> Pair in OreSpawnPointsById)
        {
            OreSpawnPoint SpawnPoint = Pair.Value;

            if (SpawnPoint == null)
            {
                continue;
            }

            OreVein CurrentVein = SpawnPoint.GetCurrentVein();

            if (!SpawnPoint.GetIsActive() || CurrentVein == null || CurrentVein.GetOreDefinition() == null)
            {
                Result.Add(new OreSpawnPointState(
                    Pair.Key,
                    false,
                    string.Empty,
                    false,
                    0,
                    0f));
                continue;
            }

            Result.Add(new OreSpawnPointState(
                Pair.Key,
                true,
                CurrentVein.GetOreDefinition().GetOreId(),
                CurrentVein.GetIsGrowing(),
                CurrentVein.GetCurrentHitsRemaining(),
                CurrentVein.GetCurrentRespawnTimer()));
        }

        return Result;
    }

    /// <summary>
    /// Restores every scene-placed world item from save.
    /// </summary>
    private void RestoreSceneWorldItems(List<SceneWorldItemState> States)
    {
        if (States == null)
        {
            return;
        }

        for (int Index = 0; Index < States.Count; Index++)
        {
            SceneWorldItemState State = States[Index];

            if (State == null || string.IsNullOrWhiteSpace(State.GetSceneId()))
            {
                continue;
            }

            if (!SceneWorldItemsById.TryGetValue(State.GetSceneId(), out ScenePlacedWorldItemPersistence SceneItem) || SceneItem == null)
            {
                continue;
            }

            if (!State.GetIsPresent())
            {
                SceneItem.SetPresent(false);
                continue;
            }

            ItemInstance RuntimeItem = State.GetItemData() != null
                ? State.GetItemData().ToRuntime(ItemDefinitionsById)
                : null;

            if (RuntimeItem == null)
            {
                SceneItem.SetPresent(false);
                continue;
            }

            SceneItem.ApplySavedState(RuntimeItem, State.GetPosition(), State.GetRotation());
        }
    }

    /// <summary>
    /// Restores runtime world items by respawning them from saved item data.
    /// </summary>
    private void RestoreRuntimeWorldItems(List<RuntimeWorldItemState> States)
    {
        if (States == null || HotbarController == null)
        {
            return;
        }

        for (int Index = 0; Index < States.Count; Index++)
        {
            RuntimeWorldItemState State = States[Index];

            if (State == null || State.GetItemData() == null)
            {
                continue;
            }

            ItemInstance RuntimeItem = State.GetItemData().ToRuntime(ItemDefinitionsById);

            if (RuntimeItem == null)
            {
                continue;
            }

            HotbarController.SpawnWorldItem(
                RuntimeItem,
                State.GetPosition(),
                State.GetRotation(),
                Vector3.zero,
                Vector3.zero,
                false);
        }
    }

    /// <summary>
    /// Restores money pickups in a stable non-moving state.
    /// </summary>
    private void RestoreMoneyPickups(List<MoneyPickupState> States)
    {
        if (States == null)
        {
            return;
        }

        for (int Index = 0; Index < States.Count; Index++)
        {
            MoneyPickupState State = States[Index];

            if (State == null || string.IsNullOrWhiteSpace(State.GetMoneyId()))
            {
                continue;
            }

            GameObject Prefab = OreSellTrigger != null
                ? OreSellTrigger.GetMoneyPrefabByDenominationId(State.GetMoneyId())
                : null;

            if (Prefab == null)
            {
                continue;
            }

            MoneyPickup Pickup = null;

            if (MoneyPickupPool != null)
            {
                Pickup = MoneyPickupPool.GetPickup(Prefab, State.GetPosition(), State.GetRotation());
            }

            if (Pickup == null)
            {
                GameObject Instance = Instantiate(Prefab, State.GetPosition(), State.GetRotation());
                Pickup = Instance.GetComponent<MoneyPickup>();

                if (Pickup == null)
                {
                    Pickup = Instance.GetComponentInChildren<MoneyPickup>(true);
                }

                if (Pickup != null)
                {
                    Pickup.BindPool(null, Prefab);
                }
            }

            if (Pickup == null)
            {
                continue;
            }

            Pickup.Initialize(State.GetAmount(), State.GetCurrencyType());
            Pickup.SetSaveMoneyId(State.GetMoneyId());

            Rigidbody RigidbodyComponent = Pickup.GetCachedRigidbody();
            if (RigidbodyComponent != null)
            {
                if (!RigidbodyComponent.isKinematic)
                {
                    RigidbodyComponent.linearVelocity = Vector3.zero;
                    RigidbodyComponent.angularVelocity = Vector3.zero;
                }

                RigidbodyComponent.Sleep();
            }
        }
    }

    /// <summary>
    /// Restores ore pickups in a stable non-moving state.
    /// </summary>
    private void RestoreOrePickups(List<OrePickupState> States)
    {
        if (States == null)
        {
            return;
        }

        for (int Index = 0; Index < States.Count; Index++)
        {
            OrePickupState State = States[Index];

            if (State == null || State.GetOreData() == null)
            {
                continue;
            }

            GameObject Prefab = ResolveOrePickupPrefabByName(State.GetSourcePrefabName());

            if (Prefab == null)
            {
                continue;
            }

            OreItemData RuntimeOreData = State.GetOreData().ToRuntime(OreDefinitionsById);

            if (RuntimeOreData == null)
            {
                continue;
            }

            OrePickup Pickup = null;

            if (OrePickupPool != null)
            {
                Pickup = OrePickupPool.GetPickup(Prefab, State.GetPosition(), State.GetRotation());
            }

            if (Pickup == null)
            {
                GameObject Instance = Instantiate(Prefab, State.GetPosition(), State.GetRotation());
                Pickup = Instance.GetComponent<OrePickup>();

                if (Pickup == null)
                {
                    Pickup = Instance.GetComponentInChildren<OrePickup>(true);
                }

                if (Pickup != null)
                {
                    Pickup.BindPool(null, Prefab);
                }
            }

            if (Pickup == null)
            {
                continue;
            }

            Pickup.Initialize(RuntimeOreData);

            Rigidbody RigidbodyComponent = Pickup.GetComponent<Rigidbody>();
            if (RigidbodyComponent == null)
            {
                RigidbodyComponent = Pickup.GetComponentInChildren<Rigidbody>(true);
            }

            if (RigidbodyComponent != null)
            {
                if (!RigidbodyComponent.isKinematic)
                {
                    RigidbodyComponent.linearVelocity = Vector3.zero;
                    RigidbodyComponent.angularVelocity = Vector3.zero;
                }

                RigidbodyComponent.Sleep();
            }
        }
    }

    /// <summary>
    /// Restores every ore spawn point in the scene from saved state.
    /// </summary>
    private void RestoreOreSpawnPoints(List<OreSpawnPointState> States)
    {
        foreach (KeyValuePair<string, OreSpawnPoint> Pair in OreSpawnPointsById)
        {
            if (Pair.Value != null)
            {
                Pair.Value.ClearPoint();
            }
        }

        if (States == null || OreRuntimeService == null)
        {
            return;
        }

        for (int Index = 0; Index < States.Count; Index++)
        {
            OreSpawnPointState State = States[Index];

            if (State == null || string.IsNullOrWhiteSpace(State.GetSceneId()))
            {
                continue;
            }

            if (!OreSpawnPointsById.TryGetValue(State.GetSceneId(), out OreSpawnPoint SpawnPoint) || SpawnPoint == null)
            {
                continue;
            }

            if (!State.GetIsActive() || string.IsNullOrWhiteSpace(State.GetOreId()))
            {
                SpawnPoint.ClearPoint();
                continue;
            }

            if (!OreDefinitionsById.TryGetValue(State.GetOreId(), out OreDefinition Definition) || Definition == null)
            {
                SpawnPoint.ClearPoint();
                continue;
            }

            bool WasSpawned = SpawnPoint.SpawnVein(Definition, OreRuntimeService);

            if (!WasSpawned)
            {
                continue;
            }

            OreVein CurrentVein = SpawnPoint.GetCurrentVein();

            if (CurrentVein != null)
            {
                CurrentVein.ApplySavedRuntimeState(
                    State.GetIsGrowing(),
                    State.GetHitsRemaining(),
                    State.GetRespawnTimerRemaining());
            }
        }
    }

    /// <summary>
    /// Removes every active runtime world item before loading.
    /// Scene-placed items are preserved because they are restored separately.
    /// </summary>
    private void ClearRuntimeWorldItems()
    {
        WorldItem[] WorldItems = FindObjectsByType<WorldItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int Index = 0; Index < WorldItems.Length; Index++)
        {
            WorldItem WorldItem = WorldItems[Index];

            if (WorldItem == null)
            {
                continue;
            }

            if (WorldItem.GetComponentInParent<ScenePlacedWorldItemPersistence>() != null)
            {
                continue;
            }

            Destroy(WorldItem.gameObject);
        }
    }

    /// <summary>
    /// Returns every active runtime money pickup to the pool or destroys it.
    /// </summary>
    private void ClearRuntimeMoneyPickups()
    {
        MoneyPickup[] Pickups = FindObjectsByType<MoneyPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int Index = 0; Index < Pickups.Length; Index++)
        {
            MoneyPickup Pickup = Pickups[Index];

            if (Pickup == null)
            {
                continue;
            }

            if (!Pickup.ReturnToPool())
            {
                Destroy(Pickup.GetRuntimeRoot().gameObject);
            }
        }
    }

    /// <summary>
    /// Returns every active runtime ore pickup to the pool or destroys it.
    /// </summary>
    private void ClearRuntimeOrePickups()
    {
        OrePickup[] Pickups = FindObjectsByType<OrePickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int Index = 0; Index < Pickups.Length; Index++)
        {
            OrePickup Pickup = Pickups[Index];

            if (Pickup == null)
            {
                continue;
            }

            if (!Pickup.ReturnToPool())
            {
                Destroy(Pickup.GetRuntimeRoot().gameObject);
            }
        }
    }

    /// <summary>
    /// Resolves an ore pickup visual prefab from its runtime source prefab name.
    /// </summary>
    private GameObject ResolveOrePickupPrefabByName(string PrefabName)
    {
        if (string.IsNullOrWhiteSpace(PrefabName))
        {
            return null;
        }

        foreach (KeyValuePair<string, OreDefinition> Pair in OreDefinitionsById)
        {
            OreDefinition Definition = Pair.Value;

            if (Definition == null)
            {
                continue;
            }

            GameObject LegacyPrefab = Definition.GetDroppedOrePrefab();

            if (LegacyPrefab != null && LegacyPrefab.name == PrefabName)
            {
                return LegacyPrefab;
            }

            IReadOnlyList<GameObject> VisualPrefabs = Definition.GetDroppedOreVisualPrefabs();

            for (int Index = 0; Index < VisualPrefabs.Count; Index++)
            {
                GameObject VisualPrefab = VisualPrefabs[Index];

                if (VisualPrefab != null && VisualPrefab.name == PrefabName)
                {
                    return VisualPrefab;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Logs save system messages if debug logging is enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[GameSaveDebugController] " + Message, this);
    }
}
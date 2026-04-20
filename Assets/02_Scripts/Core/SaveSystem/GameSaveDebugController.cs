using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Debug-only save/load entry point for the current gameplay scene.
/// Press F5 to save and F4 to load the fixed debug slot.
/// This implementation uses Easy Save 3 as the persistence backend while keeping
/// gameplay reconstruction explicit and stable.
/// </summary>
public sealed class GameSaveDebugController : MonoBehaviour
{
    private const string SaveFileName = "save_debug.es3";
    private const string SaveRootKey = "debug_save_root";

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

    [Header("Debug")]
    [Tooltip("Logs save and load operations.")]
    [SerializeField] private bool DebugLogs = true;

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
        [SerializeField] private List<ItemInstance> Slots = new();
        [SerializeField] private int SelectedIndex;

        public HotbarSaveData(List<ItemInstance> SlotsValue, int SelectedIndexValue)
        {
            Slots = SlotsValue ?? new List<ItemInstance>();
            SelectedIndex = SelectedIndexValue;
        }

        public List<ItemInstance> GetSlots() => Slots;
        public int GetSelectedIndex() => SelectedIndex;
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
        [SerializeField] private ScenePlacedWorldItemPersistence SceneItem;
        [SerializeField] private bool IsPresent;
        [SerializeField] private ItemInstance ItemInstance;
        [SerializeField] private Vector3 Position;
        [SerializeField] private Quaternion Rotation;

        public SceneWorldItemState(ScenePlacedWorldItemPersistence SceneItemValue, bool IsPresentValue, ItemInstance ItemInstanceValue, Vector3 PositionValue, Quaternion RotationValue)
        {
            SceneItem = SceneItemValue;
            IsPresent = IsPresentValue;
            ItemInstance = ItemInstanceValue;
            Position = PositionValue;
            Rotation = RotationValue;
        }

        public ScenePlacedWorldItemPersistence GetSceneItem() => SceneItem;
        public bool GetIsPresent() => IsPresent;
        public ItemInstance GetItemInstance() => ItemInstance;
        public Vector3 GetPosition() => Position;
        public Quaternion GetRotation() => Rotation;
    }

    [Serializable]
    private sealed class RuntimeWorldItemState
    {
        [SerializeField] private ItemInstance ItemInstance;
        [SerializeField] private Vector3 Position;
        [SerializeField] private Quaternion Rotation;

        public RuntimeWorldItemState(ItemInstance ItemInstanceValue, Vector3 PositionValue, Quaternion RotationValue)
        {
            ItemInstance = ItemInstanceValue;
            Position = PositionValue;
            Rotation = RotationValue;
        }

        public ItemInstance GetItemInstance() => ItemInstance;
        public Vector3 GetPosition() => Position;
        public Quaternion GetRotation() => Rotation;
    }

    [Serializable]
    private sealed class MoneyPickupState
    {
        [SerializeField] private GameObject SourcePrefab;
        [SerializeField] private CurrencyWallet.CurrencyType CurrencyType;
        [SerializeField] private float Amount;
        [SerializeField] private Vector3 Position;
        [SerializeField] private Quaternion Rotation;

        public MoneyPickupState(GameObject SourcePrefabValue, CurrencyWallet.CurrencyType CurrencyTypeValue, float AmountValue, Vector3 PositionValue, Quaternion RotationValue)
        {
            SourcePrefab = SourcePrefabValue;
            CurrencyType = CurrencyTypeValue;
            Amount = AmountValue;
            Position = PositionValue;
            Rotation = RotationValue;
        }

        public GameObject GetSourcePrefab() => SourcePrefab;
        public CurrencyWallet.CurrencyType GetCurrencyType() => CurrencyType;
        public float GetAmount() => Amount;
        public Vector3 GetPosition() => Position;
        public Quaternion GetRotation() => Rotation;
    }

    [Serializable]
    private sealed class OrePickupState
    {
        [SerializeField] private GameObject SourcePrefab;
        [SerializeField] private OreItemData OreItemData;
        [SerializeField] private Vector3 Position;
        [SerializeField] private Quaternion Rotation;

        public OrePickupState(GameObject SourcePrefabValue, OreItemData OreItemDataValue, Vector3 PositionValue, Quaternion RotationValue)
        {
            SourcePrefab = SourcePrefabValue;
            OreItemData = OreItemDataValue;
            Position = PositionValue;
            Rotation = RotationValue;
        }

        public GameObject GetSourcePrefab() => SourcePrefab;
        public OreItemData GetOreItemData() => OreItemData;
        public Vector3 GetPosition() => Position;
        public Quaternion GetRotation() => Rotation;
    }

    [Serializable]
    private sealed class OreSpawnPointState
    {
        [SerializeField] private OreSpawnPoint SpawnPoint;
        [SerializeField] private bool IsActive;
        [SerializeField] private OreDefinition OreDefinition;
        [SerializeField] private bool IsGrowing;
        [SerializeField] private int HitsRemaining;
        [SerializeField] private float RespawnTimerRemaining;

        public OreSpawnPointState(
            OreSpawnPoint SpawnPointValue,
            bool IsActiveValue,
            OreDefinition OreDefinitionValue,
            bool IsGrowingValue,
            int HitsRemainingValue,
            float RespawnTimerRemainingValue)
        {
            SpawnPoint = SpawnPointValue;
            IsActive = IsActiveValue;
            OreDefinition = OreDefinitionValue;
            IsGrowing = IsGrowingValue;
            HitsRemaining = HitsRemainingValue;
            RespawnTimerRemaining = RespawnTimerRemainingValue;
        }

        public OreSpawnPoint GetSpawnPoint() => SpawnPoint;
        public bool GetIsActive() => IsActive;
        public OreDefinition GetOreDefinition() => OreDefinition;
        public bool GetIsGrowing() => IsGrowing;
        public int GetHitsRemaining() => HitsRemaining;
        public float GetRespawnTimerRemaining() => RespawnTimerRemaining;
    }

    /// <summary>
    /// Resolves missing scene references.
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
    }

    /// <summary>
    /// Debug keyboard entry points.
    /// </summary>
    private void Update()
    {
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
        SaveData SaveData = BuildSaveData();
        ES3.Save(SaveRootKey, SaveData, SaveFileName);
        Log("Saved debug slot to file: " + SaveFileName);
    }

    /// <summary>
    /// Loads the fixed debug slot and restores the gameplay state.
    /// </summary>
    [ContextMenu("Load Debug Game")]
    public void LoadGame()
    {
        if (!ES3.KeyExists(SaveRootKey, SaveFileName))
        {
            Log("No debug save found in file: " + SaveFileName);
            return;
        }

        SaveData SaveData = ES3.Load<SaveData>(SaveRootKey, filePath: SaveFileName);

        if (SaveData == null)
        {
            Log("Loaded save data was null.");
            return;
        }

        ClearRuntimeWorldItems();
        ClearRuntimeMoneyPickups();
        ClearRuntimeOrePickups();

        ApplySaveData(SaveData);
        Physics.SyncTransforms();

        Log("Loaded debug slot from file: " + SaveFileName);
    }

    /// <summary>
    /// Builds the root save payload from the current gameplay state.
    /// </summary>
    private SaveData BuildSaveData()
    {
        SaveData SaveData = new SaveData();

        if (PlayerController != null)
        {
            SaveData.SetPlayer(new PlayerSaveData(
                PlayerController.GetWorldPosition(),
                PlayerController.IsCrouching));
        }

        if (CurrencyWallet != null)
        {
            SaveData.SetWallet(new WalletSaveData(
                CurrencyWallet.GetBalance(CurrencyWallet.CurrencyType.Gold),
                CurrencyWallet.GetBalance(CurrencyWallet.CurrencyType.Research)));
        }

        if (HotbarController != null)
        {
            SaveData.SetHotbar(new HotbarSaveData(
                HotbarController.CreateSlotSaveSnapshot(),
                HotbarController.GetSelectedIndex()));
        }

        if (ElevatorPhysicalMotor != null)
        {
            SaveData.SetElevator(new ElevatorSaveData(
                ElevatorPhysicalMotor.GetCurrentDistance(),
                ElevatorPhysicalMotor.transform.rotation,
                VerticalSnapLever != null ? VerticalSnapLever.CurrentSnapIndex : 0,
                RotationSnapLever != null ? RotationSnapLever.CurrentSnapIndex : 0));
        }

        if (UpgradeManager != null)
        {
            SaveData.SetUpgradeEntries(UpgradeManager.CreateSaveEntries());
        }

        SaveData.SetSceneWorldItems(CaptureSceneWorldItems());
        SaveData.SetRuntimeWorldItems(CaptureRuntimeWorldItems());
        SaveData.SetMoneyPickups(CaptureMoneyPickups());
        SaveData.SetOrePickups(CaptureOrePickups());
        SaveData.SetOreSpawnPoints(CaptureOreSpawnPointStates());

        return SaveData;
    }

    /// <summary>
    /// Applies a previously loaded gameplay save.
    /// </summary>
    private void ApplySaveData(SaveData SaveData)
    {
        if (SaveData == null)
        {
            return;
        }

        if (CurrencyWallet != null && SaveData.GetWallet() != null)
        {
            CurrencyWallet.SetBalance(CurrencyWallet.CurrencyType.Gold, SaveData.GetWallet().GetGold());
            CurrencyWallet.SetBalance(CurrencyWallet.CurrencyType.Research, SaveData.GetWallet().GetResearch());
        }

        if (UpgradeManager != null)
        {
            UpgradeManager.ApplySaveEntries(SaveData.GetUpgradeEntries());
        }

        if (ElevatorPhysicalMotor != null && SaveData.GetElevator() != null)
        {
            ElevatorPhysicalMotor.ApplySavedPose(
                SaveData.GetElevator().GetCurrentDistance(),
                SaveData.GetElevator().GetRotation());
        }

        if (VerticalSnapLever != null && SaveData.GetElevator() != null)
        {
            VerticalSnapLever.SetSnapIndexWithoutNotify(SaveData.GetElevator().GetVerticalLeverIndex());
        }

        if (RotationSnapLever != null && SaveData.GetElevator() != null)
        {
            RotationSnapLever.SetSnapIndexWithoutNotify(SaveData.GetElevator().GetRotationLeverIndex());
        }

        RestoreOreSpawnPoints(SaveData.GetOreSpawnPoints());
        RestoreSceneWorldItems(SaveData.GetSceneWorldItems());

        if (PlayerController != null && SaveData.GetPlayer() != null)
        {
            PlayerController.ApplySavedState(
                SaveData.GetPlayer().GetPosition(),
                SaveData.GetPlayer().GetIsCrouching());
        }

        if (HotbarController != null && SaveData.GetHotbar() != null)
        {
            HotbarController.ApplySaveState(
                SaveData.GetHotbar().GetSlots(),
                SaveData.GetHotbar().GetSelectedIndex());
        }

        RestoreRuntimeWorldItems(SaveData.GetRuntimeWorldItems());
        RestoreMoneyPickups(SaveData.GetMoneyPickups());
        RestoreOrePickups(SaveData.GetOrePickups());
    }

    /// <summary>
    /// Captures every scene-placed world item persistence wrapper.
    /// </summary>
    private List<SceneWorldItemState> CaptureSceneWorldItems()
    {
        List<SceneWorldItemState> Result = new List<SceneWorldItemState>();

        ScenePlacedWorldItemPersistence[] SceneItems = FindObjectsByType<ScenePlacedWorldItemPersistence>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int Index = 0; Index < SceneItems.Length; Index++)
        {
            ScenePlacedWorldItemPersistence SceneItem = SceneItems[Index];

            if (SceneItem == null)
            {
                continue;
            }

            bool IsPresent = SceneItem.GetIsPresent();
            WorldItem WorldItem = SceneItem.GetWorldItem();

            ItemInstance ItemInstance = null;
            Vector3 Position = SceneItem.transform.position;
            Quaternion Rotation = SceneItem.transform.rotation;

            if (IsPresent && WorldItem != null)
            {
                ItemInstance = WorldItem.CreateItemInstance();
                Position = WorldItem.GetWorldPosition();
                Rotation = WorldItem.GetWorldRotation();
            }

            Result.Add(new SceneWorldItemState(SceneItem, IsPresent, ItemInstance, Position, Rotation));
        }

        return Result;
    }

    /// <summary>
    /// Captures every active runtime world item not placed directly in the scene.
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

            ItemInstance ItemInstance = WorldItem.CreateItemInstance();

            if (ItemInstance == null)
            {
                continue;
            }

            Result.Add(new RuntimeWorldItemState(
                ItemInstance,
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

        MoneyPickup[] MoneyPickups = FindObjectsByType<MoneyPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int Index = 0; Index < MoneyPickups.Length; Index++)
        {
            MoneyPickup MoneyPickup = MoneyPickups[Index];

            if (MoneyPickup == null || !MoneyPickup.gameObject.activeInHierarchy)
            {
                continue;
            }

            GameObject SourcePrefab = MoneyPickup.GetSourcePrefab();

            if (SourcePrefab == null)
            {
                continue;
            }

            Result.Add(new MoneyPickupState(
                SourcePrefab,
                MoneyPickup.GetCurrencyType(),
                MoneyPickup.GetAmount(),
                MoneyPickup.GetRuntimeRoot().position,
                MoneyPickup.GetRuntimeRoot().rotation));
        }

        return Result;
    }

    /// <summary>
    /// Captures every active ore pickup in the world.
    /// </summary>
    private List<OrePickupState> CaptureOrePickups()
    {
        List<OrePickupState> Result = new List<OrePickupState>();

        OrePickup[] OrePickups = FindObjectsByType<OrePickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int Index = 0; Index < OrePickups.Length; Index++)
        {
            OrePickup OrePickup = OrePickups[Index];

            if (OrePickup == null || !OrePickup.gameObject.activeInHierarchy)
            {
                continue;
            }

            GameObject SourcePrefab = OrePickup.GetSourcePrefab();
            OreItemData OreItemData = OrePickup.GetOreItemData();

            if (SourcePrefab == null || OreItemData == null)
            {
                continue;
            }

            Result.Add(new OrePickupState(
                SourcePrefab,
                OreItemData,
                OrePickup.GetRuntimeRoot().position,
                OrePickup.GetRuntimeRoot().rotation));
        }

        return Result;
    }

    /// <summary>
    /// Captures every ore spawn point and its current runtime vein state.
    /// </summary>
    private List<OreSpawnPointState> CaptureOreSpawnPointStates()
    {
        List<OreSpawnPointState> Result = new List<OreSpawnPointState>();

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

            OreVein CurrentVein = SpawnPoint.GetCurrentVein();

            if (!SpawnPoint.GetIsActive() || CurrentVein == null)
            {
                Result.Add(new OreSpawnPointState(
                    SpawnPoint,
                    false,
                    null,
                    false,
                    0,
                    0f));

                continue;
            }

            Result.Add(new OreSpawnPointState(
                SpawnPoint,
                true,
                CurrentVein.GetOreDefinition(),
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

            if (State == null || State.GetSceneItem() == null)
            {
                continue;
            }

            if (!State.GetIsPresent())
            {
                State.GetSceneItem().SetPresent(false);
                continue;
            }

            State.GetSceneItem().ApplySavedState(
                State.GetItemInstance(),
                State.GetPosition(),
                State.GetRotation());
        }
    }

    /// <summary>
    /// Restores runtime world items by respawning them from their saved item instances.
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

            if (State == null || State.GetItemInstance() == null)
            {
                continue;
            }

            HotbarController.SpawnWorldItem(
                State.GetItemInstance(),
                State.GetPosition(),
                State.GetRotation(),
                Vector3.zero,
                Vector3.zero,
                false);
        }
    }

    /// <summary>
    /// Restores money pickups in a stable, non-moving state.
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

            if (State == null || State.GetSourcePrefab() == null)
            {
                continue;
            }

            MoneyPickup MoneyPickup = null;

            if (MoneyPickupPool != null)
            {
                MoneyPickup = MoneyPickupPool.GetPickup(
                    State.GetSourcePrefab(),
                    State.GetPosition(),
                    State.GetRotation());
            }

            if (MoneyPickup == null)
            {
                GameObject Instance = Instantiate(State.GetSourcePrefab(), State.GetPosition(), State.GetRotation());
                MoneyPickup = Instance.GetComponent<MoneyPickup>();

                if (MoneyPickup == null)
                {
                    MoneyPickup = Instance.GetComponentInChildren<MoneyPickup>(true);
                }

                if (MoneyPickup != null)
                {
                    MoneyPickup.BindPool(null, State.GetSourcePrefab());
                }
            }

            if (MoneyPickup == null)
            {
                continue;
            }

            MoneyPickup.Initialize(State.GetAmount(), State.GetCurrencyType());

            Rigidbody RigidbodyComponent = MoneyPickup.GetCachedRigidbody();
            if (RigidbodyComponent != null)
            {
                RigidbodyComponent.linearVelocity = Vector3.zero;
                RigidbodyComponent.angularVelocity = Vector3.zero;
                RigidbodyComponent.Sleep();
            }
        }
    }

    /// <summary>
    /// Restores ore pickups in a stable, non-moving state.
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

            if (State == null || State.GetSourcePrefab() == null || State.GetOreItemData() == null)
            {
                continue;
            }

            OrePickup OrePickup = null;

            if (OrePickupPool != null)
            {
                OrePickup = OrePickupPool.GetPickup(
                    State.GetSourcePrefab(),
                    State.GetPosition(),
                    State.GetRotation());
            }

            if (OrePickup == null)
            {
                GameObject Instance = Instantiate(State.GetSourcePrefab(), State.GetPosition(), State.GetRotation());
                OrePickup = Instance.GetComponent<OrePickup>();

                if (OrePickup == null)
                {
                    OrePickup = Instance.GetComponentInChildren<OrePickup>(true);
                }

                if (OrePickup != null)
                {
                    OrePickup.BindPool(null, State.GetSourcePrefab());
                }
            }

            if (OrePickup == null)
            {
                continue;
            }

            OrePickup.Initialize(State.GetOreItemData());

            Rigidbody RigidbodyComponent = OrePickup.GetComponent<Rigidbody>();
            if (RigidbodyComponent == null)
            {
                RigidbodyComponent = OrePickup.GetComponentInChildren<Rigidbody>(true);
            }

            if (RigidbodyComponent != null)
            {
                RigidbodyComponent.linearVelocity = Vector3.zero;
                RigidbodyComponent.angularVelocity = Vector3.zero;
                RigidbodyComponent.Sleep();
            }
        }
    }

    /// <summary>
    /// Restores the complete state of every ore spawn point in the scene.
    /// </summary>
    private void RestoreOreSpawnPoints(List<OreSpawnPointState> States)
    {
        OreSpawnPoint[] AllSpawnPoints = FindObjectsByType<OreSpawnPoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int Index = 0; Index < AllSpawnPoints.Length; Index++)
        {
            if (AllSpawnPoints[Index] != null)
            {
                AllSpawnPoints[Index].ClearPoint();
            }
        }

        if (States == null || OreRuntimeService == null)
        {
            return;
        }

        for (int Index = 0; Index < States.Count; Index++)
        {
            OreSpawnPointState State = States[Index];

            if (State == null || State.GetSpawnPoint() == null)
            {
                continue;
            }

            if (!State.GetIsActive() || State.GetOreDefinition() == null)
            {
                State.GetSpawnPoint().ClearPoint();
                continue;
            }

            bool WasSpawned = State.GetSpawnPoint().SpawnVein(State.GetOreDefinition(), OreRuntimeService);

            if (!WasSpawned)
            {
                continue;
            }

            OreVein CurrentVein = State.GetSpawnPoint().GetCurrentVein();

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

            WorldItem.gameObject.SetActive(false);
            Destroy(WorldItem.gameObject);
        }
    }

    /// <summary>
    /// Returns every active runtime money pickup to the pool or destroys it.
    /// </summary>
    private void ClearRuntimeMoneyPickups()
    {
        MoneyPickup[] MoneyPickups = FindObjectsByType<MoneyPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int Index = 0; Index < MoneyPickups.Length; Index++)
        {
            MoneyPickup MoneyPickup = MoneyPickups[Index];

            if (MoneyPickup == null)
            {
                continue;
            }

            if (!MoneyPickup.ReturnToPool())
            {
                MoneyPickup.GetRuntimeRoot().gameObject.SetActive(false);
                Destroy(MoneyPickup.GetRuntimeRoot().gameObject);
            }
        }
    }

    /// <summary>
    /// Returns every active runtime ore pickup to the pool or destroys it.
    /// </summary>
    private void ClearRuntimeOrePickups()
    {
        OrePickup[] OrePickups = FindObjectsByType<OrePickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int Index = 0; Index < OrePickups.Length; Index++)
        {
            OrePickup OrePickup = OrePickups[Index];

            if (OrePickup == null)
            {
                continue;
            }

            if (!OrePickup.ReturnToPool())
            {
                OrePickup.GetRuntimeRoot().gameObject.SetActive(false);
                Destroy(OrePickup.GetRuntimeRoot().gameObject);
            }
        }
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
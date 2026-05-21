using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Central runtime controller for pause menu navigation, external modal UI, cursor focus and world pausing.
/// This script is self-contained and does not require any additional menu panel components.
/// </summary>
[DisallowMultipleComponent]
public sealed class GameMenuFlowController : MonoBehaviour
{
    /// <summary>
    /// Serializable data for one pause menu panel controlled by this menu flow controller.
    /// </summary>
    [Serializable]
    private sealed class MenuPanel
    {
        [Tooltip("Readable identifier used only for logs and inspector clarity.")]
        [SerializeField] private string PanelId = "Panel";

        [Tooltip("Root object activated when this panel is shown and deactivated when this panel is hidden.")]
        [SerializeField] private GameObject PanelRoot;

        [Tooltip("Optional UI object selected when this panel is shown. Usually the first button in the panel.")]
        [SerializeField] private GameObject DefaultSelectedObject;

        [Tooltip("If true, the default selected object is selected whenever this panel is shown.")]
        [SerializeField] private bool SelectDefaultOnShow = true;

        /// <summary>
        /// Gets the panel identifier used by logs and debugging.
        /// </summary>
        /// <returns>Configured panel id, or a fallback id when empty.</returns>
        public string GetPanelId()
        {
            return string.IsNullOrWhiteSpace(PanelId) ? "UnnamedPanel" : PanelId;
        }

        /// <summary>
        /// Gets whether this panel has a valid root object.
        /// </summary>
        /// <returns>True if the panel can be shown or hidden.</returns>
        public bool GetIsAssigned()
        {
            return PanelRoot != null;
        }

        /// <summary>
        /// Gets the UI object that should be selected when this panel opens.
        /// </summary>
        /// <returns>Default selected object, or null when selection is disabled.</returns>
        public GameObject GetDefaultSelectedObject()
        {
            return SelectDefaultOnShow ? DefaultSelectedObject : null;
        }

        /// <summary>
        /// Shows this panel root.
        /// </summary>
        public void Show()
        {
            if (PanelRoot == null)
            {
                return;
            }

            PanelRoot.SetActive(true);
        }

        /// <summary>
        /// Hides this panel root.
        /// </summary>
        public void Hide()
        {
            if (PanelRoot == null)
            {
                return;
            }

            PanelRoot.SetActive(false);
        }
    }

    /// <summary>
    /// Serializable UI data for one save or load slot row.
    /// The slot can display saved metadata, expose a selection button and optionally show a selected indicator.
    /// </summary>
    [Serializable]
    private sealed class SaveSlotView
    {
        [Tooltip("Zero-based slot index used by the save controller. Slot 1 should use index 0.")]
        [SerializeField] private int SlotIndex;

        [Tooltip("Optional button used to select this save slot.")]
        [SerializeField] private Button SlotButton;

        [Tooltip("Optional text that displays the save timestamp or the empty-slot label.")]
        [SerializeField] private TMP_Text DateText;

        [Tooltip("Optional object activated only while this slot is selected. Use it for your red X or highlight.")]
        [SerializeField] private GameObject SelectedIndicatorRoot;

        /// <summary>
        /// Gets the zero-based slot index represented by this UI entry.
        /// </summary>
        public int GetSlotIndex()
        {
            return Mathf.Max(0, SlotIndex);
        }

        /// <summary>
        /// Gets the configured button for this slot.
        /// </summary>
        public Button GetSlotButton()
        {
            return SlotButton;
        }

        /// <summary>
        /// Updates this slot visual state.
        /// </summary>
        /// <param name="TimestampLabel">Timestamp or empty-state text displayed in the slot.</param>
        /// <param name="IsSelected">True when this slot is the current selected slot.</param>
        /// <param name="IsSelectable">True when the slot button can be clicked.</param>
        public void Refresh(string TimestampLabel, bool IsSelected, bool IsSelectable)
        {
            if (DateText != null)
            {
                DateText.text = TimestampLabel;
            }

            if (SelectedIndicatorRoot != null)
            {
                SelectedIndicatorRoot.SetActive(IsSelected);
            }

            if (SlotButton != null)
            {
                SlotButton.interactable = IsSelectable;
            }
        }
    }

    /// <summary>
    /// Serializable data for one external modal panel that is not part of the pause menu stack.
    /// Examples include upgrade UI, shop UI, research UI, storage UI or machine UI.
    /// </summary>
    [Serializable]
    private sealed class ExternalModalPanel
    {
        [Tooltip("Unique id used by scripts or UnityEvents to open this modal. Example: Upgrades, Shop, Research.")]
        [SerializeField] private string ModalId = "ExternalModal";

        [Tooltip("Optional root object activated when this external modal is shown.")]
        [SerializeField] private GameObject PanelRoot;

        [Tooltip("If true, Panel Root is activated on open and deactivated on close.")]
        [SerializeField] private bool ControlPanelRootActiveState = true;

        [Tooltip("Optional UI object selected when this external modal is shown. Usually the first button in the panel.")]
        [SerializeField] private GameObject DefaultSelectedObject;

        [Tooltip("If true, the default selected object is selected whenever this external modal is shown.")]
        [SerializeField] private bool SelectDefaultOnShow = true;

        [Tooltip("If true, opening this external modal also pauses Time.timeScale.")]
        [SerializeField] private bool PauseWorldWhileOpen = false;

        [Tooltip("If true, gameplay HUD objects configured on the menu controller are hidden while this external modal is open.")]
        [SerializeField] private bool HideGameplayHudWhileOpen = true;

        [Tooltip("Optional event invoked after the modal is opened. Use this to call custom UI setup methods such as UpgradePanelUI.ShowPanel.")]
        [SerializeField] private UnityEvent OnOpened = new UnityEvent();

        [Tooltip("Optional event invoked before the modal is closed. Use this to call custom UI shutdown methods such as UpgradePanelUI.HidePanel.")]
        [SerializeField] private UnityEvent OnClosed = new UnityEvent();

        /// <summary>
        /// Gets the unique modal identifier.
        /// </summary>
        /// <returns>Configured modal id.</returns>
        public string GetModalId()
        {
            return ModalId;
        }

        /// <summary>
        /// Gets whether this external modal has at least one usable open target.
        /// </summary>
        /// <returns>True when the modal can open a root object or invoke at least one persistent open event.</returns>
        public bool GetIsAssigned()
        {
            return PanelRoot != null || OnOpened.GetPersistentEventCount() > 0;
        }

        /// <summary>
        /// Gets whether this external modal should pause the world while open.
        /// </summary>
        /// <returns>True if this modal pauses Time.timeScale.</returns>
        public bool GetPauseWorldWhileOpen()
        {
            return PauseWorldWhileOpen;
        }

        /// <summary>
        /// Gets whether this external modal should hide configured gameplay HUD objects.
        /// </summary>
        /// <returns>True if gameplay HUD objects should be hidden while open.</returns>
        public bool GetHideGameplayHudWhileOpen()
        {
            return HideGameplayHudWhileOpen;
        }

        /// <summary>
        /// Gets the UI object that should be selected when this external modal opens.
        /// </summary>
        /// <returns>Default selected object, or null when selection is disabled.</returns>
        public GameObject GetDefaultSelectedObject()
        {
            return SelectDefaultOnShow ? DefaultSelectedObject : null;
        }

        /// <summary>
        /// Shows this external modal without changing player modal focus.
        /// </summary>
        public void ShowVisualOnly()
        {
            if (PanelRoot != null && ControlPanelRootActiveState)
            {
                PanelRoot.SetActive(true);
            }

            OnOpened?.Invoke();
        }

        /// <summary>
        /// Hides this external modal without changing player modal focus.
        /// </summary>
        public void HideVisualOnly()
        {
            OnClosed?.Invoke();

            if (PanelRoot != null && ControlPanelRootActiveState)
            {
                PanelRoot.SetActive(false);
            }
        }
    }

    [Header("Input")]
    [Tooltip("Input action used for pause, cancel and back navigation. Bind it to Escape and optionally gamepad Start/B.")]
    [SerializeField] private InputActionReference PauseCancelAction;

    [Tooltip("If true, this controller enables the pause/cancel action while active when it was not already enabled.")]
    [SerializeField] private bool EnablePauseCancelActionOnEnable = true;

    [Header("Modal Focus")]
    [Tooltip("Player modal state controller used to block gameplay input and unlock the cursor while menus are open.")]
    [SerializeField] private PlayerModalStateController PlayerModalStateController;

    [Header("Pause Menu Root")]
    [Tooltip("Optional parent object for all pause menu panels. Assign PAUSE_MENU if you want the whole menu hierarchy toggled together.")]
    [SerializeField] private GameObject PauseMenuRoot;

    [Header("Pause Menu Panels")]
    [Tooltip("Main pause panel shown after pressing Escape from gameplay.")]
    [SerializeField] private MenuPanel PauseMenuPanel = new MenuPanel();

    [Tooltip("Options panel opened from the pause menu.")]
    [SerializeField] private MenuPanel OptionsPanel = new MenuPanel();

    [Tooltip("Graphics settings panel opened from Options.")]
    [SerializeField] private MenuPanel GraphicsPanel = new MenuPanel();

    [Tooltip("Audio settings panel opened from Options.")]
    [SerializeField] private MenuPanel AudioPanel = new MenuPanel();

    [Tooltip("Controls category panel opened from Options.")]
    [SerializeField] private MenuPanel ControlsPanel = new MenuPanel();

    [Tooltip("Key bindings panel opened from Controls.")]
    [SerializeField] private MenuPanel KeybindsPanel = new MenuPanel();

    [Tooltip("Optional language settings panel opened from Options.")]
    [SerializeField] private MenuPanel LanguagePanel = new MenuPanel();

    [Tooltip("Save game panel opened from the pause menu.")]
    [SerializeField] private MenuPanel SavePanel = new MenuPanel();

    [Tooltip("Load game panel opened from the pause menu.")]
    [SerializeField] private MenuPanel LoadPanel = new MenuPanel();

    [Header("Save And Load Slots")]
    [Tooltip("Save controller used to write and load selected save slots.")]
    [SerializeField] private GameSaveDebugController SaveController;

    [Tooltip("If true, slot selection buttons are connected automatically during Awake.")]
    [SerializeField] private bool BindSlotButtonsAutomatically = true;

    [Tooltip("Text displayed in slot timestamp fields when no save data exists.")]
    [SerializeField] private string EmptySlotLabel = "NO DATA";

    [Tooltip("Button or root object that confirms saving into the selected slot. Hidden until a save slot is selected.")]
    [SerializeField] private GameObject SaveConfirmButtonRoot;

    [Tooltip("Button or root object that confirms loading the selected slot. Hidden until a valid load slot is selected.")]
    [SerializeField] private GameObject LoadConfirmButtonRoot;

    [Tooltip("Slot rows shown inside the save panel.")]
    [SerializeField] private List<SaveSlotView> SaveSlotViews = new List<SaveSlotView>();

    [Tooltip("Slot rows shown inside the load panel.")]
    [SerializeField] private List<SaveSlotView> LoadSlotViews = new List<SaveSlotView>();

    [Header("External Modal Panels")]
    [Tooltip("Panels opened outside the pause menu flow. Example: upgrades, shop, research, storage or machine UI.")]
    [SerializeField] private List<ExternalModalPanel> ExternalModalPanels = new List<ExternalModalPanel>();

    [Header("HUD Visibility")]
    [Tooltip("Gameplay HUD objects hidden while any configured modal menu is open. Do not assign the hotbar if it must stay visible.")]
    [SerializeField] private GameObject[] GameplayHudObjectsHiddenWhileModalOpen;

    [Header("Time")]
    [Tooltip("If true, opening the pause menu sets Time.timeScale to 0 and closing it restores the previous value.")]
    [SerializeField] private bool PauseWorldWhilePauseMenuOpen = true;

    [Header("Button Events")]
    [Tooltip("Event invoked after a save slot has been confirmed and written.")]
    [SerializeField] private UnityEvent OnSaveConfirmed;

    [Tooltip("Event invoked immediately before a confirmed load slot reloads the scene.")]
    [SerializeField] private UnityEvent OnLoadConfirmed;

    [Tooltip("Event invoked when the Main Menu button is pressed, after modal focus and time scale have been restored.")]
    [SerializeField] private UnityEvent OnMainMenuRequested;

    [Header("Startup")]
    [Tooltip("If true, all registered pause menu panels are hidden during Awake.")]
    [SerializeField] private bool HidePausePanelsOnAwake = true;

    [Tooltip("If true, all registered external modal panels are hidden during Awake.")]
    [SerializeField] private bool HideExternalModalsOnAwake = true;

    [Header("Debug")]
    [Tooltip("Logs menu flow transitions.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Ordered stack of currently opened pause menu panels.
    /// The first entry is usually the pause panel and the last entry is the visible active panel.
    /// </summary>
    private readonly List<MenuPanel> PanelStack = new List<MenuPanel>();

    /// <summary>
    /// Currently opened external modal panel, if any.
    /// External modals are not part of the pause menu stack.
    /// </summary>
    private ExternalModalPanel CurrentExternalModalPanel;

    /// <summary>
    /// True when this controller currently owns modal focus in PlayerModalStateController.
    /// </summary>
    private bool OwnsModalFocus;

    /// <summary>
    /// Time scale captured before this controller paused the world.
    /// </summary>
    private float PreviousTimeScale = 1f;

    /// <summary>
    /// True when this controller has modified Time.timeScale and must restore it later.
    /// </summary>
    private bool HasCapturedTimeScale;

    /// <summary>
    /// True when this controller enabled the pause/cancel action and should disable it later.
    /// </summary>
    private bool DidEnablePauseCancelAction;

    /// <summary>
    /// Currently selected save slot index, or -1 when no save slot is selected.
    /// </summary>
    private int SelectedSaveSlotIndex = -1;

    /// <summary>
    /// Currently selected load slot index, or -1 when no load slot is selected.
    /// </summary>
    private int SelectedLoadSlotIndex = -1;

    /// <summary>
    /// Gets whether the pause menu stack has at least one open panel.
    /// </summary>
    public bool IsPauseMenuOpen
    {
        get { return PanelStack.Count > 0; }
    }

    /// <summary>
    /// Gets whether any external modal is currently open.
    /// </summary>
    public bool IsExternalModalOpen
    {
        get { return CurrentExternalModalPanel != null; }
    }

    /// <summary>
    /// Gets whether any modal UI owned by this controller is currently open.
    /// </summary>
    public bool IsAnyMenuOrExternalModalOpen
    {
        get { return IsPauseMenuOpen || IsExternalModalOpen; }
    }

    /// <summary>
    /// Resolves missing references and hides configured panels on startup.
    /// </summary>
    private void Awake()
    {
        if (PlayerModalStateController == null)
        {
            PlayerModalStateController = FindFirstObjectByType<PlayerModalStateController>();
        }

        if (SaveController == null)
        {
            SaveController = FindFirstObjectByType<GameSaveDebugController>();
        }

        if (BindSlotButtonsAutomatically)
        {
            BindSaveAndLoadSlotButtons();
        }

        RefreshSaveAndLoadSlotPanels();

        if (HidePausePanelsOnAwake)
        {
            HideAllPauseMenuPanels();
            SetPauseMenuRootActive(false);
        }

        if (HideExternalModalsOnAwake)
        {
            HideAllExternalModalPanels();
        }

        ApplyGameplayHudVisibility(true);
    }

    /// <summary>
    /// Enables and subscribes to the pause/cancel input action.
    /// </summary>
    private void OnEnable()
    {
        if (PauseCancelAction == null || PauseCancelAction.action == null)
        {
            return;
        }

        PauseCancelAction.action.performed += HandlePauseCancelPerformed;

        if (EnablePauseCancelActionOnEnable && !PauseCancelAction.action.enabled)
        {
            PauseCancelAction.action.Enable();
            DidEnablePauseCancelAction = true;
        }
    }

    /// <summary>
    /// Unsubscribes from the pause/cancel input action and restores runtime state.
    /// </summary>
    private void OnDisable()
    {
        if (PauseCancelAction != null && PauseCancelAction.action != null)
        {
            PauseCancelAction.action.performed -= HandlePauseCancelPerformed;

            if (DidEnablePauseCancelAction)
            {
                PauseCancelAction.action.Disable();
                DidEnablePauseCancelAction = false;
            }
        }

        CloseAllMenusAndExternalModals();
    }

    /// <summary>
    /// Handles the pause/cancel input action.
    /// </summary>
    /// <param name="Context">Input System callback context.</param>
    private void HandlePauseCancelPerformed(InputAction.CallbackContext Context)
    {
        HandleBackOrPauseRequest();
    }

    /// <summary>
    /// Executes the global back or pause behaviour.
    /// External modals close first, menu subpanels go back, pause root closes, and gameplay opens pause.
    /// </summary>
    public void HandleBackOrPauseRequest()
    {
        if (CurrentExternalModalPanel != null)
        {
            CloseCurrentExternalModal();
            return;
        }

        if (PanelStack.Count > 1)
        {
            BackToPreviousPanel();
            return;
        }

        if (PanelStack.Count == 1)
        {
            ResumeGame();
            return;
        }

        OpenPauseMenu();
    }

    /// <summary>
    /// Opens the root pause menu from gameplay.
    /// </summary>
    public void OpenPauseMenu()
    {
        if (!PauseMenuPanel.GetIsAssigned())
        {
            Log("Cannot open pause menu because PauseMenuPanel is not assigned.");
            return;
        }

        if (CurrentExternalModalPanel != null)
        {
            CloseCurrentExternalModal();
            return;
        }

        if (!AcquireModalFocus())
        {
            Log("Cannot open pause menu because another modal owner is active.");
            return;
        }

        CaptureTimeScaleIfNeeded(PauseWorldWhilePauseMenuOpen);
        HideAllPauseMenuPanels();
        SetPauseMenuRootActive(true);

        PanelStack.Clear();
        PanelStack.Add(PauseMenuPanel);
        PauseMenuPanel.Show();
        SelectDefaultObject(PauseMenuPanel.GetDefaultSelectedObject());
        ApplyGameplayHudVisibility(false);
        Log("Pause menu opened.");
    }

    /// <summary>
    /// Closes the whole pause menu and returns to gameplay.
    /// </summary>
    public void ResumeGame()
    {
        HideAllPauseMenuPanels();
        SetPauseMenuRootActive(false);
        PanelStack.Clear();
        ReleaseModalFocusIfNoUiIsOpen();
        RestoreTimeScaleIfCaptured();
        ApplyGameplayHudVisibility(true);
        ClearSelectedObject();
        Log("Returned to gameplay.");
    }

    /// <summary>
    /// Opens the Options panel from the pause menu.
    /// </summary>
    public void OpenOptionsPanel()
    {
        OpenPauseSubPanel(OptionsPanel);
    }

    /// <summary>
    /// Opens the Graphics panel from the current menu stack.
    /// </summary>
    public void OpenGraphicsPanel()
    {
        OpenPauseSubPanel(GraphicsPanel);
    }

    /// <summary>
    /// Opens the Audio panel from the current menu stack.
    /// </summary>
    public void OpenAudioPanel()
    {
        OpenPauseSubPanel(AudioPanel);
    }

    /// <summary>
    /// Opens the Controls panel from the current menu stack.
    /// </summary>
    public void OpenControlsPanel()
    {
        OpenPauseSubPanel(ControlsPanel);
    }

    /// <summary>
    /// Opens the Keybinds panel from the current menu stack.
    /// </summary>
    public void OpenKeybindsPanel()
    {
        OpenPauseSubPanel(KeybindsPanel);
    }

    /// <summary>
    /// Opens the Language panel from the current menu stack.
    /// </summary>
    public void OpenLanguagePanel()
    {
        OpenPauseSubPanel(LanguagePanel);
    }

    /// <summary>
    /// Opens the save slot selection panel from the pause menu.
    /// </summary>
    public void OpenSavePanel()
    {
        SelectedSaveSlotIndex = -1;
        RefreshSaveSlotViews();
        ApplySaveConfirmVisibility();
        OpenPauseSubPanel(SavePanel);
    }

    /// <summary>
    /// Opens the load slot selection panel from the pause menu.
    /// </summary>
    public void OpenLoadPanel()
    {
        SelectedLoadSlotIndex = -1;
        RefreshLoadSlotViews();
        ApplyLoadConfirmVisibility();
        OpenPauseSubPanel(LoadPanel);
    }

    /// <summary>
    /// Selects one slot in the save panel. Saving is allowed for both empty and occupied slots.
    /// </summary>
    /// <param name="SlotIndex">Zero-based slot index.</param>
    public void SelectSaveSlot(int SlotIndex)
    {
        SelectedSaveSlotIndex = Mathf.Max(0, SlotIndex);
        RefreshSaveSlotViews();
        ApplySaveConfirmVisibility();
        Log("Selected save slot: " + (SelectedSaveSlotIndex + 1));
    }

    /// <summary>
    /// Selects one slot in the load panel. Loading is only confirmed if the selected slot has data.
    /// </summary>
    /// <param name="SlotIndex">Zero-based slot index.</param>
    public void SelectLoadSlot(int SlotIndex)
    {
        if (SaveController == null || !SaveController.DoesSaveSlotExist(SlotIndex))
        {
            SelectedLoadSlotIndex = -1;
            RefreshLoadSlotViews();
            ApplyLoadConfirmVisibility();
            Log("Ignored empty load slot: " + (SlotIndex + 1));
            return;
        }

        SelectedLoadSlotIndex = Mathf.Max(0, SlotIndex);
        RefreshLoadSlotViews();
        ApplyLoadConfirmVisibility();
        Log("Selected load slot: " + (SelectedLoadSlotIndex + 1));
    }

    /// <summary>
    /// Saves the current game into the selected save slot.
    /// </summary>
    public void ConfirmSelectedSaveSlot()
    {
        if (SaveController == null || SelectedSaveSlotIndex < 0)
        {
            return;
        }

        SaveController.SaveGameToSlot(SelectedSaveSlotIndex);
        RefreshSaveAndLoadSlotPanels();
        ApplySaveConfirmVisibility();
        OnSaveConfirmed?.Invoke();
    }

    /// <summary>
    /// Loads the selected load slot after restoring menu-owned runtime state.
    /// </summary>
    public void ConfirmSelectedLoadSlot()
    {
        if (SaveController == null || SelectedLoadSlotIndex < 0)
        {
            return;
        }

        if (!SaveController.DoesSaveSlotExist(SelectedLoadSlotIndex))
        {
            SelectedLoadSlotIndex = -1;
            RefreshLoadSlotViews();
            ApplyLoadConfirmVisibility();
            return;
        }

        int SlotIndexToLoad = SelectedLoadSlotIndex;
        OnLoadConfirmed?.Invoke();
        CloseAllMenusAndExternalModals();
        SaveController.LoadGameFromSlot(SlotIndexToLoad);
    }

    /// <summary>
    /// Returns to the previous pause menu panel, or resumes gameplay if only the root pause panel is open.
    /// </summary>
    public void BackToPreviousPanel()
    {
        if (CurrentExternalModalPanel != null)
        {
            CloseCurrentExternalModal();
            return;
        }

        if (PanelStack.Count <= 0)
        {
            return;
        }

        if (PanelStack.Count == 1)
        {
            ResumeGame();
            return;
        }

        MenuPanel CurrentPanel = PanelStack[PanelStack.Count - 1];
        PanelStack.RemoveAt(PanelStack.Count - 1);
        CurrentPanel.Hide();

        MenuPanel PreviousPanel = PanelStack[PanelStack.Count - 1];
        PreviousPanel.Show();
        SelectDefaultObject(PreviousPanel.GetDefaultSelectedObject());
        Log("Returned to previous panel: " + PreviousPanel.GetPanelId());
    }

    /// <summary>
    /// Opens an external modal panel by id. Use this from interaction scripts or UnityEvents.
    /// </summary>
    /// <param name="ModalId">Id of the external modal to open.</param>
    public void OpenExternalModalById(string ModalId)
    {
        TryOpenExternalModalById(ModalId);
    }

    /// <summary>
    /// Tries to open an external modal panel by id and returns whether it was opened.
    /// </summary>
    /// <param name="ModalId">Id of the external modal to open.</param>
    /// <returns>True if the modal was opened.</returns>
    public bool TryOpenExternalModalById(string ModalId)
    {
        ExternalModalPanel ExternalPanel = FindExternalModalPanel(ModalId);

        if (ExternalPanel == null)
        {
            Log("Cannot open external modal because no entry exists for id: " + ModalId);
            return false;
        }

        return OpenExternalModal(ExternalPanel);
    }

    /// <summary>
    /// Closes the current external modal if its id matches the provided id.
    /// </summary>
    /// <param name="ModalId">Id of the external modal to close.</param>
    public void CloseExternalModalById(string ModalId)
    {
        if (CurrentExternalModalPanel == null)
        {
            return;
        }

        if (!string.Equals(CurrentExternalModalPanel.GetModalId(), ModalId, StringComparison.Ordinal))
        {
            return;
        }

        CloseCurrentExternalModal();
    }

    /// <summary>
    /// Toggles an external modal by id.
    /// </summary>
    /// <param name="ModalId">Id of the external modal to toggle.</param>
    public void ToggleExternalModalById(string ModalId)
    {
        if (CurrentExternalModalPanel != null && string.Equals(CurrentExternalModalPanel.GetModalId(), ModalId, StringComparison.Ordinal))
        {
            CloseCurrentExternalModal();
            return;
        }

        TryOpenExternalModalById(ModalId);
    }

    /// <summary>
    /// Closes the currently open external modal, if one exists.
    /// </summary>
    public void CloseCurrentExternalModal()
    {
        if (CurrentExternalModalPanel == null)
        {
            return;
        }

        string ModalId = CurrentExternalModalPanel.GetModalId();
        CurrentExternalModalPanel.HideVisualOnly();
        CurrentExternalModalPanel = null;
        ReleaseModalFocusIfNoUiIsOpen();
        RestoreTimeScaleIfCaptured();
        ApplyGameplayHudVisibility(true);
        ClearSelectedObject();
        Log("External modal closed: " + ModalId);
    }

    /// <summary>
    /// Opens the save slot selection panel.
    /// Kept as a compatibility entry point for existing pause menu button events.
    /// </summary>
    public void RequestSave()
    {
        OpenSavePanel();
    }

    /// <summary>
    /// Opens the load slot selection panel.
    /// Kept as a compatibility entry point for existing pause menu button events.
    /// </summary>
    public void RequestLoad()
    {
        OpenLoadPanel();
    }

    /// <summary>
    /// Restores time and focus, then invokes the main menu button event.
    /// </summary>
    public void RequestMainMenu()
    {
        CloseAllMenusAndExternalModals();
        OnMainMenuRequested?.Invoke();
    }

    /// <summary>
    /// Closes all UI owned by this controller and restores gameplay state.
    /// </summary>
    public void CloseAllMenusAndExternalModals()
    {
        if (CurrentExternalModalPanel != null)
        {
            CurrentExternalModalPanel.HideVisualOnly();
            CurrentExternalModalPanel = null;
        }

        HideAllPauseMenuPanels();
        SetPauseMenuRootActive(false);
        PanelStack.Clear();
        ReleaseModalFocusIfNoUiIsOpen();
        RestoreTimeScaleIfCaptured();
        ApplyGameplayHudVisibility(true);
        ClearSelectedObject();
    }

    /// <summary>
    /// Opens one pause menu subpanel and pushes it onto the stack.
    /// </summary>
    /// <param name="TargetPanel">Panel to open.</param>
    private void OpenPauseSubPanel(MenuPanel TargetPanel)
    {
        if (TargetPanel == null || !TargetPanel.GetIsAssigned())
        {
            Log("Cannot open null or unassigned subpanel.");
            return;
        }

        if (CurrentExternalModalPanel != null)
        {
            Log("Cannot open pause subpanel while an external modal is open.");
            return;
        }

        if (PanelStack.Count == 0)
        {
            OpenPauseMenu();
        }

        if (PanelStack.Count == 0)
        {
            return;
        }

        MenuPanel CurrentPanel = PanelStack[PanelStack.Count - 1];

        if (CurrentPanel == TargetPanel)
        {
            return;
        }

        CurrentPanel.Hide();
        PanelStack.Add(TargetPanel);
        TargetPanel.Show();
        SelectDefaultObject(TargetPanel.GetDefaultSelectedObject());
        Log("Opened subpanel: " + TargetPanel.GetPanelId());
    }

    /// <summary>
    /// Opens the provided external modal panel.
    /// </summary>
    /// <param name="ExternalPanel">External modal panel to open.</param>
    /// <returns>True if the modal was opened.</returns>
    private bool OpenExternalModal(ExternalModalPanel ExternalPanel)
    {
        if (ExternalPanel == null || !ExternalPanel.GetIsAssigned())
        {
            return false;
        }

        if (PanelStack.Count > 0)
        {
            Log("Cannot open external modal while pause menu is open.");
            return false;
        }

        if (CurrentExternalModalPanel != null && CurrentExternalModalPanel != ExternalPanel)
        {
            Log("Cannot open external modal because another external modal is active.");
            return false;
        }

        if (!AcquireModalFocus())
        {
            Log("Cannot open external modal because another modal owner is active.");
            return false;
        }

        CurrentExternalModalPanel = ExternalPanel;
        CaptureTimeScaleIfNeeded(ExternalPanel.GetPauseWorldWhileOpen());
        ExternalPanel.ShowVisualOnly();
        SelectDefaultObject(ExternalPanel.GetDefaultSelectedObject());

        if (ExternalPanel.GetHideGameplayHudWhileOpen())
        {
            ApplyGameplayHudVisibility(false);
        }

        Log("External modal opened: " + ExternalPanel.GetModalId());
        return true;
    }

    /// <summary>
    /// Attempts to acquire modal focus from the player modal state controller.
    /// </summary>
    /// <returns>True if modal focus is owned by this controller.</returns>
    private bool AcquireModalFocus()
    {
        if (OwnsModalFocus)
        {
            return true;
        }

        if (PlayerModalStateController == null)
        {
            OwnsModalFocus = true;
            return true;
        }

        if (!PlayerModalStateController.TryOpenModal(this))
        {
            return false;
        }

        OwnsModalFocus = true;
        return true;
    }

    /// <summary>
    /// Releases modal focus only when no pause menu or external modal remains open.
    /// </summary>
    private void ReleaseModalFocusIfNoUiIsOpen()
    {
        if (CurrentExternalModalPanel != null || PanelStack.Count > 0)
        {
            return;
        }

        if (PlayerModalStateController != null && OwnsModalFocus)
        {
            PlayerModalStateController.CloseModal(this);
        }

        OwnsModalFocus = false;
    }

    /// <summary>
    /// Captures and pauses Time.timeScale if requested and not already captured.
    /// </summary>
    /// <param name="ShouldPauseWorld">True to pause the world.</param>
    private void CaptureTimeScaleIfNeeded(bool ShouldPauseWorld)
    {
        if (!ShouldPauseWorld || HasCapturedTimeScale)
        {
            return;
        }

        PreviousTimeScale = Time.timeScale;
        HasCapturedTimeScale = true;
        Time.timeScale = 0f;
    }

    /// <summary>
    /// Restores Time.timeScale if this controller previously paused it.
    /// </summary>
    private void RestoreTimeScaleIfCaptured()
    {
        if (!HasCapturedTimeScale)
        {
            return;
        }

        Time.timeScale = PreviousTimeScale;
        HasCapturedTimeScale = false;
    }

    /// <summary>
    /// Automatically binds configured slot buttons to their selection callbacks.
    /// </summary>
    private void BindSaveAndLoadSlotButtons()
    {
        for (int Index = 0; Index < SaveSlotViews.Count; Index++)
        {
            SaveSlotView SlotView = SaveSlotViews[Index];

            if (SlotView == null || SlotView.GetSlotButton() == null)
            {
                continue;
            }

            int CapturedSlotIndex = SlotView.GetSlotIndex();
            SlotView.GetSlotButton().onClick.AddListener(() => SelectSaveSlot(CapturedSlotIndex));
        }

        for (int Index = 0; Index < LoadSlotViews.Count; Index++)
        {
            SaveSlotView SlotView = LoadSlotViews[Index];

            if (SlotView == null || SlotView.GetSlotButton() == null)
            {
                continue;
            }

            int CapturedSlotIndex = SlotView.GetSlotIndex();
            SlotView.GetSlotButton().onClick.AddListener(() => SelectLoadSlot(CapturedSlotIndex));
        }
    }

    /// <summary>
    /// Refreshes both save and load slot views.
    /// </summary>
    private void RefreshSaveAndLoadSlotPanels()
    {
        RefreshSaveSlotViews();
        RefreshLoadSlotViews();
        ApplySaveConfirmVisibility();
        ApplyLoadConfirmVisibility();
    }

    /// <summary>
    /// Refreshes the save panel slot labels and selected state.
    /// </summary>
    private void RefreshSaveSlotViews()
    {
        for (int Index = 0; Index < SaveSlotViews.Count; Index++)
        {
            SaveSlotView SlotView = SaveSlotViews[Index];

            if (SlotView == null)
            {
                continue;
            }

            int SlotIndex = SlotView.GetSlotIndex();
            string TimestampLabel = SaveController != null
                ? SaveController.GetSaveSlotTimestampLabel(SlotIndex, EmptySlotLabel)
                : EmptySlotLabel;

            SlotView.Refresh(
                TimestampLabel,
                SlotIndex == SelectedSaveSlotIndex,
                true);
        }
    }

    /// <summary>
    /// Refreshes the load panel slot labels, selected state and interactability.
    /// Empty load slots remain visible but cannot be selected.
    /// </summary>
    private void RefreshLoadSlotViews()
    {
        for (int Index = 0; Index < LoadSlotViews.Count; Index++)
        {
            SaveSlotView SlotView = LoadSlotViews[Index];

            if (SlotView == null)
            {
                continue;
            }

            int SlotIndex = SlotView.GetSlotIndex();
            bool HasData = SaveController != null && SaveController.DoesSaveSlotExist(SlotIndex);
            string TimestampLabel = SaveController != null
                ? SaveController.GetSaveSlotTimestampLabel(SlotIndex, EmptySlotLabel)
                : EmptySlotLabel;

            SlotView.Refresh(
                TimestampLabel,
                HasData && SlotIndex == SelectedLoadSlotIndex,
                HasData);
        }
    }

    /// <summary>
    /// Shows the save confirmation button only after a save slot has been selected.
    /// </summary>
    private void ApplySaveConfirmVisibility()
    {
        if (SaveConfirmButtonRoot != null)
        {
            SaveConfirmButtonRoot.SetActive(SelectedSaveSlotIndex >= 0);
        }
    }

    /// <summary>
    /// Shows the load confirmation button only after a valid save slot has been selected.
    /// </summary>
    private void ApplyLoadConfirmVisibility()
    {
        bool CanLoad = SaveController != null &&
            SelectedLoadSlotIndex >= 0 &&
            SaveController.DoesSaveSlotExist(SelectedLoadSlotIndex);

        if (LoadConfirmButtonRoot != null)
        {
            LoadConfirmButtonRoot.SetActive(CanLoad);
        }
    }

    /// <summary>
    /// Hides every registered pause menu panel.
    /// </summary>
    private void HideAllPauseMenuPanels()
    {
        PauseMenuPanel.Hide();
        OptionsPanel.Hide();
        GraphicsPanel.Hide();
        AudioPanel.Hide();
        ControlsPanel.Hide();
        KeybindsPanel.Hide();
        LanguagePanel.Hide();
        SavePanel.Hide();
        LoadPanel.Hide();
    }

    /// <summary>
    /// Hides every registered external modal panel.
    /// </summary>
    private void HideAllExternalModalPanels()
    {
        for (int Index = 0; Index < ExternalModalPanels.Count; Index++)
        {
            if (ExternalModalPanels[Index] == null)
            {
                continue;
            }

            ExternalModalPanels[Index].HideVisualOnly();
        }
    }

    /// <summary>
    /// Applies active state to the optional pause menu parent root.
    /// </summary>
    /// <param name="IsActive">True to activate the root, false to deactivate it.</param>
    private void SetPauseMenuRootActive(bool IsActive)
    {
        if (PauseMenuRoot == null)
        {
            return;
        }

        PauseMenuRoot.SetActive(IsActive);
    }

    /// <summary>
    /// Applies visibility to gameplay HUD objects configured as hidden while menus are open.
    /// </summary>
    /// <param name="IsGameplayVisible">True to show gameplay HUD objects, false to hide them.</param>
    private void ApplyGameplayHudVisibility(bool IsGameplayVisible)
    {
        if (GameplayHudObjectsHiddenWhileModalOpen == null)
        {
            return;
        }

        for (int Index = 0; Index < GameplayHudObjectsHiddenWhileModalOpen.Length; Index++)
        {
            if (GameplayHudObjectsHiddenWhileModalOpen[Index] == null)
            {
                continue;
            }

            GameplayHudObjectsHiddenWhileModalOpen[Index].SetActive(IsGameplayVisible);
        }
    }

    /// <summary>
    /// Selects a default UI object through the current EventSystem.
    /// </summary>
    /// <param name="DefaultSelectedObject">Object to select.</param>
    private void SelectDefaultObject(GameObject DefaultSelectedObject)
    {
        if (EventSystem.current == null || DefaultSelectedObject == null || !DefaultSelectedObject.activeInHierarchy)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(DefaultSelectedObject);
    }

    /// <summary>
    /// Clears the selected UI object through the current EventSystem.
    /// </summary>
    private void ClearSelectedObject()
    {
        if (EventSystem.current == null)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Finds an external modal panel by id.
    /// </summary>
    /// <param name="ModalId">Id to search for.</param>
    /// <returns>Matching external modal panel, or null when none exists.</returns>
    private ExternalModalPanel FindExternalModalPanel(string ModalId)
    {
        if (string.IsNullOrWhiteSpace(ModalId))
        {
            return null;
        }

        for (int Index = 0; Index < ExternalModalPanels.Count; Index++)
        {
            ExternalModalPanel Panel = ExternalModalPanels[Index];

            if (Panel == null)
            {
                continue;
            }

            if (string.Equals(Panel.GetModalId(), ModalId, StringComparison.Ordinal))
            {
                return Panel;
            }
        }

        return null;
    }

    /// <summary>
    /// Logs menu flow messages when debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[GameMenuFlowController] " + Message, this);
    }
}

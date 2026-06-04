using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Inspector-driven audio trigger for UI elements, world prefabs, trigger volumes and simple scene events.
/// This component supports one generic audio event plus dedicated UI audio events such as hover, click and selection.
/// </summary>
[DisallowMultipleComponent]
public sealed class GameAudioTrigger : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerClickHandler,
    ISelectHandler,
    IDeselectHandler,
    ISubmitHandler,
    ICancelHandler
{
    /// <summary>
    /// Defines how the main audio event is played.
    /// UI-specific events always play through GameAudio.PlayUi.
    /// </summary>
    private enum PlaybackMode
    {
        Ui2D = 0,
        Sound2D = 1,
        Sound3DAtTransform = 2,
        LoopAttached = 3,
        Music = 4
    }

    /// <summary>
    /// Defines when the main audio event automatically plays.
    /// UI-specific events are controlled by EnableUiEvents and Unity's EventSystem callbacks.
    /// </summary>
    private enum TriggerMode
    {
        ManualOnly = 0,
        Awake = 1,
        Start = 2,
        OnEnable = 3,
        OnTriggerEnter = 4,
        OnTriggerExit = 5
    }

    [Header("Main Audio Event")]
    [Tooltip("Main audio event played by this trigger. Use this for world sounds, music, loops or simple manual UI calls.")]
    [SerializeField] private GameAudioEvent AudioEvent;

    [Tooltip("How the main audio event should be played.")]
    [SerializeField] private PlaybackMode PlayMode = PlaybackMode.Sound3DAtTransform;

    [Tooltip("When the main audio event should play automatically. Use Manual Only when another script or UnityEvent calls Play.")]
    [SerializeField] private TriggerMode Trigger = TriggerMode.ManualOnly;

    [Header("UI Audio Events")]
    [Tooltip("If true, this component listens to Unity UI pointer and navigation events such as hover, click, select and submit.")]
    [SerializeField] private bool EnableUiEvents = false;

    [Tooltip("If true, UI audio events are ignored when this object has a Selectable component that is not interactable.")]
    [SerializeField] private bool RespectSelectableInteractable = true;

    [Tooltip("Audio event played when the pointer enters this UI element.")]
    [SerializeField] private GameAudioEvent PointerEnterAudio;

    [Tooltip("Audio event played when the pointer exits this UI element.")]
    [SerializeField] private GameAudioEvent PointerExitAudio;

    [Tooltip("Audio event played when the pointer button is pressed down on this UI element.")]
    [SerializeField] private GameAudioEvent PointerDownAudio;

    [Tooltip("Audio event played when the pointer button is released on this UI element.")]
    [SerializeField] private GameAudioEvent PointerUpAudio;

    [Tooltip("Audio event played when this UI element receives a valid pointer click.")]
    [SerializeField] private GameAudioEvent PointerClickAudio;

    [Tooltip("Audio event played when this UI element is selected by keyboard, gamepad or UI navigation.")]
    [SerializeField] private GameAudioEvent SelectAudio;

    [Tooltip("Audio event played when this UI element is deselected by keyboard, gamepad or UI navigation.")]
    [SerializeField] private GameAudioEvent DeselectAudio;

    [Tooltip("Audio event played when this UI element receives a submit action from keyboard, gamepad or UI navigation.")]
    [SerializeField] private GameAudioEvent SubmitAudio;

    [Tooltip("Audio event played when this UI element receives a cancel action from keyboard, gamepad or UI navigation.")]
    [SerializeField] private GameAudioEvent CancelAudio;

    [Header("Position")]
    [Tooltip("Optional transform used as the playback position or loop follow target. If empty, this transform is used.")]
    [SerializeField] private Transform PlaybackTransform;

    [Header("Music")]
    [Tooltip("Fade duration used when this trigger starts or stops music.")]
    [SerializeField] private float MusicFadeDuration = 1f;

    [Header("Loop Control")]
    [Tooltip("If true, an already active loop will be stopped and restarted when Play is called again.")]
    [SerializeField] private bool RestartLoopWhenAlreadyPlaying = false;

    [Tooltip("If true, active loops started by this trigger are stopped when this component is disabled.")]
    [SerializeField] private bool StopLoopOnDisable = true;

    [Tooltip("If true, active loops started by this trigger are stopped when a valid object exits this trigger collider.")]
    [SerializeField] private bool StopLoopOnTriggerExit = true;

    [Header("Trigger Filter")]
    [Tooltip("If true, trigger enter and exit events only react to colliders with the required tag.")]
    [SerializeField] private bool UseRequiredTag = false;

    [Tooltip("Required tag for trigger enter and exit events when tag filtering is enabled.")]
    [SerializeField] private string RequiredTag = "Player";

    [Header("Debug")]
    [Tooltip("If true, this trigger logs basic playback warnings.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Active loop handle created by this trigger.
    /// </summary>
    private GameAudio.AudioHandle ActiveLoopHandle;

    /// <summary>
    /// Cached Selectable used to validate interactable UI state without repeated component lookups.
    /// </summary>
    private Selectable CachedSelectable;

    /// <summary>
    /// Caches optional UI components and plays automatically during Awake when configured to do so.
    /// </summary>
    private void Awake()
    {
        CachedSelectable = GetComponent<Selectable>();

        if (Trigger == TriggerMode.Awake)
        {
            Play();
        }
    }

    /// <summary>
    /// Plays automatically during Start when configured to do so.
    /// </summary>
    private void Start()
    {
        if (Trigger == TriggerMode.Start)
        {
            Play();
        }
    }

    /// <summary>
    /// Plays automatically during OnEnable when configured to do so.
    /// </summary>
    private void OnEnable()
    {
        if (Trigger == TriggerMode.OnEnable)
        {
            Play();
        }
    }

    /// <summary>
    /// Stops active loops when this trigger is disabled if configured to do so.
    /// </summary>
    private void OnDisable()
    {
        if (StopLoopOnDisable)
        {
            Stop();
        }
    }

    /// <summary>
    /// Plays the pointer enter UI audio event when UI events are enabled.
    /// </summary>
    /// <param name="EventData">Pointer event data provided by Unity's EventSystem.</param>
    public void OnPointerEnter(PointerEventData EventData)
    {
        PlayUiEventIfAllowed(PointerEnterAudio, "PointerEnter");
    }

    /// <summary>
    /// Plays the pointer exit UI audio event when UI events are enabled.
    /// </summary>
    /// <param name="EventData">Pointer event data provided by Unity's EventSystem.</param>
    public void OnPointerExit(PointerEventData EventData)
    {
        PlayUiEventIfAllowed(PointerExitAudio, "PointerExit");
    }

    /// <summary>
    /// Plays the pointer down UI audio event when UI events are enabled.
    /// </summary>
    /// <param name="EventData">Pointer event data provided by Unity's EventSystem.</param>
    public void OnPointerDown(PointerEventData EventData)
    {
        PlayUiEventIfAllowed(PointerDownAudio, "PointerDown");
    }

    /// <summary>
    /// Plays the pointer up UI audio event when UI events are enabled.
    /// </summary>
    /// <param name="EventData">Pointer event data provided by Unity's EventSystem.</param>
    public void OnPointerUp(PointerEventData EventData)
    {
        PlayUiEventIfAllowed(PointerUpAudio, "PointerUp");
    }

    /// <summary>
    /// Plays the pointer click UI audio event when UI events are enabled.
    /// </summary>
    /// <param name="EventData">Pointer event data provided by Unity's EventSystem.</param>
    public void OnPointerClick(PointerEventData EventData)
    {
        PlayUiEventIfAllowed(PointerClickAudio, "PointerClick");
    }

    /// <summary>
    /// Plays the selection UI audio event when UI events are enabled.
    /// </summary>
    /// <param name="EventData">Base event data provided by Unity's EventSystem.</param>
    public void OnSelect(BaseEventData EventData)
    {
        PlayUiEventIfAllowed(SelectAudio, "Select");
    }

    /// <summary>
    /// Plays the deselection UI audio event when UI events are enabled.
    /// </summary>
    /// <param name="EventData">Base event data provided by Unity's EventSystem.</param>
    public void OnDeselect(BaseEventData EventData)
    {
        PlayUiEventIfAllowed(DeselectAudio, "Deselect");
    }

    /// <summary>
    /// Plays the submit UI audio event when UI events are enabled.
    /// </summary>
    /// <param name="EventData">Base event data provided by Unity's EventSystem.</param>
    public void OnSubmit(BaseEventData EventData)
    {
        PlayUiEventIfAllowed(SubmitAudio, "Submit");
    }

    /// <summary>
    /// Plays the cancel UI audio event when UI events are enabled.
    /// </summary>
    /// <param name="EventData">Base event data provided by Unity's EventSystem.</param>
    public void OnCancel(BaseEventData EventData)
    {
        PlayUiEventIfAllowed(CancelAudio, "Cancel");
    }

    /// <summary>
    /// Plays automatically when a valid collider enters this trigger.
    /// </summary>
    /// <param name="Other">Collider that entered this trigger.</param>
    private void OnTriggerEnter(Collider Other)
    {
        if (Trigger != TriggerMode.OnTriggerEnter || !PassesTriggerFilter(Other))
        {
            return;
        }

        Play();
    }

    /// <summary>
    /// Plays or stops automatically when a valid collider exits this trigger.
    /// </summary>
    /// <param name="Other">Collider that exited this trigger.</param>
    private void OnTriggerExit(Collider Other)
    {
        if (!PassesTriggerFilter(Other))
        {
            return;
        }

        if (StopLoopOnTriggerExit)
        {
            Stop();
        }

        if (Trigger == TriggerMode.OnTriggerExit)
        {
            Play();
        }
    }

    /// <summary>
    /// Plays the configured main audio event using the selected playback mode.
    /// This method is intended to be called from UnityEvents, UI Buttons or gameplay code.
    /// </summary>
    public void Play()
    {
        if (AudioEvent == null)
        {
            LogWarning("Missing main AudioEvent on trigger: " + name);
            return;
        }

        Transform ResolvedTransform = GetPlaybackTransform();

        switch (PlayMode)
        {
            case PlaybackMode.Ui2D:
                GameAudio.PlayUi(AudioEvent);
                break;

            case PlaybackMode.Sound2D:
                GameAudio.Play(AudioEvent);
                break;

            case PlaybackMode.Sound3DAtTransform:
                GameAudio.PlayAt(AudioEvent, ResolvedTransform.position);
                break;

            case PlaybackMode.LoopAttached:
                PlayAttachedLoop(ResolvedTransform);
                break;

            case PlaybackMode.Music:
                GameAudio.PlayMusic(AudioEvent, MusicFadeDuration);
                break;
        }
    }

    /// <summary>
    /// Plays the configured pointer enter UI audio event manually.
    /// This is useful for explicit UnityEvent wiring when automatic UI events are disabled.
    /// </summary>
    public void PlayPointerEnter()
    {
        PlayUiEvent(PointerEnterAudio, "PointerEnter");
    }

    /// <summary>
    /// Plays the configured pointer exit UI audio event manually.
    /// </summary>
    public void PlayPointerExit()
    {
        PlayUiEvent(PointerExitAudio, "PointerExit");
    }

    /// <summary>
    /// Plays the configured pointer down UI audio event manually.
    /// </summary>
    public void PlayPointerDown()
    {
        PlayUiEvent(PointerDownAudio, "PointerDown");
    }

    /// <summary>
    /// Plays the configured pointer up UI audio event manually.
    /// </summary>
    public void PlayPointerUp()
    {
        PlayUiEvent(PointerUpAudio, "PointerUp");
    }

    /// <summary>
    /// Plays the configured pointer click UI audio event manually.
    /// </summary>
    public void PlayPointerClick()
    {
        PlayUiEvent(PointerClickAudio, "PointerClick");
    }

    /// <summary>
    /// Plays the configured selection UI audio event manually.
    /// </summary>
    public void PlaySelect()
    {
        PlayUiEvent(SelectAudio, "Select");
    }

    /// <summary>
    /// Plays the configured deselection UI audio event manually.
    /// </summary>
    public void PlayDeselect()
    {
        PlayUiEvent(DeselectAudio, "Deselect");
    }

    /// <summary>
    /// Plays the configured submit UI audio event manually.
    /// </summary>
    public void PlaySubmit()
    {
        PlayUiEvent(SubmitAudio, "Submit");
    }

    /// <summary>
    /// Plays the configured cancel UI audio event manually.
    /// </summary>
    public void PlayCancel()
    {
        PlayUiEvent(CancelAudio, "Cancel");
    }

    /// <summary>
    /// Convenience alias for hover sounds used by UnityEvents.
    /// </summary>
    public void PlayHover()
    {
        PlayPointerEnter();
    }

    /// <summary>
    /// Convenience alias for click sounds used by UnityEvents.
    /// </summary>
    public void PlayClick()
    {
        PlayPointerClick();
    }

    /// <summary>
    /// Stops the active loop created by this trigger, if any.
    /// This method can be called from UnityEvents or gameplay code.
    /// </summary>
    public void Stop()
    {
        if (ActiveLoopHandle == null || !ActiveLoopHandle.IsValid)
        {
            return;
        }

        GameAudio.Stop(ActiveLoopHandle);
        ActiveLoopHandle = null;
    }

    /// <summary>
    /// Stops currently playing music through the global audio service.
    /// This method can be called from UnityEvents or gameplay code.
    /// </summary>
    public void StopMusic()
    {
        GameAudio.StopMusic(MusicFadeDuration);
    }

    /// <summary>
    /// Plays an attached loop while preventing accidental duplicate loops.
    /// </summary>
    /// <param name="Target">Transform followed by the loop.</param>
    private void PlayAttachedLoop(Transform Target)
    {
        if (ActiveLoopHandle != null && ActiveLoopHandle.IsValid)
        {
            if (!RestartLoopWhenAlreadyPlaying)
            {
                return;
            }

            Stop();
        }

        ActiveLoopHandle = GameAudio.PlayLoopAttached(AudioEvent, Target);
    }

    /// <summary>
    /// Plays a UI audio event only when automatic UI events are enabled and the current Selectable state allows it.
    /// </summary>
    /// <param name="UiAudioEvent">UI audio event to play.</param>
    /// <param name="EventName">Name used for debug warnings.</param>
    private void PlayUiEventIfAllowed(GameAudioEvent UiAudioEvent, string EventName)
    {
        if (!EnableUiEvents)
        {
            return;
        }

        if (!CanPlayUiEvent())
        {
            return;
        }

        PlayUiEvent(UiAudioEvent, EventName);
    }

    /// <summary>
    /// Plays a UI audio event directly through the global audio service.
    /// </summary>
    /// <param name="UiAudioEvent">UI audio event to play.</param>
    /// <param name="EventName">Name used for debug warnings.</param>
    private void PlayUiEvent(GameAudioEvent UiAudioEvent, string EventName)
    {
        if (UiAudioEvent == null)
        {
            return;
        }

        GameAudio.PlayUi(UiAudioEvent);
    }

    /// <summary>
    /// Returns true when this UI element is allowed to emit UI audio events.
    /// </summary>
    private bool CanPlayUiEvent()
    {
        if (!RespectSelectableInteractable)
        {
            return true;
        }

        if (CachedSelectable == null)
        {
            CachedSelectable = GetComponent<Selectable>();
        }

        if (CachedSelectable == null)
        {
            return true;
        }

        return CachedSelectable.IsInteractable();
    }

    /// <summary>
    /// Gets the transform used for positional playback.
    /// </summary>
    /// <returns>Configured playback transform or this transform if none is assigned.</returns>
    private Transform GetPlaybackTransform()
    {
        return PlaybackTransform != null ? PlaybackTransform : transform;
    }

    /// <summary>
    /// Returns true if a collider is allowed to activate this trigger.
    /// </summary>
    /// <param name="Other">Collider to validate.</param>
    /// <returns>True when the collider passes this trigger filter.</returns>
    private bool PassesTriggerFilter(Collider Other)
    {
        if (Other == null)
        {
            return false;
        }

        if (!UseRequiredTag)
        {
            return true;
        }

        return Other.CompareTag(RequiredTag);
    }

    /// <summary>
    /// Logs a warning if debug logging is enabled.
    /// </summary>
    /// <param name="Message">Warning message.</param>
    private void LogWarning(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.LogWarning("[GameAudioTrigger] " + Message, this);
    }

    /// <summary>
    /// Clamps editor values to safe ranges.
    /// </summary>
    private void OnValidate()
    {
        MusicFadeDuration = Mathf.Max(0f, MusicFadeDuration);
    }
}

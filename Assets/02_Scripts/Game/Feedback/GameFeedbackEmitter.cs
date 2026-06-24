using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MoreMountains.Feedbacks;
using UnityEngine;

/// <summary>
/// Generic feedback playback component used by tools, ores, machines, stations and UI adapters.
/// It can play particles, Visual Effect Graph prefabs, decals and audio from a GameFeedbackProfile,
/// plus prefab-specific local ParticleSystem, VisualEffect and Feel bindings.
/// </summary>
public sealed class GameFeedbackEmitter : MonoBehaviour
{
    private const string VisualEffectTypeName = "UnityEngine.VFX.VisualEffect";

    [Serializable]
    private sealed class LocalFeedbackBinding
    {
        [Tooltip("Stable event id that triggers this local binding.")]
        [SerializeField] private string EventId;

        [Tooltip("Local particle systems played when this event fires. Use this for viewmodel particles that should already exist under the prefab.")]
        [SerializeField] private ParticleSystem[] LocalParticleSystems = Array.Empty<ParticleSystem>();

        [Tooltip("Local Visual Effect Graph GameObjects played when this event fires. Assign the GameObject that owns the VisualEffect component or a parent that contains it.")]
        [SerializeField] private GameObject[] LocalVisualEffectObjects = Array.Empty<GameObject>();

        [Tooltip("Feel players triggered when this event fires.")]
        [SerializeField] private MMF_Player[] FeelPlayers = Array.Empty<MMF_Player>();

        /// <summary>
        /// Gets the event id used by this local binding.
        /// </summary>
        public string GetEventId() => EventId;

        /// <summary>
        /// Gets local particle systems configured for this binding.
        /// </summary>
        public IReadOnlyList<ParticleSystem> GetLocalParticleSystems() => LocalParticleSystems ?? Array.Empty<ParticleSystem>();

        /// <summary>
        /// Gets local visual effect objects configured for this binding.
        /// </summary>
        public IReadOnlyList<GameObject> GetLocalVisualEffectObjects() => LocalVisualEffectObjects ?? Array.Empty<GameObject>();

        /// <summary>
        /// Gets Feel players configured for this binding.
        /// </summary>
        public IReadOnlyList<MMF_Player> GetFeelPlayers() => FeelPlayers ?? Array.Empty<MMF_Player>();
    }

    [Header("Profile")]
    [Tooltip("Reusable feedback profile used to spawn particles, visual effects, decals and play audio for requested event ids.")]
    [SerializeField] private GameFeedbackProfile FeedbackProfile;

    [Header("Fallback")]
    [Tooltip("Fallback transform used when a feedback context does not provide a valid world position.")]
    [SerializeField] private Transform FallbackFeedbackRoot;

    [Header("Local Bindings")]
    [Tooltip("Prefab-local particles, Visual Effects and Feel players mapped to event ids.")]
    [SerializeField] private List<LocalFeedbackBinding> LocalBindings = new();

    [Header("Debug")]
    [Tooltip("Logs feedback playback requests and missing event ids.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Gets the currently assigned feedback profile.
    /// </summary>
    public GameFeedbackProfile GetFeedbackProfile()
    {
        return FeedbackProfile;
    }

    /// <summary>
    /// Replaces the feedback profile used by this emitter.
    /// </summary>
    /// <param name="Profile">New feedback profile.</param>
    public void SetFeedbackProfile(GameFeedbackProfile Profile)
    {
        FeedbackProfile = Profile;
    }

    /// <summary>
    /// Plays a feedback event using this transform as fallback context.
    /// </summary>
    /// <param name="EventId">Stable event id to play.</param>
    public void Play(string EventId)
    {
        Play(EventId, GameFeedbackContext.FromTransform(GetFallbackRoot()));
    }

    /// <summary>
    /// Plays a feedback event using the provided runtime context.
    /// </summary>
    /// <param name="EventId">Stable event id to play.</param>
    /// <param name="Context">Runtime feedback context.</param>
    public void Play(string EventId, GameFeedbackContext Context)
    {
        if (string.IsNullOrWhiteSpace(EventId))
        {
            return;
        }

        bool PlayedSomething = false;
        GameFeedbackProfile.GameFeedbackEvent FeedbackEvent = null;
        GameFeedbackProfile.EventResolutionStatus ProfileStatus = GameFeedbackProfile.EventResolutionStatus.MissingProfileEvent;

        if (FeedbackProfile != null)
        {
            ProfileStatus = FeedbackProfile.ResolveEvent(EventId, out FeedbackEvent);

            if (ProfileStatus == GameFeedbackProfile.EventResolutionStatus.EnabledProfileEvent)
            {
                PlayProfileEvent(FeedbackEvent, Context);
                PlayedSomething = true;
            }
        }

        if (PlayLocalBindings(EventId, FeedbackEvent, Context))
        {
            PlayedSomething = true;
        }

        if (!PlayedSomething)
        {
            Log(BuildMissingFeedbackMessage(EventId, ProfileStatus));
            return;
        }

        Log("Played feedback event: " + EventId);
    }

    /// <summary>
    /// Plays particle, visual effect, decal and audio data configured in a profile event.
    /// </summary>
    /// <param name="FeedbackEvent">Profile event to play.</param>
    /// <param name="Context">Runtime feedback context.</param>
    private void PlayProfileEvent(GameFeedbackProfile.GameFeedbackEvent FeedbackEvent, GameFeedbackContext Context)
    {
        if (FeedbackEvent == null)
        {
            return;
        }

        Vector3 Position = ResolvePosition(Context);
        PlayParticlePrefabs(FeedbackEvent, Position, ResolveParticleRotation(FeedbackEvent, Context), Context);
        PlayVisualEffectPrefabs(FeedbackEvent, Position, ResolveVisualEffectRotation(FeedbackEvent, Context), Context);
        PlayDecalPrefabs(FeedbackEvent, Position, ResolveDecalRotation(FeedbackEvent, Context), Context);
        PlayAudioEvents(FeedbackEvent, Position);
    }

    /// <summary>
    /// Spawns configured particle prefabs for a profile event.
    /// </summary>
    /// <param name="FeedbackEvent">Profile event that owns particle settings.</param>
    /// <param name="Position">Resolved world position.</param>
    /// <param name="Rotation">Resolved world rotation.</param>
    /// <param name="Context">Runtime feedback context.</param>
    private void PlayParticlePrefabs(GameFeedbackProfile.GameFeedbackEvent FeedbackEvent, Vector3 Position, Quaternion Rotation, GameFeedbackContext Context)
    {
        IReadOnlyList<GameObject> ParticlePrefabs = FeedbackEvent.GetParticlePrefabs();

        if (ParticlePrefabs == null || ParticlePrefabs.Count == 0)
        {
            return;
        }

        if (FeedbackEvent.GetParticleSelectionMode() == GameFeedbackProfile.FeedbackSelectionMode.RandomOne)
        {
            GameObject RandomPrefab = GetRandomValidPrefab(ParticlePrefabs);
            SpawnParticlePrefab(RandomPrefab, FeedbackEvent, Position, Rotation, Context);
            return;
        }

        for (int Index = 0; Index < ParticlePrefabs.Count; Index++)
        {
            SpawnParticlePrefab(ParticlePrefabs[Index], FeedbackEvent, Position, Rotation, Context);
        }
    }

    /// <summary>
    /// Spawns one particle prefab and starts any ParticleSystem found inside it.
    /// </summary>
    /// <param name="ParticlePrefab">Particle prefab to spawn.</param>
    /// <param name="FeedbackEvent">Profile event containing spawn settings.</param>
    /// <param name="Position">Resolved world position.</param>
    /// <param name="Rotation">Resolved world rotation.</param>
    /// <param name="Context">Runtime feedback context.</param>
    private void SpawnParticlePrefab(GameObject ParticlePrefab, GameFeedbackProfile.GameFeedbackEvent FeedbackEvent, Vector3 Position, Quaternion Rotation, GameFeedbackContext Context)
    {
        if (ParticlePrefab == null)
        {
            return;
        }

        Vector3 SpawnPosition = Position + (Rotation * FeedbackEvent.GetParticlePositionOffset());
        Transform ParentTransform = FeedbackEvent.GetParentParticlesToContextParent() ? Context.ParentTransform : null;
        GameObject Instance = Instantiate(ParticlePrefab, SpawnPosition, Rotation, ParentTransform);
        ParticleSystem[] ParticleSystems = Instance.GetComponentsInChildren<ParticleSystem>(true);

        for (int Index = 0; Index < ParticleSystems.Length; Index++)
        {
            if (ParticleSystems[Index] != null)
            {
                ParticleSystems[Index].Play(true);
            }
        }

        float DestroyDelay = FeedbackEvent.GetParticleDestroyDelay();

        if (DestroyDelay > 0f)
        {
            Destroy(Instance, DestroyDelay);
        }
    }

    /// <summary>
    /// Spawns configured Visual Effect Graph prefabs for a profile event.
    /// </summary>
    /// <param name="FeedbackEvent">Profile event that owns visual effect settings.</param>
    /// <param name="Position">Resolved world position.</param>
    /// <param name="Rotation">Resolved world rotation.</param>
    /// <param name="Context">Runtime feedback context.</param>
    private void PlayVisualEffectPrefabs(GameFeedbackProfile.GameFeedbackEvent FeedbackEvent, Vector3 Position, Quaternion Rotation, GameFeedbackContext Context)
    {
        IReadOnlyList<GameObject> VisualEffectPrefabs = FeedbackEvent.GetVisualEffectPrefabs();

        if (VisualEffectPrefabs == null || VisualEffectPrefabs.Count == 0)
        {
            return;
        }

        if (FeedbackEvent.GetVisualEffectSelectionMode() == GameFeedbackProfile.FeedbackSelectionMode.RandomOne)
        {
            GameObject RandomPrefab = GetRandomValidVisualEffectPrefab(VisualEffectPrefabs);
            SpawnVisualEffectPrefab(RandomPrefab, FeedbackEvent, Position, Rotation, Context);
            return;
        }

        for (int Index = 0; Index < VisualEffectPrefabs.Count; Index++)
        {
            SpawnVisualEffectPrefab(VisualEffectPrefabs[Index], FeedbackEvent, Position, Rotation, Context);
        }
    }

    /// <summary>
    /// Spawns one visual effect prefab and starts any VisualEffect component found inside it.
    /// Reflection is used so this generic gameplay assembly does not require a hard compile-time dependency on UnityEngine.VFX.
    /// </summary>
    /// <param name="VisualEffectPrefab">Visual effect prefab to spawn.</param>
    /// <param name="FeedbackEvent">Profile event containing spawn settings.</param>
    /// <param name="Position">Resolved world position.</param>
    /// <param name="Rotation">Resolved world rotation.</param>
    /// <param name="Context">Runtime feedback context.</param>
    private void SpawnVisualEffectPrefab(GameObject VisualEffectPrefab, GameFeedbackProfile.GameFeedbackEvent FeedbackEvent, Vector3 Position, Quaternion Rotation, GameFeedbackContext Context)
    {
        if (VisualEffectPrefab == null)
        {
            return;
        }

        if (!ContainsVisualEffectComponent(VisualEffectPrefab))
        {
            Log("Ignored visual effect prefab because it does not contain a VisualEffect component: " + VisualEffectPrefab.name);
            return;
        }

        Vector3 SpawnPosition = Position + (Rotation * FeedbackEvent.GetVisualEffectPositionOffset());
        Transform ParentTransform = FeedbackEvent.GetParentVisualEffectsToContextParent() ? Context.ParentTransform : null;
        GameObject Instance = Instantiate(VisualEffectPrefab, SpawnPosition, Rotation, ParentTransform);
        Component[] VisualEffects = GetVisualEffectComponentsInHierarchy(Instance);
        PlayVisualEffects(VisualEffects, FeedbackEvent.GetReinitializeVisualEffectsBeforePlay());
        ScheduleVisualEffectStop(VisualEffects, FeedbackEvent.GetVisualEffectStopDelay());

        float DestroyDelay = FeedbackEvent.GetVisualEffectDestroyDelay();

        if (DestroyDelay > 0f)
        {
            Destroy(Instance, DestroyDelay);
        }
    }

    /// <summary>
    /// Spawns configured decal prefabs for a profile event.
    /// </summary>
    /// <param name="FeedbackEvent">Profile event that owns decal settings.</param>
    /// <param name="Position">Resolved world position.</param>
    /// <param name="Rotation">Resolved world rotation.</param>
    /// <param name="Context">Runtime feedback context.</param>
    private void PlayDecalPrefabs(GameFeedbackProfile.GameFeedbackEvent FeedbackEvent, Vector3 Position, Quaternion Rotation, GameFeedbackContext Context)
    {
        IReadOnlyList<GameObject> DecalPrefabs = FeedbackEvent.GetDecalPrefabs();

        if (DecalPrefabs == null || DecalPrefabs.Count == 0)
        {
            return;
        }

        if (!Context.HasNormal)
        {
            Log("Decal feedback requested without a surface normal. Event id: " + FeedbackEvent.GetEventId());
        }

        if (FeedbackEvent.GetDecalSelectionMode() == GameFeedbackProfile.FeedbackSelectionMode.RandomOne)
        {
            GameObject RandomPrefab = GetRandomValidPrefab(DecalPrefabs);
            SpawnDecalPrefab(RandomPrefab, FeedbackEvent, Position, Rotation, Context);
            return;
        }

        for (int Index = 0; Index < DecalPrefabs.Count; Index++)
        {
            SpawnDecalPrefab(DecalPrefabs[Index], FeedbackEvent, Position, Rotation, Context);
        }
    }

    /// <summary>
    /// Spawns one decal prefab at the feedback surface.
    /// </summary>
    /// <param name="DecalPrefab">Decal prefab to spawn.</param>
    /// <param name="FeedbackEvent">Profile event containing spawn settings.</param>
    /// <param name="Position">Resolved world position.</param>
    /// <param name="Rotation">Resolved world rotation.</param>
    /// <param name="Context">Runtime feedback context.</param>
    private void SpawnDecalPrefab(GameObject DecalPrefab, GameFeedbackProfile.GameFeedbackEvent FeedbackEvent, Vector3 Position, Quaternion Rotation, GameFeedbackContext Context)
    {
        if (DecalPrefab == null)
        {
            return;
        }

        Vector3 Normal = Context.HasNormal ? Context.Normal.normalized : Rotation * Vector3.forward;
        Vector3 SpawnPosition = Position + (Normal * FeedbackEvent.GetDecalSurfaceOffset()) + (Rotation * FeedbackEvent.GetDecalPositionOffset());
        Transform ParentTransform = FeedbackEvent.GetParentDecalsToContextParent() ? Context.ParentTransform : null;
        GameObject Instance = Instantiate(DecalPrefab, SpawnPosition, Rotation, ParentTransform);

        float DestroyDelay = FeedbackEvent.GetDecalDestroyDelay();

        if (DestroyDelay > 0f)
        {
            Destroy(Instance, DestroyDelay);
        }
    }

    /// <summary>
    /// Gets a random non-null prefab from a list.
    /// </summary>
    /// <param name="Prefabs">Prefab list to sample.</param>
    /// <returns>Random valid prefab, or null.</returns>
    private GameObject GetRandomValidPrefab(IReadOnlyList<GameObject> Prefabs)
    {
        if (Prefabs == null || Prefabs.Count == 0)
        {
            return null;
        }

        int StartIndex = UnityEngine.Random.Range(0, Prefabs.Count);

        for (int Offset = 0; Offset < Prefabs.Count; Offset++)
        {
            GameObject Candidate = Prefabs[(StartIndex + Offset) % Prefabs.Count];

            if (Candidate != null)
            {
                return Candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets a random prefab that contains at least one VisualEffect component.
    /// </summary>
    /// <param name="Prefabs">Visual effect prefab list to sample.</param>
    /// <returns>Random valid VisualEffect prefab, or null.</returns>
    private GameObject GetRandomValidVisualEffectPrefab(IReadOnlyList<GameObject> Prefabs)
    {
        if (Prefabs == null || Prefabs.Count == 0)
        {
            return null;
        }

        int StartIndex = UnityEngine.Random.Range(0, Prefabs.Count);

        for (int Offset = 0; Offset < Prefabs.Count; Offset++)
        {
            GameObject Candidate = Prefabs[(StartIndex + Offset) % Prefabs.Count];

            if (Candidate != null && ContainsVisualEffectComponent(Candidate))
            {
                return Candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns whether a root object contains at least one VisualEffect component.
    /// </summary>
    /// <param name="RootObject">Root object to inspect.</param>
    /// <returns>True if a VisualEffect component exists in the hierarchy.</returns>
    private bool ContainsVisualEffectComponent(GameObject RootObject)
    {
        return GetVisualEffectComponentsInHierarchy(RootObject).Length > 0;
    }

    /// <summary>
    /// Finds every VisualEffect component under a root object using reflection-safe component scanning.
    /// </summary>
    /// <param name="RootObject">Root object that may contain VisualEffect components.</param>
    /// <returns>VisualEffect components as generic Component references.</returns>
    private Component[] GetVisualEffectComponentsInHierarchy(GameObject RootObject)
    {
        if (RootObject == null)
        {
            return Array.Empty<Component>();
        }

        Component[] Components = RootObject.GetComponentsInChildren<Component>(true);
        List<Component> VisualEffects = null;

        for (int Index = 0; Index < Components.Length; Index++)
        {
            Component Candidate = Components[Index];

            if (Candidate == null || Candidate.GetType().FullName != VisualEffectTypeName)
            {
                continue;
            }

            VisualEffects ??= new List<Component>();
            VisualEffects.Add(Candidate);
        }

        return VisualEffects != null ? VisualEffects.ToArray() : Array.Empty<Component>();
    }

    /// <summary>
    /// Reinitializes and plays VisualEffect components through reflection.
    /// </summary>
    /// <param name="VisualEffects">VisualEffect components to play.</param>
    /// <param name="ReinitializeBeforePlay">If true, Reinit is called before Play.</param>
    private void PlayVisualEffects(IReadOnlyList<Component> VisualEffects, bool ReinitializeBeforePlay)
    {
        if (VisualEffects == null)
        {
            return;
        }

        for (int Index = 0; Index < VisualEffects.Count; Index++)
        {
            Component VisualEffectComponent = VisualEffects[Index];

            if (VisualEffectComponent == null)
            {
                continue;
            }

            TryInvokeVisualEffectMethod(VisualEffectComponent, ReinitializeBeforePlay ? "Reinit" : string.Empty);
            TryInvokeVisualEffectMethod(VisualEffectComponent, "Play");
        }
    }

    /// <summary>
    /// Schedules a delayed Stop call for VisualEffect components.
    /// </summary>
    /// <param name="VisualEffects">VisualEffect components to stop.</param>
    /// <param name="StopDelay">Seconds before Stop is called.</param>
    private void ScheduleVisualEffectStop(IReadOnlyList<Component> VisualEffects, float StopDelay)
    {
        if (VisualEffects == null || VisualEffects.Count == 0 || StopDelay <= 0f)
        {
            return;
        }

        StartCoroutine(StopVisualEffectsAfterDelay(VisualEffects, StopDelay));
    }

    /// <summary>
    /// Stops VisualEffect components after a delay.
    /// </summary>
    /// <param name="VisualEffects">VisualEffect components to stop.</param>
    /// <param name="StopDelay">Delay in seconds.</param>
    private IEnumerator StopVisualEffectsAfterDelay(IReadOnlyList<Component> VisualEffects, float StopDelay)
    {
        yield return new WaitForSeconds(StopDelay);

        if (VisualEffects == null)
        {
            yield break;
        }

        for (int Index = 0; Index < VisualEffects.Count; Index++)
        {
            Component VisualEffectComponent = VisualEffects[Index];

            if (VisualEffectComponent == null)
            {
                continue;
            }

            TryInvokeVisualEffectMethod(VisualEffectComponent, "Stop");
        }
    }

    /// <summary>
    /// Invokes a VisualEffect method if it exists.
    /// </summary>
    /// <param name="VisualEffectComponent">VisualEffect component.</param>
    /// <param name="MethodName">Method name to invoke.</param>
    private void TryInvokeVisualEffectMethod(Component VisualEffectComponent, string MethodName)
    {
        if (VisualEffectComponent == null || string.IsNullOrWhiteSpace(MethodName))
        {
            return;
        }

        try
        {
            MethodInfo Method = VisualEffectComponent.GetType().GetMethod(MethodName, Type.EmptyTypes);
            Method?.Invoke(VisualEffectComponent, null);
        }
        catch (Exception Exception)
        {
            Log("VisualEffect method invocation failed. Method: " + MethodName + " | Object: " + VisualEffectComponent.name + " | Error: " + Exception.Message);
        }
    }

    /// <summary>
    /// Plays configured audio events for a profile event.
    /// </summary>
    /// <param name="FeedbackEvent">Profile event that owns audio settings.</param>
    /// <param name="Position">World position used by spatial audio.</param>
    private void PlayAudioEvents(GameFeedbackProfile.GameFeedbackEvent FeedbackEvent, Vector3 Position)
    {
        IReadOnlyList<GameAudioEvent> AudioEvents = FeedbackEvent.GetAudioEvents();

        if (AudioEvents == null || AudioEvents.Count == 0)
        {
            return;
        }

        if (FeedbackEvent.GetAudioSelectionMode() == GameFeedbackProfile.FeedbackSelectionMode.RandomOne)
        {
            GameAudioEvent RandomAudioEvent = GetRandomValidAudioEvent(AudioEvents);
            PlayAudioEvent(RandomAudioEvent, Position, FeedbackEvent.GetPlayAudioAtWorldPosition());
            return;
        }

        for (int Index = 0; Index < AudioEvents.Count; Index++)
        {
            PlayAudioEvent(AudioEvents[Index], Position, FeedbackEvent.GetPlayAudioAtWorldPosition());
        }
    }

    /// <summary>
    /// Gets a random non-null audio event from a list.
    /// </summary>
    /// <param name="AudioEvents">Audio event list to sample.</param>
    /// <returns>Random valid audio event, or null.</returns>
    private GameAudioEvent GetRandomValidAudioEvent(IReadOnlyList<GameAudioEvent> AudioEvents)
    {
        if (AudioEvents == null || AudioEvents.Count == 0)
        {
            return null;
        }

        int StartIndex = UnityEngine.Random.Range(0, AudioEvents.Count);

        for (int Offset = 0; Offset < AudioEvents.Count; Offset++)
        {
            GameAudioEvent Candidate = AudioEvents[(StartIndex + Offset) % AudioEvents.Count];

            if (Candidate != null)
            {
                return Candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Plays one audio event through the central audio service.
    /// </summary>
    /// <param name="AudioEvent">Audio event to play.</param>
    /// <param name="Position">World position used for 3D playback.</param>
    /// <param name="PlayAtWorldPosition">True to use 3D playback.</param>
    private void PlayAudioEvent(GameAudioEvent AudioEvent, Vector3 Position, bool PlayAtWorldPosition)
    {
        if (AudioEvent == null)
        {
            return;
        }

        if (PlayAtWorldPosition)
        {
            GameAudio.PlayAt(AudioEvent, Position);
            return;
        }

        GameAudio.Play(AudioEvent);
    }

    /// <summary>
    /// Plays prefab-local particles, Visual Effects and Feel feedbacks for an event id.
    /// </summary>
    /// <param name="EventId">Requested event id.</param>
    /// <param name="FeedbackEvent">Optional profile event used to scale Feel intensity and VFX lifecycle.</param>
    /// <param name="Context">Runtime feedback context.</param>
    /// <returns>True if at least one local binding played.</returns>
    private bool PlayLocalBindings(string EventId, GameFeedbackProfile.GameFeedbackEvent FeedbackEvent, GameFeedbackContext Context)
    {
        bool PlayedSomething = false;
        Vector3 Position = ResolvePosition(Context);
        float Intensity = Context.Intensity * (FeedbackEvent != null ? FeedbackEvent.GetFeelIntensityMultiplier() : 1f);

        for (int BindingIndex = 0; BindingIndex < LocalBindings.Count; BindingIndex++)
        {
            LocalFeedbackBinding Binding = LocalBindings[BindingIndex];

            if (Binding == null || !string.Equals(Binding.GetEventId(), EventId, StringComparison.Ordinal))
            {
                continue;
            }

            IReadOnlyList<ParticleSystem> LocalParticleSystems = Binding.GetLocalParticleSystems();

            for (int ParticleIndex = 0; ParticleIndex < LocalParticleSystems.Count; ParticleIndex++)
            {
                ParticleSystem LocalParticleSystem = LocalParticleSystems[ParticleIndex];

                if (LocalParticleSystem == null)
                {
                    continue;
                }

                LocalParticleSystem.Play(true);
                PlayedSomething = true;
            }

            IReadOnlyList<GameObject> LocalVisualEffectObjects = Binding.GetLocalVisualEffectObjects();

            for (int VisualEffectIndex = 0; VisualEffectIndex < LocalVisualEffectObjects.Count; VisualEffectIndex++)
            {
                GameObject LocalVisualEffectObject = LocalVisualEffectObjects[VisualEffectIndex];

                if (LocalVisualEffectObject == null)
                {
                    continue;
                }

                Component[] VisualEffects = GetVisualEffectComponentsInHierarchy(LocalVisualEffectObject);

                if (VisualEffects.Length == 0)
                {
                    Log("Ignored local visual effect object because it does not contain a VisualEffect component: " + LocalVisualEffectObject.name);
                    continue;
                }

                PlayVisualEffects(VisualEffects, FeedbackEvent == null || FeedbackEvent.GetReinitializeVisualEffectsBeforePlay());
                ScheduleVisualEffectStop(VisualEffects, FeedbackEvent != null ? FeedbackEvent.GetVisualEffectStopDelay() : 0f);
                PlayedSomething = true;
            }

            IReadOnlyList<MMF_Player> FeelPlayers = Binding.GetFeelPlayers();

            for (int FeelIndex = 0; FeelIndex < FeelPlayers.Count; FeelIndex++)
            {
                MMF_Player FeelPlayer = FeelPlayers[FeelIndex];

                if (FeelPlayer == null)
                {
                    continue;
                }

                FeelPlayer.PlayFeedbacks(Position, Mathf.Max(0f, Intensity));
                PlayedSomething = true;
            }
        }

        return PlayedSomething;
    }

    /// <summary>
    /// Resolves the world position used by one feedback event.
    /// </summary>
    /// <param name="Context">Runtime feedback context.</param>
    /// <returns>World position for particles, audio and Feel.</returns>
    private Vector3 ResolvePosition(GameFeedbackContext Context)
    {
        if (Context.HasPosition)
        {
            return Context.Position;
        }

        Transform Root = GetFallbackRoot();
        return Root != null ? Root.position : transform.position;
    }

    /// <summary>
    /// Resolves the world rotation used by spawned particle prefabs.
    /// </summary>
    /// <param name="FeedbackEvent">Profile event containing alignment settings.</param>
    /// <param name="Context">Runtime feedback context.</param>
    /// <returns>World rotation for spawned particles.</returns>
    private Quaternion ResolveParticleRotation(GameFeedbackProfile.GameFeedbackEvent FeedbackEvent, GameFeedbackContext Context)
    {
        if (FeedbackEvent != null && FeedbackEvent.GetAlignParticlesToSurfaceNormal() && Context.HasNormal)
        {
            return Quaternion.FromToRotation(Vector3.up, Context.Normal.normalized);
        }

        Transform Root = GetFallbackRoot();
        return Root != null ? Root.rotation : transform.rotation;
    }

    /// <summary>
    /// Resolves the world rotation used by spawned Visual Effect Graph prefabs.
    /// </summary>
    /// <param name="FeedbackEvent">Profile event containing alignment settings.</param>
    /// <param name="Context">Runtime feedback context.</param>
    /// <returns>World rotation for spawned visual effects.</returns>
    private Quaternion ResolveVisualEffectRotation(GameFeedbackProfile.GameFeedbackEvent FeedbackEvent, GameFeedbackContext Context)
    {
        if (FeedbackEvent != null && FeedbackEvent.GetAlignVisualEffectsToSurfaceNormal() && Context.HasNormal)
        {
            return Quaternion.FromToRotation(Vector3.up, Context.Normal.normalized);
        }

        Transform Root = GetFallbackRoot();
        return Root != null ? Root.rotation : transform.rotation;
    }

    /// <summary>
    /// Resolves the world rotation used by spawned decal prefabs.
    /// </summary>
    /// <param name="FeedbackEvent">Profile event containing decal alignment settings.</param>
    /// <param name="Context">Runtime feedback context.</param>
    /// <returns>World rotation for spawned decals.</returns>
    private Quaternion ResolveDecalRotation(GameFeedbackProfile.GameFeedbackEvent FeedbackEvent, GameFeedbackContext Context)
    {
        Quaternion Rotation;

        if (FeedbackEvent != null && Context.HasNormal)
        {
            Vector3 Normal = Context.Normal.normalized;

            switch (FeedbackEvent.GetDecalAlignmentMode())
            {
                case GameFeedbackProfile.SurfaceAlignmentMode.ForwardToSurfaceNormal:
                    Rotation = Quaternion.LookRotation(Normal);
                    break;

                case GameFeedbackProfile.SurfaceAlignmentMode.ForwardIntoSurface:
                    Rotation = Quaternion.LookRotation(-Normal);
                    break;

                default:
                    Rotation = Quaternion.FromToRotation(Vector3.up, Normal);
                    break;
            }

            if (FeedbackEvent.GetRandomizeDecalRoll())
            {
                float RandomRoll = UnityEngine.Random.Range(FeedbackEvent.GetDecalRollMin(), FeedbackEvent.GetDecalRollMax());
                Rotation = Quaternion.AngleAxis(RandomRoll, Normal) * Rotation;
            }

            return Rotation;
        }

        Transform Root = GetFallbackRoot();
        return Root != null ? Root.rotation : transform.rotation;
    }

    /// <summary>
    /// Gets the fallback root used when context data is incomplete.
    /// </summary>
    /// <returns>Fallback transform.</returns>
    private Transform GetFallbackRoot()
    {
        return FallbackFeedbackRoot != null ? FallbackFeedbackRoot : transform;
    }

    /// <summary>
    /// Builds a precise diagnostic message for missing or disabled feedback requests.
    /// </summary>
    /// <param name="EventId">Requested event id.</param>
    /// <param name="ProfileStatus">Resolution status returned by the profile.</param>
    /// <returns>Readable diagnostic message.</returns>
    private string BuildMissingFeedbackMessage(string EventId, GameFeedbackProfile.EventResolutionStatus ProfileStatus)
    {
        if (FeedbackProfile == null)
        {
            return "No feedback profile assigned. Event id: " + EventId;
        }

        switch (ProfileStatus)
        {
            case GameFeedbackProfile.EventResolutionStatus.InvalidEventId:
                return "Ignored feedback request because the event id is empty.";
            case GameFeedbackProfile.EventResolutionStatus.DisabledProfileEvent:
                return "Feedback event exists but is disabled. Event id: " + EventId + " | Profile: " + FeedbackProfile.name;
            case GameFeedbackProfile.EventResolutionStatus.MissingProfileEvent:
                return "No profile event configured for event id: " + EventId + " | Profile: " + FeedbackProfile.name;
            case GameFeedbackProfile.EventResolutionStatus.EnabledProfileEvent:
                return "Feedback event resolved but nothing was played. Check empty prefab/audio/local binding lists. Event id: " + EventId + " | Profile: " + FeedbackProfile.name;
            default:
                return "No feedback configured for event id: " + EventId + " | Profile: " + FeedbackProfile.name;
        }
    }

    /// <summary>
    /// Logs feedback messages when enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[GameFeedbackEmitter] " + Message, this);
    }
}

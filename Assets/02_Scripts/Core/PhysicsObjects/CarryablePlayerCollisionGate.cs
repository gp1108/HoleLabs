using UnityEngine;

/// <summary>
/// Handles temporary collision and layer changes used while a carryable must ignore the player.
/// It also owns the short grace window used after releasing the object.
/// </summary>
[DisallowMultipleComponent]
public sealed class CarryablePlayerCollisionGate : MonoBehaviour
{
    [Header("Collision")]
    [Tooltip("Temporary layer applied while this carryable must ignore collisions against the player.")]
    [SerializeField] private string IgnoredByPlayerLayerName = "PlayerIgnoredPhysicsObjects";

    /// <summary>
    /// Returns true while player collision is currently ignored.
    /// </summary>
    public bool IsIgnoringPlayerCollision { get; private set; }

    /// <summary>
    /// Returns true while the post-release grace window is active.
    /// </summary>
    public bool IsReleaseGraceActive { get; private set; }

    /// <summary>
    /// Cached carryable colliders.
    /// </summary>
    private Collider[] CachedColliders;

    /// <summary>
    /// Cached transform hierarchy.
    /// </summary>
    private Transform[] CachedTransforms;

    /// <summary>
    /// Original layers for every transform in the hierarchy.
    /// </summary>
    private int[] CachedOriginalLayers;

    /// <summary>
    /// Resolved ignored layer.
    /// </summary>
    private int IgnoredByPlayerLayer = -1;

    /// <summary>
    /// Current player colliders being ignored.
    /// </summary>
    private Collider[] IgnoredPlayerColliders;

    /// <summary>
    /// Release grace minimum duration.
    /// </summary>
    private float ReleaseGraceMinimumTime;

    /// <summary>
    /// Release grace maximum duration.
    /// </summary>
    private float ReleaseGraceMaximumTime;

    /// <summary>
    /// Accumulated release grace time.
    /// </summary>
    private float ReleaseGraceTimer;

    /// <summary>
    /// If true, sleeping can end the grace state immediately.
    /// </summary>
    private bool RestoreOnSleep;

    /// <summary>
    /// Caches hierarchy references.
    /// </summary>
    private void Awake()
    {
        CachedColliders = GetComponentsInChildren<Collider>(true);
        CachedTransforms = GetComponentsInChildren<Transform>(true);
        CachedOriginalLayers = new int[CachedTransforms.Length];

        for (int Index = 0; Index < CachedTransforms.Length; Index++)
        {
            CachedOriginalLayers[Index] = CachedTransforms[Index].gameObject.layer;
        }

        IgnoredByPlayerLayer = LayerMask.NameToLayer(IgnoredByPlayerLayerName);
    }

    /// <summary>
    /// Starts fully ignoring the provided player colliders.
    /// </summary>
    /// <param name="PlayerColliders">Player colliders to ignore.</param>
    public void BeginIgnore(Collider[] PlayerColliders)
    {
        IgnoredPlayerColliders = PlayerColliders;
        IsReleaseGraceActive = false;
        ReleaseGraceTimer = 0f;

        SetIgnoreInternal(true);
    }

    /// <summary>
    /// Starts a short post-release grace window while still ignoring the player.
    /// </summary>
    /// <param name="PlayerColliders">Player colliders to ignore.</param>
    /// <param name="MinimumTime">Minimum grace duration.</param>
    /// <param name="MaximumTime">Maximum grace duration.</param>
    /// <param name="AllowRestoreOnSleep">True if sleeping can immediately restore normal collisions.</param>
    public void BeginReleaseGrace(Collider[] PlayerColliders, float MinimumTime, float MaximumTime, bool AllowRestoreOnSleep)
    {
        IgnoredPlayerColliders = PlayerColliders;
        ReleaseGraceMinimumTime = MinimumTime;
        ReleaseGraceMaximumTime = MaximumTime;
        RestoreOnSleep = AllowRestoreOnSleep;
        ReleaseGraceTimer = 0f;
        IsReleaseGraceActive = true;

        SetIgnoreInternal(true);
    }

    /// <summary>
    /// Updates the release grace window and restores collisions when safe.
    /// </summary>
    /// <param name="IsSleeping">True if the carryable rigidbody is currently sleeping.</param>
    public void Tick(bool IsSleeping)
    {
        if (!IsReleaseGraceActive)
        {
            return;
        }

        ReleaseGraceTimer += Time.fixedDeltaTime;

        bool HasMetMinimumTime = ReleaseGraceTimer >= ReleaseGraceMinimumTime;
        bool HasReachedMaximumTime = ReleaseGraceTimer >= ReleaseGraceMaximumTime;
        bool CanRestoreOnSleep = RestoreOnSleep && IsSleeping;
        bool IsStillOverlappingPlayer = IsOverlappingIgnoredPlayerColliders();

        if ((HasMetMinimumTime && !IsStillOverlappingPlayer) || CanRestoreOnSleep || HasReachedMaximumTime)
        {
            EndIgnore();
        }
    }

    /// <summary>
    /// Restores normal collision and original hierarchy layers immediately.
    /// </summary>
    public void EndIgnore()
    {
        IsReleaseGraceActive = false;
        ReleaseGraceTimer = 0f;

        SetIgnoreInternal(false);
        IgnoredPlayerColliders = null;
    }

    /// <summary>
    /// Applies or restores ignore collision and hierarchy layers.
    /// </summary>
    /// <param name="Ignore">True to ignore, false to restore.</param>
    private void SetIgnoreInternal(bool Ignore)
    {
        IsIgnoringPlayerCollision = Ignore;

        if (CachedColliders != null && IgnoredPlayerColliders != null)
        {
            for (int CarryableColliderIndex = 0; CarryableColliderIndex < CachedColliders.Length; CarryableColliderIndex++)
            {
                Collider CarryableCollider = CachedColliders[CarryableColliderIndex];
                if (CarryableCollider == null)
                {
                    continue;
                }

                for (int PlayerColliderIndex = 0; PlayerColliderIndex < IgnoredPlayerColliders.Length; PlayerColliderIndex++)
                {
                    Collider PlayerCollider = IgnoredPlayerColliders[PlayerColliderIndex];
                    if (PlayerCollider == null)
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(CarryableCollider, PlayerCollider, Ignore);
                }
            }
        }

        if (Ignore && IgnoredByPlayerLayer >= 0)
        {
            for (int TransformIndex = 0; TransformIndex < CachedTransforms.Length; TransformIndex++)
            {
                Transform CurrentTransform = CachedTransforms[TransformIndex];
                if (CurrentTransform == null)
                {
                    continue;
                }

                CurrentTransform.gameObject.layer = IgnoredByPlayerLayer;
            }

            return;
        }

        for (int TransformIndex = 0; TransformIndex < CachedTransforms.Length; TransformIndex++)
        {
            Transform CurrentTransform = CachedTransforms[TransformIndex];
            if (CurrentTransform == null)
            {
                continue;
            }

            CurrentTransform.gameObject.layer = CachedOriginalLayers[TransformIndex];
        }
    }

    /// <summary>
    /// Returns true if any carryable collider still overlaps any ignored player collider.
    /// </summary>
    /// <returns>True when overlap still exists.</returns>
    private bool IsOverlappingIgnoredPlayerColliders()
    {
        if (IgnoredPlayerColliders == null || CachedColliders == null)
        {
            return false;
        }

        for (int CarryableColliderIndex = 0; CarryableColliderIndex < CachedColliders.Length; CarryableColliderIndex++)
        {
            Collider CarryableCollider = CachedColliders[CarryableColliderIndex];
            if (CarryableCollider == null || !CarryableCollider.enabled)
            {
                continue;
            }

            for (int PlayerColliderIndex = 0; PlayerColliderIndex < IgnoredPlayerColliders.Length; PlayerColliderIndex++)
            {
                Collider PlayerCollider = IgnoredPlayerColliders[PlayerColliderIndex];
                if (PlayerCollider == null || !PlayerCollider.enabled)
                {
                    continue;
                }

                bool IsOverlapping = Physics.ComputePenetration(
                    CarryableCollider,
                    CarryableCollider.transform.position,
                    CarryableCollider.transform.rotation,
                    PlayerCollider,
                    PlayerCollider.transform.position,
                    PlayerCollider.transform.rotation,
                    out _,
                    out _);

                if (IsOverlapping)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
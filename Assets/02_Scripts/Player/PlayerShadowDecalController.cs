using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Controls a URP Decal Projector used as a fake player contact shadow.
/// The decal changes size and opacity depending on grounded, crouching and airborne states.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DecalProjector))]
public sealed class PlayerShadowDecalController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Player controller used as the authoritative source for crouch and grounded state.")]
    [SerializeField] private PlayerController PlayerController;

    [Tooltip("URP decal projector used to render the player fake shadow.")]
    [SerializeField] private DecalProjector DecalProjector;

    [Header("Standing Shadow")]
    [Tooltip("Decal width used while the player is standing on the ground.")]
    [SerializeField] private float StandingWidth = 1.15f;

    [Tooltip("Decal height used while the player is standing on the ground.")]
    [SerializeField] private float StandingHeight = 1.15f;

    [Tooltip("Decal opacity used while the player is standing on the ground.")]
    [Range(0f, 1f)]
    [SerializeField] private float StandingOpacity = 0.65f;

    [Header("Crouching Shadow")]
    [Tooltip("Decal width used while the player is crouching on the ground.")]
    [SerializeField] private float CrouchingWidth = 1.45f;

    [Tooltip("Decal height used while the player is crouching on the ground.")]
    [SerializeField] private float CrouchingHeight = 1.35f;

    [Tooltip("Decal opacity used while the player is crouching on the ground.")]
    [Range(0f, 1f)]
    [SerializeField] private float CrouchingOpacity = 0.75f;

    [Header("Airborne Shadow")]
    [Tooltip("Decal width used while the player is airborne.")]
    [SerializeField] private float AirborneWidth = 0.55f;

    [Tooltip("Decal height used while the player is airborne.")]
    [SerializeField] private float AirborneHeight = 0.55f;

    [Tooltip("Decal opacity used while the player is airborne.")]
    [Range(0f, 1f)]
    [SerializeField] private float AirborneOpacity = 0.35f;

    [Header("Projection")]
    [Tooltip("Projection depth used by the decal projector.")]
    [SerializeField] private float ProjectionDepth = 0.6f;

    [Tooltip("Local position used to keep the decal centered at the player's feet.")]
    [SerializeField] private Vector3 LocalFeetOffset = Vector3.zero;

    [Header("Smoothing")]
    [Tooltip("Speed used to interpolate decal size changes.")]
    [SerializeField] private float SizeInterpolationSpeed = 12f;

    [Tooltip("Speed used to interpolate decal opacity changes.")]
    [SerializeField] private float OpacityInterpolationSpeed = 14f;

    /// <summary>
    /// Current smoothed decal width.
    /// </summary>
    private float CurrentWidth;

    /// <summary>
    /// Current smoothed decal height.
    /// </summary>
    private float CurrentHeight;

    /// <summary>
    /// Current smoothed decal opacity.
    /// </summary>
    private float CurrentOpacity;

    /// <summary>
    /// Resolves required references and initializes decal values.
    /// </summary>
    private void Awake()
    {
        if (DecalProjector == null)
        {
            DecalProjector = GetComponent<DecalProjector>();
        }

        if (PlayerController == null)
        {
            PlayerController = GetComponentInParent<PlayerController>();
        }

        CurrentWidth = StandingWidth;
        CurrentHeight = StandingHeight;
        CurrentOpacity = StandingOpacity;

        ApplyDecalValues(CurrentWidth, CurrentHeight, CurrentOpacity);
    }

    /// <summary>
    /// Updates decal size and opacity after the player controller has processed movement.
    /// </summary>
    private void LateUpdate()
    {
        if (DecalProjector == null || PlayerController == null)
        {
            return;
        }

        transform.localPosition = LocalFeetOffset;

        float TargetWidth;
        float TargetHeight;
        float TargetOpacity;

        ResolveTargetValues(out TargetWidth, out TargetHeight, out TargetOpacity);

        CurrentWidth = Mathf.MoveTowards(CurrentWidth, TargetWidth, SizeInterpolationSpeed * Time.deltaTime);
        CurrentHeight = Mathf.MoveTowards(CurrentHeight, TargetHeight, SizeInterpolationSpeed * Time.deltaTime);
        CurrentOpacity = Mathf.MoveTowards(CurrentOpacity, TargetOpacity, OpacityInterpolationSpeed * Time.deltaTime);

        ApplyDecalValues(CurrentWidth, CurrentHeight, CurrentOpacity);
    }

    /// <summary>
    /// Selects the target decal values from the current player locomotion state.
    /// </summary>
    /// <param name="TargetWidth">Resolved target decal width.</param>
    /// <param name="TargetHeight">Resolved target decal height.</param>
    /// <param name="TargetOpacity">Resolved target decal opacity.</param>
    private void ResolveTargetValues(out float TargetWidth, out float TargetHeight, out float TargetOpacity)
    {
        if (!PlayerController.IsGrounded)
        {
            TargetWidth = AirborneWidth;
            TargetHeight = AirborneHeight;
            TargetOpacity = AirborneOpacity;
            return;
        }

        if (PlayerController.IsCrouching)
        {
            TargetWidth = CrouchingWidth;
            TargetHeight = CrouchingHeight;
            TargetOpacity = CrouchingOpacity;
            return;
        }

        TargetWidth = StandingWidth;
        TargetHeight = StandingHeight;
        TargetOpacity = StandingOpacity;
    }

    /// <summary>
    /// Applies size, projection depth and opacity to the decal projector.
    /// </summary>
    /// <param name="Width">Target decal width.</param>
    /// <param name="Height">Target decal height.</param>
    /// <param name="Opacity">Target decal opacity.</param>
    private void ApplyDecalValues(float Width, float Height, float Opacity)
    {
        Vector3 CurrentSize = DecalProjector.size;
        CurrentSize.x = Mathf.Max(0.01f, Width);
        CurrentSize.y = Mathf.Max(0.01f, Height);
        CurrentSize.z = Mathf.Max(0.01f, ProjectionDepth);

        DecalProjector.size = CurrentSize;
        DecalProjector.fadeFactor = Mathf.Clamp01(Opacity);
    }
}
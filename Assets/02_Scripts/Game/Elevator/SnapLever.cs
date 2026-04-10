using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Generic physical snap lever that supports drag interaction and fixed snap positions.
/// The lever owns only angle evaluation, local rotation and snapped state.
/// </summary>
[DisallowMultipleComponent]
public sealed class SnapLever : MonoBehaviour
{
    /// <summary>
    /// Defines which local axis the lever rotates around.
    /// </summary>
    public enum LeverAxis
    {
        LocalX,
        LocalY,
        LocalZ
    }

    /// <summary>
    /// Defines which mouse delta is used while dragging.
    /// </summary>
    public enum DragInputMode
    {
        VerticalMouse,
        HorizontalMouse
    }

    /// <summary>
    /// Invoked when the snapped state changes.
    /// </summary>
    [Serializable]
    public sealed class LeverSnapChangedEvent : UnityEvent<int>
    {
    }

    [Header("References")]
    [Tooltip("Optional pivot used as the visual rotating part of the lever. If null, this transform is used.")]
    [SerializeField] private Transform LeverPivot;

    [Tooltip("Preferred collider used for interaction checks.")]
    [SerializeField] private Collider InteractionCollider;

    [Header("Rotation")]
    [Tooltip("Local axis used by the lever rotation.")]
    [SerializeField] private LeverAxis RotationAxis = LeverAxis.LocalX;

    [Tooltip("Mouse delta source used to drag the lever.")]
    [SerializeField] private DragInputMode DragMode = DragInputMode.VerticalMouse;

    [Tooltip("Degrees applied per mouse input unit while dragging.")]
    [SerializeField] private float DragSensitivity = 160f;

    [Tooltip("Interpolation speed used when returning to the snapped target.")]
    [SerializeField] private float SnapSpeed = 720f;

    [Tooltip("Minimum local lever angle.")]
    [SerializeField] private float MinAngle = -35f;

    [Tooltip("Maximum local lever angle.")]
    [SerializeField] private float MaxAngle = 35f;

    [Header("Interaction")]
    [Tooltip("Maximum interaction distance allowed for this lever.")]
    [SerializeField] private float InteractionDistance = 3f;

    [Tooltip("Extra world radius used to help off-center interaction.")]
    [SerializeField] private float InteractionRadius = 0.35f;

    [Header("Snaps")]
    [Tooltip("Ordered local snap angles in degrees.")]
    [SerializeField] private float[] SnapAngles = new float[] { -25f, 0f, 25f };

    [Tooltip("If true, the lever drag direction is inverted.")]
    [SerializeField] private bool InvertDragInput = false;

    [Header("Events")]
    [Tooltip("Invoked whenever the snapped index changes.")]
    [SerializeField] private LeverSnapChangedEvent OnSnapChanged = new LeverSnapChangedEvent();

    [Header("Debug")]
    [Tooltip("Logs runtime state changes.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Current snapped index.
    /// </summary>
    public int CurrentSnapIndex { get; private set; }

    /// <summary>
    /// Whether this lever is currently locked by an external gameplay condition.
    /// </summary>
    private bool IsExternallyLocked;

    /// <summary>
    /// Snap index enforced while the lever is externally locked.
    /// </summary>
    private int LockedSnapIndex;

    /// <summary>
    /// Current angle used by the lever.
    /// </summary>
    private float CurrentAngle;

    /// <summary>
    /// Current snapped target angle.
    /// </summary>
    private float TargetAngle;

    /// <summary>
    /// Whether an external system is currently dragging the lever.
    /// </summary>
    private bool IsBeingDragged;

    /// <summary>
    /// Initializes cached references and the snapped pose.
    /// </summary>
    private void Awake()
    {
        if (LeverPivot == null)
        {
            LeverPivot = transform;
        }

        if (InteractionCollider == null)
        {
            InteractionCollider = GetComponentInChildren<Collider>();
        }

        SanitizeSnapAngles();

        CurrentSnapIndex = GetClosestSnapIndex(0f);
        TargetAngle = SnapAngles[CurrentSnapIndex];
        CurrentAngle = TargetAngle;

        ApplyAngle(CurrentAngle);
    }

    /// <summary>
    /// Validates serialized values in the editor.
    /// </summary>
    private void OnValidate()
    {
        DragSensitivity = Mathf.Max(0f, DragSensitivity);
        SnapSpeed = Mathf.Max(0f, SnapSpeed);
        InteractionDistance = Mathf.Max(0.1f, InteractionDistance);
        InteractionRadius = Mathf.Max(0f, InteractionRadius);

        if (MaxAngle < MinAngle)
        {
            MaxAngle = MinAngle;
        }

        SanitizeSnapAngles();
    }

    /// <summary>
    /// Keeps the lever fixed on the current snapped target when not dragging.
    /// </summary>
    private void Update()
    {
        if (IsExternallyLocked)
        {
            TargetAngle = SnapAngles[LockedSnapIndex];
            CurrentAngle = Mathf.MoveTowards(CurrentAngle, TargetAngle, SnapSpeed * Time.deltaTime);
            ApplyAngle(CurrentAngle);
            return;
        }

        if (IsBeingDragged)
        {
            return;
        }

        CurrentAngle = Mathf.MoveTowards(CurrentAngle, TargetAngle, SnapSpeed * Time.deltaTime);
        ApplyAngle(CurrentAngle);
    }

    /// <summary>
    /// Returns whether the lever is currently locked by an external gameplay condition.
    /// </summary>
    public bool GetIsExternallyLocked()
    {
        return IsExternallyLocked;
    }

    /// <summary>
    /// Returns the interaction distance used by external interaction systems.
    /// </summary>
    public float GetInteractionDistance()
    {
        return InteractionDistance;
    }

    /// <summary>
    /// Returns the interaction support radius used by external interaction systems.
    /// </summary>
    public float GetInteractionRadius()
    {
        return InteractionRadius;
    }

    /// <summary>
    /// Returns the preferred collider for interaction checks.
    /// </summary>
    public Collider GetInteractionCollider()
    {
        return InteractionCollider;
    }

    /// <summary>
    /// Locks the lever to a specific snap index and prevents drag interaction until unlocked.
    /// </summary>
    /// <param name="IsLocked">True to lock the lever, false to unlock it.</param>
    /// <param name="SnapIndex">Snap index enforced while locked.</param>
    public void SetExternalLock(bool IsLocked, int SnapIndex)
    {
        IsExternallyLocked = IsLocked;
        LockedSnapIndex = Mathf.Clamp(SnapIndex, 0, SnapAngles.Length - 1);

        if (IsExternallyLocked)
        {
            IsBeingDragged = false;
            SetSnapIndexWithoutNotify(LockedSnapIndex);
        }
    }

    /// <summary>
    /// Starts drag control.
    /// </summary>
    public void BeginDrag()
    {
        if (IsExternallyLocked)
        {
            return;
        }

        IsBeingDragged = true;
    }

    /// <summary>
    /// Ends drag control and snaps to the closest valid state.
    /// </summary>
    public void EndDrag()
    {
        if (!IsBeingDragged)
        {
            return;
        }

        IsBeingDragged = false;
        SnapToClosestState(true);
    }

    /// <summary>
    /// Processes look input while dragging the lever.
    /// While dragging, the lever also updates its active snap state in real time
    /// as soon as the current angle becomes closer to another snap.
    /// </summary>
    /// <param name="LookDelta">Player look delta.</param>
    public void ProcessDrag(Vector2 LookDelta)
    {
        if (!IsBeingDragged || IsExternallyLocked)
        {
            return;
        }

        float InputValue = DragMode == DragInputMode.VerticalMouse ? -LookDelta.y : LookDelta.x;

        if (InvertDragInput)
        {
            InputValue *= -1f;
        }

        float DeltaAngle = InputValue * DragSensitivity * Time.deltaTime;

        CurrentAngle = Mathf.Clamp(CurrentAngle + DeltaAngle, MinAngle, MaxAngle);
        ApplyAngle(CurrentAngle);

        int ClosestIndex = GetClosestSnapIndex(CurrentAngle);

        if (ClosestIndex != CurrentSnapIndex)
        {
            CurrentSnapIndex = ClosestIndex;
            TargetAngle = SnapAngles[ClosestIndex];

            if (DebugLogs)
            {
                Debug.Log("[SnapLever] Drag changed active snap to index " + CurrentSnapIndex, this);
            }

            OnSnapChanged.Invoke(CurrentSnapIndex);
        }
    }

    /// <summary>
    /// Forces the lever to a given snap index immediately and invokes the change event.
    /// </summary>
    /// <param name="SnapIndex">Target snap index.</param>
    public void SetSnapIndexImmediate(int SnapIndex)
    {
        SetSnapIndexInternal(SnapIndex, true);
    }

    /// <summary>
    /// Forces the lever to a given snap index immediately without invoking the change event.
    /// Use this for external corrections to avoid recursive event loops.
    /// </summary>
    /// <param name="SnapIndex">Target snap index.</param>
    public void SetSnapIndexWithoutNotify(int SnapIndex)
    {
        SetSnapIndexInternal(SnapIndex, false);
    }

    /// <summary>
    /// Applies a snap index immediately.
    /// </summary>
    /// <param name="SnapIndex">Target snap index.</param>
    /// <param name="InvokeEvent">Whether the change event should be invoked.</param>
    private void SetSnapIndexInternal(int SnapIndex, bool InvokeEvent)
    {
        SanitizeSnapAngles();

        int ClampedIndex = Mathf.Clamp(SnapIndex, 0, SnapAngles.Length - 1);
        CurrentSnapIndex = ClampedIndex;
        TargetAngle = SnapAngles[ClampedIndex];
        CurrentAngle = TargetAngle;

        ApplyAngle(CurrentAngle);

        if (InvokeEvent)
        {
            OnSnapChanged.Invoke(CurrentSnapIndex);
        }
    }

    /// <summary>
    /// Snaps the lever to the closest configured state.
    /// </summary>
    /// <param name="InvokeEvent">Whether to notify the state change.</param>
    private void SnapToClosestState(bool InvokeEvent)
    {
        int ClosestIndex = GetClosestSnapIndex(CurrentAngle);
        bool HasChanged = ClosestIndex != CurrentSnapIndex;

        CurrentSnapIndex = ClosestIndex;
        TargetAngle = SnapAngles[ClosestIndex];
        CurrentAngle = TargetAngle;

        ApplyAngle(CurrentAngle);

        if (InvokeEvent && HasChanged)
        {
            if (DebugLogs)
            {
                Debug.Log("[SnapLever] Snap changed to index " + CurrentSnapIndex, this);
            }

            OnSnapChanged.Invoke(CurrentSnapIndex);
        }
    }

    /// <summary>
    /// Returns the closest snap index for the provided angle.
    /// </summary>
    /// <param name="Angle">Angle to evaluate.</param>
    /// <returns>Closest snap index.</returns>
    private int GetClosestSnapIndex(float Angle)
    {
        int BestIndex = 0;
        float BestDistance = Mathf.Abs(Angle - SnapAngles[0]);

        for (int Index = 1; Index < SnapAngles.Length; Index++)
        {
            float Distance = Mathf.Abs(Angle - SnapAngles[Index]);
            if (Distance < BestDistance)
            {
                BestDistance = Distance;
                BestIndex = Index;
            }
        }

        return BestIndex;
    }

    /// <summary>
    /// Applies the provided angle to the selected local axis.
    /// </summary>
    /// <param name="Angle">Local lever angle.</param>
    private void ApplyAngle(float Angle)
    {
        Vector3 Euler = LeverPivot.localEulerAngles;

        Euler = new Vector3(
            NormalizeAngle(Euler.x),
            NormalizeAngle(Euler.y),
            NormalizeAngle(Euler.z));

        switch (RotationAxis)
        {
            case LeverAxis.LocalX:
                Euler.x = Angle;
                break;

            case LeverAxis.LocalY:
                Euler.y = Angle;
                break;

            case LeverAxis.LocalZ:
                Euler.z = Angle;
                break;
        }

        LeverPivot.localRotation = Quaternion.Euler(Euler);
    }

    /// <summary>
    /// Ensures snap angles are valid and clamped to the allowed range.
    /// </summary>
    private void SanitizeSnapAngles()
    {
        if (SnapAngles == null || SnapAngles.Length == 0)
        {
            SnapAngles = new float[] { 0f };
        }

        for (int Index = 0; Index < SnapAngles.Length; Index++)
        {
            SnapAngles[Index] = Mathf.Clamp(SnapAngles[Index], MinAngle, MaxAngle);
        }

        Array.Sort(SnapAngles);
    }

    /// <summary>
    /// Normalizes an angle to the [-180, 180] range.
    /// </summary>
    private static float NormalizeAngle(float Angle)
    {
        while (Angle > 180f) Angle -= 360f;
        while (Angle < -180f) Angle += 360f;
        return Angle;
    }
}
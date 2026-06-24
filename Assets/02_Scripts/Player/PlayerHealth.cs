using System;
using UnityEngine;

/// <summary>
/// Runtime health container for the player. It is intentionally independent from movement so hazards,
/// fall damage and future combat can all damage the player through the same stable API.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    [Tooltip("Maximum player health value restored by full heals and respawns.")]
    [SerializeField] private float MaxHealth = 100f;

    [Tooltip("Current player health value. This is clamped between zero and Max Health at runtime.")]
    [SerializeField] private float CurrentHealth = 100f;

    [Tooltip("If true, incoming damage is ignored. Useful for testing recovery flows without disabling the component.")]
    [SerializeField] private bool IsInvulnerable = false;

    [Header("Death")]
    [Tooltip("If true, health is restored to full when ReviveFull is called by recovery systems.")]
    [SerializeField] private bool RestoreFullHealthOnRevive = true;

    [Header("Debug")]
    [Tooltip("Logs damage, healing and death events.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Raised when the player reaches zero health for the first time in the current life.
    /// </summary>
    public event Action<PlayerHealth> OnDied;

    /// <summary>
    /// Gets the current health value.
    /// </summary>
    public float Health => CurrentHealth;

    /// <summary>
    /// Gets the maximum health value.
    /// </summary>
    public float HealthMax => MaxHealth;

    /// <summary>
    /// Gets whether the player is currently dead and waiting for recovery.
    /// </summary>
    public bool IsDead { get; private set; }

    /// <summary>
    /// Initializes health values safely.
    /// </summary>
    private void Awake()
    {
        MaxHealth = Mathf.Max(1f, MaxHealth);
        CurrentHealth = Mathf.Clamp(CurrentHealth, 0f, MaxHealth);
        IsDead = CurrentHealth <= 0f;
    }

    /// <summary>
    /// Applies damage to the player and triggers death when health reaches zero.
    /// </summary>
    /// <param name="DamageAmount">Positive damage amount.</param>
    /// <param name="DamageSource">Optional source object for debug messages.</param>
    public void ApplyDamage(float DamageAmount, UnityEngine.Object DamageSource = null)
    {
        if (IsInvulnerable || IsDead || DamageAmount <= 0f)
        {
            return;
        }

        CurrentHealth = Mathf.Clamp(CurrentHealth - DamageAmount, 0f, MaxHealth);
        Log("Damage: " + DamageAmount.ToString("0.##") + " | Health: " + CurrentHealth.ToString("0.##"), DamageSource);

        if (CurrentHealth <= 0f)
        {
            Kill(DamageSource);
        }
    }

    /// <summary>
    /// Heals the player without exceeding Max Health.
    /// </summary>
    /// <param name="HealAmount">Positive health amount to restore.</param>
    public void Heal(float HealAmount)
    {
        if (HealAmount <= 0f)
        {
            return;
        }

        MaxHealth = Mathf.Max(1f, MaxHealth);
        CurrentHealth = Mathf.Clamp(CurrentHealth + HealAmount, 0f, MaxHealth);
        Log("Heal: " + HealAmount.ToString("0.##") + " | Health: " + CurrentHealth.ToString("0.##"), this);
    }

    /// <summary>
    /// Immediately kills the player, bypassing the current health value.
    /// </summary>
    /// <param name="DeathSource">Optional source object for debug messages.</param>
    public void Kill(UnityEngine.Object DeathSource = null)
    {
        if (IsDead)
        {
            return;
        }

        CurrentHealth = 0f;
        IsDead = true;
        Log("Player died.", DeathSource);
        OnDied?.Invoke(this);
    }

    /// <summary>
    /// Revives the player after a recovery system has repositioned them.
    /// </summary>
    public void ReviveFull()
    {
        IsDead = false;

        if (RestoreFullHealthOnRevive)
        {
            CurrentHealth = Mathf.Max(1f, MaxHealth);
        }
        else
        {
            CurrentHealth = Mathf.Max(1f, CurrentHealth);
        }

        Log("Player revived. Health: " + CurrentHealth.ToString("0.##"), this);
    }

    /// <summary>
    /// Restores health state from external systems.
    /// </summary>
    /// <param name="HealthValue">Current health value to apply.</param>
    /// <param name="IsDeadValue">Whether the player should be considered dead.</param>
    public void ApplySavedState(float HealthValue, bool IsDeadValue)
    {
        MaxHealth = Mathf.Max(1f, MaxHealth);
        CurrentHealth = Mathf.Clamp(HealthValue, 0f, MaxHealth);
        IsDead = IsDeadValue || CurrentHealth <= 0f;
    }

    /// <summary>
    /// Logs a health message when enabled.
    /// </summary>
    /// <param name="Message">Message body.</param>
    /// <param name="Context">Optional Unity context.</param>
    private void Log(string Message, UnityEngine.Object Context)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[PlayerHealth] " + Message, Context != null ? Context : this);
    }
}

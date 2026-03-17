using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Handles the visual state of a single hotbar slot.
/// This script is intentionally simple so the global hotbar UI controller can drive it.
/// </summary>
public sealed class HotbarSlotUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Image used as the slot background.")]
    [SerializeField] private Image BackgroundImage;

    [Tooltip("Image used to display the item icon.")]
    [SerializeField] private Image IconImage;

    [Tooltip("Text used to show the item stack amount.")]
    [SerializeField] private TMP_Text AmountText;

    [Tooltip("Optional text used to display the slot shortcut number.")]
    [SerializeField] private TMP_Text SlotIndexText;

    [Header("Colors")]
    [Tooltip("Background color applied when the slot is not selected.")]
    [SerializeField] private Color NormalBackgroundColor = new Color(1f, 1f, 1f, 0.2f);

    [Tooltip("Background color applied when the slot is selected.")]
    [SerializeField] private Color SelectedBackgroundColor = new Color(1f, 0.85f, 0.25f, 0.95f);

    [Tooltip("Icon tint applied when the slot contains an item.")]
    [SerializeField] private Color FilledIconColor = Color.white;

    [Tooltip("Icon tint applied when the slot is empty.")]
    [SerializeField] private Color EmptyIconColor = new Color(1f, 1f, 1f, 0f);

    /// <summary>
    /// Sets the shortcut label displayed by this slot.
    /// </summary>
    public void SetSlotIndexLabel(int slotNumber)
    {
        if (SlotIndexText == null)
        {
            return;
        }

        SlotIndexText.text = slotNumber.ToString();
    }

    /// <summary>
    /// Applies visual data for the provided item instance.
    /// </summary>
    public void SetItem(ItemInstance itemInstance)
    {
        bool hasItem = itemInstance != null && itemInstance.GetDefinition() != null;

        if (IconImage != null)
        {
            IconImage.sprite = hasItem ? itemInstance.GetDefinition().GetIcon() : null;
            IconImage.color = hasItem ? FilledIconColor : EmptyIconColor;
            IconImage.enabled = hasItem;
        }

        if (AmountText != null)
        {
            if (!hasItem)
            {
                AmountText.text = string.Empty;
                return;
            }

            int amount = itemInstance.GetAmount();
            AmountText.text = amount > 1 ? amount.ToString() : string.Empty;
        }
    }

    /// <summary>
    /// Applies the visual selected or unselected state of this slot.
    /// </summary>
    public void SetSelected(bool isSelected)
    {
        if (BackgroundImage == null)
        {
            return;
        }

        BackgroundImage.color = isSelected ? SelectedBackgroundColor : NormalBackgroundColor;
    }
}

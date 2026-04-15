using TMPro;
using UnityEngine;

/// <summary>
/// Controls the scanner world-space UI using separate panels for ore and vein targets.
/// It also owns the shared status text shown independently from the data panels.
/// </summary>
public sealed class ScannerDisplayUI : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("Optional root object used to enable or disable the full scanner UI.")]
    [SerializeField] private GameObject ScreenRoot;

    [Header("Panels")]
    [Tooltip("Panel used to display scanned dropped ore information.")]
    [SerializeField] private GameObject OrePanelInfo;

    [Tooltip("Panel used to display scanned vein information.")]
    [SerializeField] private GameObject VeinPanelInfo;

    [Tooltip("Optional container used for the global status area.")]
    [SerializeField] private GameObject StatusInfoRoot;

    [Header("Ore Panel Fields")]
    [Tooltip("Text used to display the dropped ore mineral type.")]
    [SerializeField] private TMP_Text OreMineralTypeText;

    [Tooltip("Text used to display the dropped ore purity.")]
    [SerializeField] private TMP_Text OrePurityText;

    [Tooltip("Text used to display the dropped ore size.")]
    [SerializeField] private TMP_Text OreSizeText;

    [Tooltip("Text used to display the dropped ore weight.")]
    [SerializeField] private TMP_Text OreWeightText;

    [Tooltip("Text used to display the dropped ore gold value.")]
    [SerializeField] private TMP_Text OrePriceGoldText;

    [Tooltip("Text used to display the dropped ore research value.")]
    [SerializeField] private TMP_Text OrePriceResearchText;

    [Tooltip("Optional status text placed inside the ore panel.")]
    [SerializeField] private TMP_Text OrePanelStatusText;

    [Header("Vein Panel Fields")]
    [Tooltip("Text used to display the vein mineral type.")]
    [SerializeField] private TMP_Text VeinMineralTypeText;

    [Tooltip("Text used to display the vein drop amount range.")]
    [SerializeField] private TMP_Text VeinDropAmountText;

    [Header("Shared Status")]
    [Tooltip("Main shared status text shown in the separate status area.")]
    [SerializeField] private TMP_Text SharedStatusText;

    /// <summary>
    /// Shows or hides the whole scanner screen.
    /// </summary>
    public void SetVisible(bool IsVisible)
    {
        if (ScreenRoot != null)
        {
            ScreenRoot.SetActive(IsVisible);
            return;
        }

        gameObject.SetActive(IsVisible);
    }

    /// <summary>
    /// Resets the scanner UI to its idle state.
    /// </summary>
    public void ShowIdle()
    {
        SetOrePanelVisible(false);
        SetVeinPanelVisible(false);
        SetStatus("No Target");
        SetOrePanelStatus("No Target");
        ClearOreFields();
        ClearVeinFields();
    }

    /// <summary>
    /// Displays shared scanning progress for the current target.
    /// </summary>
    public void ShowScanning(string TargetLabel, float NormalizedProgress)
    {
        int Percentage = Mathf.RoundToInt(Mathf.Clamp01(NormalizedProgress) * 100f);
        string StatusMessage = "Scanning " + TargetLabel + "... " + Percentage + "%";

        SetStatus(StatusMessage);
        SetOrePanelStatus(StatusMessage);
    }

    /// <summary>
    /// Displays final scan data for a vein and hides the ore panel.
    /// </summary>
    public void ShowVeinResult(string MineralType, bool ShowDropRange, int MinDropCount, int MaxDropCount)
    {
        SetOrePanelVisible(false);
        SetVeinPanelVisible(true);

        SetText(VeinMineralTypeText, "Mineral Type: " + MineralType);
        SetText(
            VeinDropAmountText,
            ShowDropRange
                ? "Drop Amount: " + MinDropCount + " - " + MaxDropCount
                : "Drop Amount: Locked");

        SetStatus("Scan Complete");
        SetOrePanelStatus(string.Empty);
        ClearOreFields();
    }

    /// <summary>
    /// Displays final scan data for a dropped ore and hides the vein panel.
    /// </summary>
    public void ShowOreResult(
        string MineralType,
        bool ShowGoldValue,
        float GoldValue,
        bool ShowResearchValue,
        float ResearchValue,
        bool ShowPurity,
        float Purity,
        bool ShowSize,
        float Size,
        bool ShowWeight,
        float Weight)
    {
        SetOrePanelVisible(true);
        SetVeinPanelVisible(false);

        SetText(OreMineralTypeText, "Mineral Type: " + MineralType);
        SetText(OrePriceGoldText, ShowGoldValue ? "Price Gold: " + GoldValue.ToString("0.00") : "Price Gold: Locked");
        SetText(OrePriceResearchText, ShowResearchValue ? "Price Research: " + ResearchValue.ToString("0.00") : "Price Research: Locked");
        SetText(OrePurityText, ShowPurity ? "Purity: " + Purity.ToString("0.00") : "Purity: Locked");
        SetText(OreSizeText, ShowSize ? "Size: " + Size.ToString("0.00") : "Size: Locked");
        SetText(OreWeightText, ShowWeight ? "Weight: " + Weight.ToString("0.00") : "Weight: Locked");

        SetStatus("Scan Complete");
        SetOrePanelStatus("Scan Complete");
        ClearVeinFields();
    }

    /// <summary>
    /// Clears all ore panel fields.
    /// </summary>
    private void ClearOreFields()
    {
        SetText(OreMineralTypeText, "Mineral Type: -");
        SetText(OrePurityText, "Purity: -");
        SetText(OreSizeText, "Size: -");
        SetText(OreWeightText, "Weight: -");
        SetText(OrePriceGoldText, "Price Gold: -");
        SetText(OrePriceResearchText, "Price Research: -");
    }

    /// <summary>
    /// Clears all vein panel fields.
    /// </summary>
    private void ClearVeinFields()
    {
        SetText(VeinMineralTypeText, "Mineral Type: -");
        SetText(VeinDropAmountText, "Drop Amount: -");
    }

    /// <summary>
    /// Sets whether the ore panel is visible.
    /// </summary>
    private void SetOrePanelVisible(bool IsVisible)
    {
        if (OrePanelInfo != null)
        {
            OrePanelInfo.SetActive(IsVisible);
        }
    }

    /// <summary>
    /// Sets whether the vein panel is visible.
    /// </summary>
    private void SetVeinPanelVisible(bool IsVisible)
    {
        if (VeinPanelInfo != null)
        {
            VeinPanelInfo.SetActive(IsVisible);
        }
    }

    /// <summary>
    /// Updates the shared status text.
    /// </summary>
    private void SetStatus(string Message)
    {
        if (StatusInfoRoot != null && !StatusInfoRoot.activeSelf)
        {
            StatusInfoRoot.SetActive(true);
        }

        SetText(SharedStatusText, Message);
    }

    /// <summary>
    /// Updates the optional ore-panel-local status text.
    /// </summary>
    private void SetOrePanelStatus(string Message)
    {
        SetText(OrePanelStatusText, Message);
    }

    /// <summary>
    /// Safely assigns a string to an optional TMP text field.
    /// </summary>
    private void SetText(TMP_Text TextField, string Value)
    {
        if (TextField == null)
        {
            return;
        }

        TextField.text = Value;
    }
}
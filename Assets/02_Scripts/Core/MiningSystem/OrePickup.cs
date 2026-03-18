using UnityEngine;

/// <summary>
/// Stores runtime ore data on a dropped physical ore object.
/// This component is separate from the player's generic item system so ore-specific
/// properties can remain flexible without polluting every item type.
/// </summary>
public sealed class OrePickup : MonoBehaviour
{
    [Header("Runtime Data")]
    [Tooltip("Runtime ore data carried by this dropped pickup.")]
    [SerializeField] private OreItemData OreItemData;

    public void Initialize(OreItemData oreItemData)
    {
        OreItemData = oreItemData;

        if (OreItemData != null && OreItemData.GetOreDefinition() != null)
        {
            gameObject.name = "OrePickup_" + OreItemData.GetOreDefinition().GetDisplayName();
        }
    }

    public OreItemData GetOreItemData()
    {
        return OreItemData;
    }
}

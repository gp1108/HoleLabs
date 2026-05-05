using UnityEngine;

/// <summary>
/// Allows future tools to retrieve a placed drill back into inventory form.
/// </summary>
public interface IDrillRetrievable
{
    /// <summary>
    /// Tries to remove the placed drill and returns its inventory item representation.
    /// </summary>
    bool TryRetrieveDrill(out ItemInstance RetrievedItemInstance);
}
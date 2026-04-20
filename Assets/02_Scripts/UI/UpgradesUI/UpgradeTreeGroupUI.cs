using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manual root for one upgrade tree.
/// This component discovers all tree nodes under its NodesContainer,
/// initializes them and automatically draws visual connections
/// based on the real prerequisite data stored in each UpgradeDefinition.
/// </summary>
public sealed class UpgradeTreeGroupUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Container that holds the manually placed tree node entries.")]
    [SerializeField] private RectTransform NodesContainer;

    [Tooltip("Container where visual connection lines are spawned.")]
    [SerializeField] private RectTransform ConnectionsContainer;

    [Tooltip("Prefab used to render one visual connection line.")]
    [SerializeField] private UpgradeTreeConnectionUI ConnectionPrefab;

    [Tooltip("If true, nodes are rediscovered whenever connections are rebuilt.")]
    [SerializeField] private bool RediscoverNodesOnRebuild = true;

    private UpgradeManager UpgradeManager;

    /// <summary>
    /// Runtime list of tree nodes currently discovered under this group.
    /// </summary>
    private readonly List<UpgradeTreeEntryUI> RegisteredEntries = new();

    /// <summary>
    /// Runtime list of active visual connection instances.
    /// </summary>
    private readonly List<UpgradeTreeConnectionUI> SpawnedConnections = new();

    /// <summary>
    /// Initializes this tree group with runtime references and prepares its current nodes.
    /// </summary>
    public void Initialize(UpgradeManager UpgradeManagerReference)
    {
        UpgradeManager = UpgradeManagerReference;

        DiscoverEntries();
        InitializeEntries();
    }

    /// <summary>
    /// Refreshes every discovered tree node entry.
    /// </summary>
    public void RefreshAllEntries()
    {
        for (int Index = 0; Index < RegisteredEntries.Count; Index++)
        {
            if (RegisteredEntries[Index] != null)
            {
                RegisteredEntries[Index].RefreshView();
            }
        }
    }

    /// <summary>
    /// Rebuilds every visual connection for this tree group using the current manual node layout.
    /// </summary>
    public void RebuildConnections()
    {
        if (RediscoverNodesOnRebuild)
        {
            DiscoverEntries();
            InitializeEntries();
        }

        ClearConnections();

        if (NodesContainer == null || ConnectionsContainer == null || ConnectionPrefab == null)
        {
            return;
        }

        Dictionary<UpgradeDefinition, UpgradeTreeEntryUI> EntriesByDefinition = new();

        for (int Index = 0; Index < RegisteredEntries.Count; Index++)
        {
            UpgradeTreeEntryUI Entry = RegisteredEntries[Index];

            if (Entry == null)
            {
                continue;
            }

            UpgradeDefinition Definition = Entry.GetUpgradeDefinition();

            if (Definition == null)
            {
                continue;
            }

            if (!EntriesByDefinition.ContainsKey(Definition))
            {
                EntriesByDefinition.Add(Definition, Entry);
            }
        }

        foreach (KeyValuePair<UpgradeDefinition, UpgradeTreeEntryUI> Pair in EntriesByDefinition)
        {
            UpgradeDefinition TargetDefinition = Pair.Key;
            UpgradeTreeEntryUI TargetEntry = Pair.Value;
            IReadOnlyList<UpgradeDefinition.UpgradePrerequisiteDefinition> Prerequisites = TargetDefinition.GetPrerequisites();

            if (Prerequisites == null || Prerequisites.Count <= 0)
            {
                continue;
            }

            for (int PrerequisiteIndex = 0; PrerequisiteIndex < Prerequisites.Count; PrerequisiteIndex++)
            {
                UpgradeDefinition.UpgradePrerequisiteDefinition Prerequisite = Prerequisites[PrerequisiteIndex];

                if (Prerequisite == null)
                {
                    continue;
                }

                UpgradeDefinition SourceDefinition = Prerequisite.GetRequiredUpgradeDefinition();

                if (SourceDefinition == null)
                {
                    continue;
                }

                if (!EntriesByDefinition.TryGetValue(SourceDefinition, out UpgradeTreeEntryUI SourceEntry) || SourceEntry == null)
                {
                    continue;
                }

                UpgradeTreeConnectionUI Connection = Instantiate(ConnectionPrefab, ConnectionsContainer);
                UpdateConnectionTransform(Connection, SourceEntry, TargetEntry);
                SpawnedConnections.Add(Connection);
            }
        }
    }

    /// <summary>
    /// Discovers all tree node entries currently placed under the configured nodes container.
    /// </summary>
    public void DiscoverEntries()
    {
        RegisteredEntries.Clear();

        if (NodesContainer == null)
        {
            return;
        }

        UpgradeTreeEntryUI[] Entries = NodesContainer.GetComponentsInChildren<UpgradeTreeEntryUI>(true);

        for (int Index = 0; Index < Entries.Length; Index++)
        {
            if (Entries[Index] != null)
            {
                RegisteredEntries.Add(Entries[Index]);
            }
        }
    }

    /// <summary>
    /// Initializes all discovered tree node entries with the current runtime references.
    /// </summary>
    public void InitializeEntries()
    {
        for (int Index = 0; Index < RegisteredEntries.Count; Index++)
        {
            if (RegisteredEntries[Index] != null)
            {
                RegisteredEntries[Index].Initialize(UpgradeManager);
            }
        }
    }

    /// <summary>
    /// Removes every spawned visual connection currently owned by this tree group.
    /// </summary>
    private void ClearConnections()
    {
        for (int Index = 0; Index < SpawnedConnections.Count; Index++)
        {
            if (SpawnedConnections[Index] != null)
            {
                Destroy(SpawnedConnections[Index].gameObject);
            }
        }

        SpawnedConnections.Clear();
    }

    /// <summary>
    /// Updates one visual connection so it links the provided source and target entries.
    /// </summary>
    private void UpdateConnectionTransform(
        UpgradeTreeConnectionUI Connection,
        UpgradeTreeEntryUI SourceEntry,
        UpgradeTreeEntryUI TargetEntry
    )
    {
        if (Connection == null || ConnectionsContainer == null || SourceEntry == null || TargetEntry == null)
        {
            return;
        }

        Vector3 SourceWorldPosition = SourceEntry.GetConnectionAnchorWorldPosition();
        Vector3 TargetWorldPosition = TargetEntry.GetConnectionAnchorWorldPosition();

        Vector2 SourceLocalPosition = ConnectionsContainer.InverseTransformPoint(SourceWorldPosition);
        Vector2 TargetLocalPosition = ConnectionsContainer.InverseTransformPoint(TargetWorldPosition);

        Connection.SetEndpoints(SourceLocalPosition, TargetLocalPosition);
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

using Object = UnityEngine.Object;

public static class MineralPrefabBatchCreator
{
    private const string InstancesFolder =
        "Assets/00_Meshes/02_Minerals/03_Instances";

    private const string MaterialsRootFolder =
        "Assets/00_Meshes/02_Minerals/01_Textures/01_Minerals";

    private static readonly string[] MeshSearchRoots =
    {
        "Assets/00_Meshes/02_Minerals"
    };

    private static readonly string[] OreDefinitionSearchRoots =
    {
        "Assets/02_Scripts/Core/MiningSystem/Minerals"
    };

    private static readonly string[] FeedbackProfileSearchRoots =
    {
        "Assets/02_Scripts/Game/Feedback/Ores"
    };

    private const string OreTemplatePrefabName = "Ore_Quartz_a01";
    private const string VeinTemplatePrefabName = "Quartz_Vein_a01";

    private const string SourceOreDefinitionName = "OD_Quartz";
    private const string SourceFeedbackProfileName = "FP_Quartz";

    [MenuItem("Tools/Minerals/Crear Ore y Vein Prefabs desde Quartz SAFE")]
    private static void CreatePrefabsSafe()
    {
        Generate(updateExisting: false);
    }

    [MenuItem("Tools/Minerals/Actualizar Ore y Vein Prefabs existentes")]
    private static void UpdateExistingPrefabs()
    {
        bool ok = EditorUtility.DisplayDialog(
            "Actualizar prefabs de minerales",
            "Esto NO borrará ni moverá assets.\n\n" +
            "Si un prefab ya existe, actualizará su MeshFilter, MeshCollider, material, OreDefinition y FeedbackProfile.\n\n" +
            "Si un prefab no existe, lo creará duplicando el prefab de Quartz.\n\n" +
            "¿Continuar?",
            "Sí, actualizar",
            "Cancelar"
        );

        if (!ok)
            return;

        Generate(updateExisting: true);
    }

    private static void Generate(bool updateExisting)
    {
        RunStats stats = new RunStats();
        StringBuilder report = new StringBuilder();

        if (!AssetDatabase.IsValidFolder(InstancesFolder))
        {
            EditorUtility.DisplayDialog(
                "Error",
                $"No existe la carpeta de instancias:\n{InstancesFolder}",
                "OK"
            );
            return;
        }

        if (!AssetDatabase.IsValidFolder(MaterialsRootFolder))
        {
            EditorUtility.DisplayDialog(
                "Error",
                $"No existe la carpeta de materiales:\n{MaterialsRootFolder}",
                "OK"
            );
            return;
        }

        string oreTemplatePath = FindPrefabPath(OreTemplatePrefabName);
        string veinTemplatePath = FindPrefabPath(VeinTemplatePrefabName);

        if (string.IsNullOrEmpty(oreTemplatePath) || string.IsNullOrEmpty(veinTemplatePath))
        {
            EditorUtility.DisplayDialog(
                "Error",
                "No he encontrado los prefabs template de Quartz:\n\n" +
                $"{OreTemplatePrefabName}\n" +
                $"{VeinTemplatePrefabName}\n\n" +
                $"Deben estar en:\n{InstancesFolder}",
                "OK"
            );
            return;
        }

        Object sourceOreDefinition = FindAssetByExactName<Object>(
            SourceOreDefinitionName,
            OreDefinitionSearchRoots
        );

        Object sourceFeedbackProfile = FindAssetByExactName<Object>(
            SourceFeedbackProfileName,
            FeedbackProfileSearchRoots
        );

        bool oreTemplateNeedsOreDefinition =
            sourceOreDefinition != null && CountObjectReferencesInPrefab(oreTemplatePath, sourceOreDefinition) > 0;

        bool veinTemplateNeedsOreDefinition =
            sourceOreDefinition != null && CountObjectReferencesInPrefab(veinTemplatePath, sourceOreDefinition) > 0;

        bool oreTemplateNeedsFeedbackProfile =
            sourceFeedbackProfile != null && CountObjectReferencesInPrefab(oreTemplatePath, sourceFeedbackProfile) > 0;

        bool veinTemplateNeedsFeedbackProfile =
            sourceFeedbackProfile != null && CountObjectReferencesInPrefab(veinTemplatePath, sourceFeedbackProfile) > 0;

        List<MineralInfo> minerals = GetMineralsFromMaterialFolders();

        if (minerals.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Error",
                $"No he encontrado carpetas de minerales dentro de:\n{MaterialsRootFolder}",
                "OK"
            );
            return;
        }

        try
        {
            for (int i = 0; i < minerals.Count; i++)
            {
                MineralInfo mineral = minerals[i];

                EditorUtility.DisplayProgressBar(
                    "Creando prefabs de minerales",
                    mineral.Name,
                    (float)i / minerals.Count
                );

                if (string.Equals(mineral.Name, "Quartz", StringComparison.OrdinalIgnoreCase))
                {
                    stats.Skipped++;
                    report.AppendLine("SKIP  Quartz: es el template de referencia.");
                    continue;
                }

                ProcessMineral(
                    mineral,
                    oreTemplatePath,
                    veinTemplatePath,
                    sourceOreDefinition,
                    sourceFeedbackProfile,
                    oreTemplateNeedsOreDefinition,
                    veinTemplateNeedsOreDefinition,
                    oreTemplateNeedsFeedbackProfile,
                    veinTemplateNeedsFeedbackProfile,
                    updateExisting,
                    stats,
                    report
                );
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "[MineralPrefabBatchCreator] Finalizado.\n\n" +
            $"Creados: {stats.Created}\n" +
            $"Actualizados: {stats.Updated}\n" +
            $"Saltados: {stats.Skipped}\n" +
            $"Warnings: {stats.Warnings}\n" +
            $"Errores: {stats.Errors}\n\n" +
            report
        );

        EditorUtility.DisplayDialog(
            "Prefabs de minerales",
            "Proceso terminado.\n\n" +
            $"Creados: {stats.Created}\n" +
            $"Actualizados: {stats.Updated}\n" +
            $"Saltados: {stats.Skipped}\n" +
            $"Warnings: {stats.Warnings}\n" +
            $"Errores: {stats.Errors}\n\n" +
            "Mira la Console para el detalle completo.",
            "OK"
        );
    }

    private static void ProcessMineral(
        MineralInfo mineral,
        string oreTemplatePath,
        string veinTemplatePath,
        Object sourceOreDefinition,
        Object sourceFeedbackProfile,
        bool oreTemplateNeedsOreDefinition,
        bool veinTemplateNeedsOreDefinition,
        bool oreTemplateNeedsFeedbackProfile,
        bool veinTemplateNeedsFeedbackProfile,
        bool updateExisting,
        RunStats stats,
        StringBuilder report)
    {
        Material dropMaterial = FindMaterial(mineral.Name, "Drop");
        Material veinMaterial = FindMaterial(mineral.Name, "Vein");

        Mesh dropMesh = FindBestMesh(mineral, MeshKind.Drop);
        Mesh veinMesh = FindBestMesh(mineral, MeshKind.Vein);

        Object oreDefinition = FindAssetByExactName<Object>(
            mineral.OreDefinitionAssetName,
            OreDefinitionSearchRoots
        );

        Object feedbackProfile = FindAssetByExactName<Object>(
            mineral.FeedbackProfileAssetName,
            FeedbackProfileSearchRoots
        );

        if (dropMaterial == null)
        {
            stats.Errors++;
            report.AppendLine($"ERROR {mineral.Name}: falta material Drop MI_{mineral.Name}Drop_b01.");
        }

        if (veinMaterial == null)
        {
            stats.Errors++;
            report.AppendLine($"ERROR {mineral.Name}: falta material Vein MI_{mineral.Name}Vein_b01.");
        }

        if (dropMesh == null)
        {
            stats.Errors++;
            report.AppendLine($"ERROR {mineral.Name}: no encontré mesh Drop.");
        }

        if (veinMesh == null)
        {
            stats.Errors++;
            report.AppendLine($"ERROR {mineral.Name}: no encontré mesh Vein.");
        }

        if ((oreTemplateNeedsOreDefinition || veinTemplateNeedsOreDefinition) && oreDefinition == null)
        {
            stats.Errors++;
            report.AppendLine($"ERROR {mineral.Name}: falta OreDefinition {mineral.OreDefinitionAssetName}.");
        }

        if ((oreTemplateNeedsFeedbackProfile || veinTemplateNeedsFeedbackProfile) && feedbackProfile == null)
        {
            stats.Errors++;
            report.AppendLine($"ERROR {mineral.Name}: falta FeedbackProfile {mineral.FeedbackProfileAssetName}.");
        }

        if (dropMaterial != null &&
            dropMesh != null &&
            (!oreTemplateNeedsOreDefinition || oreDefinition != null) &&
            (!oreTemplateNeedsFeedbackProfile || feedbackProfile != null))
        {
            string orePrefabName = $"Ore_{mineral.Name}_a01";
            string oreTargetPath = CombineAssetPath(InstancesFolder, orePrefabName + ".prefab");

            CreateOrUpdatePrefab(
                templatePrefabPath: oreTemplatePath,
                targetPrefabPath: oreTargetPath,
                rootObjectName: orePrefabName,
                mesh: dropMesh,
                material: dropMaterial,
                sourceOreDefinition: sourceOreDefinition,
                targetOreDefinition: oreDefinition,
                sourceFeedbackProfile: sourceFeedbackProfile,
                targetFeedbackProfile: feedbackProfile,
                updateExisting: updateExisting,
                stats: stats,
                report: report
            );
        }

        if (veinMaterial != null &&
            veinMesh != null &&
            (!veinTemplateNeedsOreDefinition || oreDefinition != null) &&
            (!veinTemplateNeedsFeedbackProfile || feedbackProfile != null))
        {
            string veinPrefabName = $"{mineral.Name}_Vein_a01";
            string veinTargetPath = CombineAssetPath(InstancesFolder, veinPrefabName + ".prefab");

            CreateOrUpdatePrefab(
                templatePrefabPath: veinTemplatePath,
                targetPrefabPath: veinTargetPath,
                rootObjectName: veinPrefabName,
                mesh: veinMesh,
                material: veinMaterial,
                sourceOreDefinition: sourceOreDefinition,
                targetOreDefinition: oreDefinition,
                sourceFeedbackProfile: sourceFeedbackProfile,
                targetFeedbackProfile: feedbackProfile,
                updateExisting: updateExisting,
                stats: stats,
                report: report
            );
        }
    }

    private static void CreateOrUpdatePrefab(
        string templatePrefabPath,
        string targetPrefabPath,
        string rootObjectName,
        Mesh mesh,
        Material material,
        Object sourceOreDefinition,
        Object targetOreDefinition,
        Object sourceFeedbackProfile,
        Object targetFeedbackProfile,
        bool updateExisting,
        RunStats stats,
        StringBuilder report)
    {
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(targetPrefabPath) != null;
        bool createdNow = false;

        if (exists && !updateExisting)
        {
            stats.Skipped++;
            report.AppendLine($"SKIP  {targetPrefabPath} ya existe.");
            return;
        }

        if (!exists)
        {
            bool copied = AssetDatabase.CopyAsset(templatePrefabPath, targetPrefabPath);

            if (!copied)
            {
                stats.Errors++;
                report.AppendLine($"ERROR No pude copiar prefab hacia: {targetPrefabPath}");
                return;
            }

            createdNow = true;
        }

        bool configured = ConfigurePrefab(
            targetPrefabPath,
            rootObjectName,
            mesh,
            material,
            sourceOreDefinition,
            targetOreDefinition,
            sourceFeedbackProfile,
            targetFeedbackProfile,
            report
        );

        if (!configured)
        {
            stats.Errors++;

            if (createdNow)
                AssetDatabase.DeleteAsset(targetPrefabPath);

            report.AppendLine($"ERROR {targetPrefabPath}: configuración fallida. Prefab nuevo eliminado si acababa de crearse.");
            return;
        }

        if (createdNow)
        {
            stats.Created++;
            report.AppendLine($"CREATE {targetPrefabPath}");
        }
        else
        {
            stats.Updated++;
            report.AppendLine($"UPDATE {targetPrefabPath}");
        }

        report.AppendLine($"      Mesh     -> {AssetDatabase.GetAssetPath(mesh)} / {mesh.name}");
        report.AppendLine($"      Material -> {AssetDatabase.GetAssetPath(material)}");
    }

    private static bool ConfigurePrefab(
        string prefabPath,
        string rootObjectName,
        Mesh mesh,
        Material material,
        Object sourceOreDefinition,
        Object targetOreDefinition,
        Object sourceFeedbackProfile,
        Object targetFeedbackProfile,
        StringBuilder report)
    {
        GameObject root = null;

        try
        {
            root = PrefabUtility.LoadPrefabContents(prefabPath);

            if (root == null)
            {
                report.AppendLine($"ERROR {prefabPath}: no pude abrir prefab contents.");
                return false;
            }

            root.name = rootObjectName;

            Transform visuals = FindChildRecursive(root.transform, "Visuals");

            if (visuals == null)
            {
                report.AppendLine($"ERROR {prefabPath}: no encontré child 'Visuals'.");
                return false;
            }

            MeshFilter meshFilter = visuals.GetComponent<MeshFilter>();

            if (meshFilter == null)
                meshFilter = visuals.GetComponentInChildren<MeshFilter>(true);

            if (meshFilter == null)
            {
                report.AppendLine($"ERROR {prefabPath}: Visuals no tiene MeshFilter.");
                return false;
            }

            meshFilter.sharedMesh = mesh;
            EditorUtility.SetDirty(meshFilter);

            MeshCollider[] meshColliders = visuals.GetComponentsInChildren<MeshCollider>(true);

            foreach (MeshCollider meshCollider in meshColliders)
            {
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = mesh;
                EditorUtility.SetDirty(meshCollider);
            }

            Renderer[] renderers = visuals.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
            {
                report.AppendLine($"ERROR {prefabPath}: Visuals no tiene Renderer.");
                return false;
            }

            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;

                if (materials == null || materials.Length == 0)
                {
                    renderer.sharedMaterial = material;
                }
                else
                {
                    for (int i = 0; i < materials.Length; i++)
                        materials[i] = material;

                    renderer.sharedMaterials = materials;
                }

                EditorUtility.SetDirty(renderer);
            }

            int oreDefinitionRefs = ReplaceObjectReferencesInPrefab(
                root,
                sourceOreDefinition,
                targetOreDefinition,
                ReferenceKind.OreDefinition
            );

            int feedbackRefs = ReplaceObjectReferencesInPrefab(
                root,
                sourceFeedbackProfile,
                targetFeedbackProfile,
                ReferenceKind.FeedbackProfile
            );

            report.AppendLine($"      OreDefinition refs reemplazadas -> {oreDefinitionRefs}");
            report.AppendLine($"      Feedback refs reemplazadas      -> {feedbackRefs}");

            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

            return true;
        }
        catch (Exception ex)
        {
            report.AppendLine($"ERROR {prefabPath}: {ex.Message}");
            return false;
        }
        finally
        {
            if (root != null)
                PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static int ReplaceObjectReferencesInPrefab(
        GameObject root,
        Object source,
        Object target,
        ReferenceKind kind)
    {
        if (root == null || target == null)
            return 0;

        int replaced = 0;
        Component[] components = root.GetComponentsInChildren<Component>(true);

        foreach (Component component in components)
        {
            if (component == null)
                continue;

            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty property = serializedObject.GetIterator();

            bool enterChildren = true;
            bool changed = false;

            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                if (property.name == "m_Script")
                    continue;

                if (!ShouldReplaceReference(property, source, target, kind))
                    continue;

                property.objectReferenceValue = target;
                replaced++;
                changed = true;
            }

            if (changed)
            {
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(component);
            }
        }

        return replaced;
    }

    private static bool ShouldReplaceReference(
        SerializedProperty property,
        Object source,
        Object target,
        ReferenceKind kind)
    {
        Object current = property.objectReferenceValue;

        if (source != null && current == source)
            return true;

        if (source != null &&
            current != null &&
            string.Equals(current.name, source.name, StringComparison.OrdinalIgnoreCase))
            return true;

        if (current != null)
            return false;

        string bag =
            $"{property.name} {property.displayName} {property.type}";

        if (!SerializedPropertyTypeLooksCompatible(property, target))
            return false;

        if (kind == ReferenceKind.OreDefinition)
        {
            return ContainsCI(bag, "ore") && ContainsCI(bag, "definition");
        }

        if (kind == ReferenceKind.FeedbackProfile)
        {
            return ContainsCI(bag, "feedback") && ContainsCI(bag, "profile");
        }

        return false;
    }

    private static bool SerializedPropertyTypeLooksCompatible(
        SerializedProperty property,
        Object target)
    {
        if (property == null || target == null)
            return false;

        string targetTypeName = target.GetType().Name;
        string propertyType = property.type ?? "";

        return ContainsCI(propertyType, targetTypeName);
    }

    private static int CountObjectReferencesInPrefab(string prefabPath, Object source)
    {
        if (string.IsNullOrEmpty(prefabPath) || source == null)
            return 0;

        GameObject root = null;
        int count = 0;

        try
        {
            root = PrefabUtility.LoadPrefabContents(prefabPath);

            if (root == null)
                return 0;

            Component[] components = root.GetComponentsInChildren<Component>(true);

            foreach (Component component in components)
            {
                if (component == null)
                    continue;

                SerializedObject serializedObject = new SerializedObject(component);
                SerializedProperty property = serializedObject.GetIterator();

                bool enterChildren = true;

                while (property.NextVisible(enterChildren))
                {
                    enterChildren = false;

                    if (property.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    if (property.name == "m_Script")
                        continue;

                    Object current = property.objectReferenceValue;

                    if (current == source)
                        count++;
                }
            }

            return count;
        }
        finally
        {
            if (root != null)
                PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Mesh FindBestMesh(MineralInfo mineral, MeshKind kind)
    {
        List<MeshCandidate> candidates = new List<MeshCandidate>();
        HashSet<string> assetPaths = GetCandidateMeshAssetPaths();

        foreach (string path in assetPaths)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);

            foreach (Object asset in assets)
            {
                Mesh mesh = asset as Mesh;

                if (mesh == null)
                    continue;

                if (mesh.name.StartsWith("__preview", StringComparison.OrdinalIgnoreCase))
                    continue;

                int score = ScoreMeshCandidate(mineral, kind, path, mesh.name);

                if (score <= 0)
                    continue;

                candidates.Add(new MeshCandidate
                {
                    Mesh = mesh,
                    AssetPath = path,
                    Score = score
                });
            }
        }

        MeshCandidate best = candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.AssetPath)
            .FirstOrDefault();

        int minimumScore = kind == MeshKind.Drop ? 1000 : 700;

        if (best == null || best.Score < minimumScore)
            return null;

        return best.Mesh;
    }

    private static HashSet<string> GetCandidateMeshAssetPaths()
    {
        HashSet<string> paths = new HashSet<string>();

        string[] roots = GetValidSearchRoots(MeshSearchRoots);

        if (roots.Length == 0)
            roots = new[] { "Assets" };

        foreach (string root in roots)
        {
            string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { root });

            foreach (string guid in modelGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (string.IsNullOrEmpty(path))
                    continue;

                paths.Add(path);
            }

            string[] meshGuids = AssetDatabase.FindAssets("t:Mesh", new[] { root });

            foreach (string guid in meshGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (string.IsNullOrEmpty(path))
                    continue;

                paths.Add(path);
            }
        }

        return paths;
    }

    private static int ScoreMeshCandidate(
        MineralInfo mineral,
        MeshKind kind,
        string assetPath,
        string meshName)
    {
        string path = NormalizeForSearch(assetPath);
        string file = NormalizeForSearch(Path.GetFileNameWithoutExtension(assetPath));
        string mesh = NormalizeForSearch(meshName);

        int score = 0;
        bool matchedAlias = false;

        foreach (string rawAlias in mineral.MeshAliases)
        {
            string alias = NormalizeForSearch(rawAlias);

            if (string.IsNullOrEmpty(alias))
                continue;

            if (path.Contains("/" + alias + "/"))
            {
                score += 800;
                matchedAlias = true;
            }

            if (file.Contains(alias))
            {
                score += 500;
                matchedAlias = true;
            }

            if (mesh.Contains(alias))
            {
                score += 400;
                matchedAlias = true;
            }
        }

        if (!matchedAlias)
            return -10000;

        if (path.Contains("/01_textures/") || path.Contains("/03_instances/"))
            score -= 5000;

        if (path.Contains("/rocks/") || path.Contains("/02_fragments/"))
            score -= 5000;

        bool looksDrop =
            path.Contains("/drop/") ||
            file.Contains("drop") ||
            mesh.Contains("drop");

        if (kind == MeshKind.Drop)
        {
            if (path.Contains("/drop/"))
                score += 900;

            if (looksDrop)
                score += 600;
            else
                score -= 2500;
        }
        else
        {
            if (looksDrop)
                score -= 6000;

            if (file.Contains("vein") || mesh.Contains("vein") || path.Contains("vein"))
                score += 500;

            if (file.EndsWith("_a01") || mesh.EndsWith("_a01"))
                score += 100;
        }

        return score;
    }

    private static Material FindMaterial(string mineralName, string variant)
    {
        string expectedPath = CombineAssetPath(
            CombineAssetPath(MaterialsRootFolder, mineralName),
            $"MI_{mineralName}{variant}_b01.mat"
        );

        Material direct = AssetDatabase.LoadAssetAtPath<Material>(expectedPath);

        if (direct != null)
            return direct;

        return FindAssetByExactName<Material>(
            $"MI_{mineralName}{variant}_b01",
            new[] { MaterialsRootFolder }
        );
    }

    private static List<MineralInfo> GetMineralsFromMaterialFolders()
    {
        List<MineralInfo> minerals = new List<MineralInfo>();

        string[] folders = AssetDatabase.GetSubFolders(MaterialsRootFolder);

        foreach (string folder in folders)
        {
            string mineralName = Path.GetFileName(folder);

            minerals.Add(BuildMineralInfo(mineralName));
        }

        return minerals
            .OrderBy(m => m.Name)
            .ToList();
    }

    private static MineralInfo BuildMineralInfo(string mineralName)
    {
        List<string> aliases = new List<string>();
        aliases.Add(mineralName);

        string oreDefinitionName = "OD_" + mineralName;
        string feedbackProfileName = "FP_" + mineralName;

        AddAliasesForKnownMineral(mineralName, aliases, ref oreDefinitionName, ref feedbackProfileName);

        return new MineralInfo
        {
            Name = mineralName,
            OreDefinitionAssetName = oreDefinitionName,
            FeedbackProfileAssetName = feedbackProfileName,
            MeshAliases = aliases
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static void AddAliasesForKnownMineral(
        string mineralName,
        List<string> aliases,
        ref string oreDefinitionName,
        ref string feedbackProfileName)
    {
        if (EqualsCI(mineralName, "Amethyst"))
        {
            aliases.Add("Amatista");
            aliases.Add("Amethyts");
        }
        else if (EqualsCI(mineralName, "Carnelian"))
        {
            aliases.Add("Cornalina");
        }
        else if (EqualsCI(mineralName, "Coal"))
        {
            aliases.Add("Carbon");
            aliases.Add("Carbón");
        }
        else if (EqualsCI(mineralName, "Copper"))
        {
            aliases.Add("Cobre");
            aliases.Add("Bronze");
            aliases.Add("Bronce");
        }
        else if (EqualsCI(mineralName, "Diamond"))
        {
            aliases.Add("Diamante");
        }
        else if (EqualsCI(mineralName, "Emerald"))
        {
            aliases.Add("Esmeralda");
        }
        else if (EqualsCI(mineralName, "Fluorite"))
        {
            aliases.Add("Fluorita");
        }
        else if (EqualsCI(mineralName, "Gold"))
        {
            aliases.Add("Oro");
        }
        else if (EqualsCI(mineralName, "Halite"))
        {
            aliases.Add("Halita");
        }
        else if (EqualsCI(mineralName, "Iron"))
        {
            aliases.Add("Hierro");
        }
        else if (EqualsCI(mineralName, "Obsidian"))
        {
            aliases.Add("Obsidiana");
        }
        else if (EqualsCI(mineralName, "Pizarra"))
        {
            aliases.Add("Slate");
            feedbackProfileName = "FP_Slate";
        }
        else if (EqualsCI(mineralName, "Slate"))
        {
            aliases.Add("Pizarra");
            oreDefinitionName = "OD_Pizarra";
            feedbackProfileName = "FP_Slate";
        }
        else if (EqualsCI(mineralName, "Pyrite"))
        {
            aliases.Add("Pirita");
        }
        else if (EqualsCI(mineralName, "Quartz"))
        {
            aliases.Add("Cuarzo");
        }
        else if (EqualsCI(mineralName, "Ruby"))
        {
            aliases.Add("Rubi");
            aliases.Add("Rubí");
        }
        else if (EqualsCI(mineralName, "Tungsten"))
        {
            aliases.Add("Tungsteno");
        }
        else if (EqualsCI(mineralName, "Turquoise"))
        {
            aliases.Add("Turquesa");
        }
        else if (EqualsCI(mineralName, "Uranium"))
        {
            aliases.Add("Uranio");
        }
    }

    private static string FindPrefabPath(string prefabName)
    {
        string expectedPath = CombineAssetPath(InstancesFolder, prefabName + ".prefab");

        GameObject direct = AssetDatabase.LoadAssetAtPath<GameObject>(expectedPath);

        if (direct != null)
            return expectedPath;

        string[] guids = AssetDatabase.FindAssets(prefabName + " t:Prefab", new[] { InstancesFolder });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);

            if (string.Equals(fileName, prefabName, StringComparison.OrdinalIgnoreCase))
                return path;
        }

        return null;
    }

    private static T FindAssetByExactName<T>(string assetName, string[] preferredRoots)
        where T : Object
    {
        if (string.IsNullOrWhiteSpace(assetName))
            return null;

        string[] validRoots = GetValidSearchRoots(preferredRoots);

        foreach (string root in validRoots)
        {
            T result = FindAssetByExactNameInRoots<T>(assetName, new[] { root });

            if (result != null)
                return result;
        }

        return FindAssetByExactNameInRoots<T>(assetName, null);
    }

    private static T FindAssetByExactNameInRoots<T>(string assetName, string[] roots)
        where T : Object
    {
        string query = assetName;

        if (typeof(T) == typeof(Material))
            query += " t:Material";

        string[] guids = roots == null
            ? AssetDatabase.FindAssets(query)
            : AssetDatabase.FindAssets(query, roots);

        List<string> exactPaths = new List<string>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (string.IsNullOrEmpty(path))
                continue;

            string fileName = Path.GetFileNameWithoutExtension(path);

            if (!string.Equals(fileName, assetName, StringComparison.OrdinalIgnoreCase))
                continue;

            exactPaths.Add(path);
        }

        exactPaths.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (string path in exactPaths)
        {
            T typed = AssetDatabase.LoadAssetAtPath<T>(path);

            if (typed != null)
                return typed;

            Object main = AssetDatabase.LoadMainAssetAtPath(path);

            if (main is T)
                return (T)main;
        }

        return null;
    }

    private static string[] GetValidSearchRoots(string[] roots)
    {
        if (roots == null)
            return new string[0];

        return roots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Where(AssetDatabase.IsValidFolder)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Transform FindChildRecursive(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        foreach (Transform child in parent)
        {
            if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
                return child;

            Transform nested = FindChildRecursive(child, childName);

            if (nested != null)
                return nested;
        }

        return null;
    }

    private static string CombineAssetPath(string a, string b)
    {
        return a.TrimEnd('/') + "/" + b.TrimStart('/');
    }

    private static bool ContainsCI(string source, string value)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
            return false;

        return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool EqualsCI(string a, string b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForSearch(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return value
            .Replace("\\", "/")
            .Replace("á", "a")
            .Replace("é", "e")
            .Replace("í", "i")
            .Replace("ó", "o")
            .Replace("ú", "u")
            .Replace("Á", "a")
            .Replace("É", "e")
            .Replace("Í", "i")
            .Replace("Ó", "o")
            .Replace("Ú", "u")
            .ToLowerInvariant();
    }

    private enum MeshKind
    {
        Drop,
        Vein
    }

    private enum ReferenceKind
    {
        OreDefinition,
        FeedbackProfile
    }

    private sealed class MineralInfo
    {
        public string Name;
        public string OreDefinitionAssetName;
        public string FeedbackProfileAssetName;
        public string[] MeshAliases;
    }

    private sealed class MeshCandidate
    {
        public Mesh Mesh;
        public string AssetPath;
        public int Score;
    }

    private sealed class RunStats
    {
        public int Created;
        public int Updated;
        public int Skipped;
        public int Warnings;
        public int Errors;
    }
}
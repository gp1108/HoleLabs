using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

using Object = UnityEngine.Object;

public static class OreDefinitionBatchConfigurator
{
    private const string OreDefinitionsFolder =
        "Assets/02_Scripts/Core/MiningSystem/Minerals";

    private const string OrePrefabsFolder =
        "Assets/00_Meshes/02_Minerals/03_Instances";

    [MenuItem("Tools/Minerals/Configurar Ore Definitions desde GDD")]
    private static void ConfigureOreDefinitions()
    {
        bool ok = EditorUtility.DisplayDialog(
            "Configurar Ore Definitions",
            "Esto modificará los assets OD_* existentes.\n\n" +
            "No moverá assets, no borrará assets y no tocará iconos.\n\n" +
            "Asignará valores de gameplay, Dropped Ore Prefab y datos base según el GDD.\n\n" +
            "¿Continuar?",
            "Sí, configurar",
            "Cancelar"
        );

        if (!ok)
            return;

        if (!AssetDatabase.IsValidFolder(OreDefinitionsFolder))
        {
            EditorUtility.DisplayDialog(
                "Error",
                $"No existe la carpeta de OreDefinitions:\n{OreDefinitionsFolder}",
                "OK"
            );
            return;
        }

        if (!AssetDatabase.IsValidFolder(OrePrefabsFolder))
        {
            EditorUtility.DisplayDialog(
                "Error",
                $"No existe la carpeta de prefabs de ores:\n{OrePrefabsFolder}",
                "OK"
            );
            return;
        }

        List<OreConfig> configs = BuildOreConfigs();

        RunStats stats = new RunStats();
        StringBuilder report = new StringBuilder();

        try
        {
            for (int i = 0; i < configs.Count; i++)
            {
                OreConfig config = configs[i];

                EditorUtility.DisplayProgressBar(
                    "Configurando OreDefinitions",
                    config.OreDefinitionAssetName,
                    (float)i / configs.Count
                );

                ConfigureSingleOreDefinition(config, stats, report);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "[OreDefinitionBatchConfigurator] Finalizado.\n\n" +
            $"Configurados: {stats.Configured}\n" +
            $"Warnings: {stats.Warnings}\n" +
            $"Errores: {stats.Errors}\n\n" +
            report
        );

        EditorUtility.DisplayDialog(
            "Ore Definitions",
            "Proceso terminado.\n\n" +
            $"Configurados: {stats.Configured}\n" +
            $"Warnings: {stats.Warnings}\n" +
            $"Errores: {stats.Errors}\n\n" +
            "Mira la Console para el detalle completo.",
            "OK"
        );
    }

    private static void ConfigureSingleOreDefinition(
        OreConfig config,
        RunStats stats,
        StringBuilder report)
    {
        Object oreDefinition = FindAssetByExactName<Object>(
            config.OreDefinitionAssetName,
            new[] { OreDefinitionsFolder }
        );

        if (oreDefinition == null)
        {
            stats.Errors++;
            report.AppendLine($"ERROR Falta OreDefinition: {config.OreDefinitionAssetName}");
            return;
        }

        GameObject droppedOrePrefab = FindDroppedOrePrefab(config.PrefabMineralName);

        if (droppedOrePrefab == null)
        {
            stats.Errors++;
            report.AppendLine($"ERROR {config.OreDefinitionAssetName}: falta prefab Ore_{config.PrefabMineralName}_a01.");
            return;
        }

        Undo.RecordObject(oreDefinition, "Configure OreDefinition");

        SerializedObject serializedObject = new SerializedObject(oreDefinition);
        PropertyEditContext context = new PropertyEditContext(config.OreDefinitionAssetName, report);

        SetString(
            serializedObject,
            context,
            config.OreId,
            "Ore Id",
            "OreID",
            "oreId"
        );

        SetString(
            serializedObject,
            context,
            config.DisplayName,
            "Display Name",
            "displayName"
        );

        SetObjectReference(
            serializedObject,
            context,
            droppedOrePrefab,
            "Dropped Ore Prefab",
            "droppedOrePrefab"
        );

        SetMiningTier(
            serializedObject,
            context,
            config.RequiredMiningTier,
            "Required Mining Tier",
            "requiredMiningTier",
            "miningTier"
        );

        SetNumber(
            serializedObject,
            context,
            config.BaseMiningDurability,
            "Base Mining Durability",
            "baseMiningDurability",
            "miningDurability",
            "durability"
        );

        SetNumber(
            serializedObject,
            context,
            config.BaseRespawnTime,
            "Base Respawn Time",
            "baseRespawnTime",
            "respawnTime"
        );

        SetNumber(
            serializedObject,
            context,
            config.BaseDropCountMin,
            "Base Drop Count Min",
            "baseDropCountMin",
            "dropCountMin"
        );

        SetNumber(
            serializedObject,
            context,
            config.BaseDropCountMax,
            "Base Drop Count Max",
            "baseDropCountMax",
            "dropCountMax"
        );

        SetNumber(
            serializedObject,
            context,
            config.MinPurityPercent,
            "Min Purity Percent",
            "minPurityPercent",
            "purityMin"
        );

        SetNumber(
            serializedObject,
            context,
            config.MaxPurityPercent,
            "Max Purity Percent",
            "maxPurityPercent",
            "purityMax"
        );

        SetNumber(
            serializedObject,
            context,
            config.MinSizeScale,
            "Min Size Scale",
            "minSizeScale",
            "sizeScaleMin"
        );

        SetNumber(
            serializedObject,
            context,
            config.MaxSizeScale,
            "Max Size Scale",
            "maxSizeScale",
            "sizeScaleMax"
        );

        SetNumber(
            serializedObject,
            context,
            config.MinCreditValue,
            "Min Credit Value",
            "minCreditValue",
            "creditValueMin"
        );

        SetNumber(
            serializedObject,
            context,
            config.MaxCreditValue,
            "Max Credit Value",
            "maxCreditValue",
            "creditValueMax"
        );

        SetNumber(
            serializedObject,
            context,
            config.PurityCreditContribution,
            "Purity Credit Contribution",
            "purityCreditContribution"
        );

        SetNumber(
            serializedObject,
            context,
            config.SizeCreditContribution,
            "Size Credit Contribution",
            "sizeCreditContribution"
        );

        SetNumber(
            serializedObject,
            context,
            config.MinWeightValue,
            "Min Weight Value",
            "minWeightValue",
            "weightValueMin"
        );

        SetNumber(
            serializedObject,
            context,
            config.MaxWeightValue,
            "Max Weight Value",
            "maxWeightValue",
            "weightValueMax"
        );

        SetNumber(
            serializedObject,
            context,
            config.PurityWeightContribution,
            "Purity Weight Contribution",
            "purityWeightContribution"
        );

        SetNumber(
            serializedObject,
            context,
            config.SizeWeightContribution,
            "Size Weight Contribution",
            "sizeWeightContribution"
        );

        serializedObject.ApplyModifiedProperties();

        EditorUtility.SetDirty(oreDefinition);

        stats.Configured++;
        stats.Warnings += context.Warnings;
        stats.Errors += context.Errors;

        report.AppendLine(
            $"OK    {config.OreDefinitionAssetName} -> {AssetDatabase.GetAssetPath(droppedOrePrefab)}"
        );
    }

    private static List<OreConfig> BuildOreConfigs()
    {
        // Valores base extraídos del GDD:
        // Tier, Hits/Vida, Menas Min/Max, Valor Base y Peso Base.
        //
        // Valores inferidos:
        // Respawn, pureza, tamaño y margen controlado de valor/peso por item.
        //
        // Nota importante:
        // El GDD parece calcular VALOR MAX y PESO MAX como total potencial de la veta
        // usando el número de drops. Por eso aquí NO se mete VALOR MAX directamente
        // como Max Credit Value por item, para evitar duplicar progresión con el drop count.

        return new List<OreConfig>
        {
            new OreConfig(
                assetName: "Quartz",
                displayName: "Cuarzo",
                oreId: "Ore.Quartz",
                tier: 1,
                durability: 3,
                dropMin: 1,
                dropMax: 1,
                respawn: 10f,
                purityMin: 0f,
                purityMax: 20f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 5f,
                creditMax: 6.25f,
                weightMin: 2f,
                weightMax: 2.5f
            ),

            new OreConfig(
                assetName: "Pizarra",
                displayName: "Pizarra",
                oreId: "Ore.Pizarra",
                tier: 1,
                durability: 4,
                dropMin: 1,
                dropMax: 2,
                respawn: 10f,
                purityMin: 0f,
                purityMax: 20f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 3f,
                creditMax: 3.75f,
                weightMin: 1f,
                weightMax: 1.25f
            ),

            new OreConfig(
                assetName: "Iron",
                displayName: "Hierro",
                oreId: "Ore.Iron",
                tier: 1,
                durability: 6,
                dropMin: 1,
                dropMax: 2,
                respawn: 15f,
                purityMin: 5f,
                purityMax: 25f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 6f,
                creditMax: 7.5f,
                weightMin: 4f,
                weightMax: 5f
            ),

            new OreConfig(
                assetName: "Coal",
                displayName: "Carbón",
                oreId: "Ore.Coal",
                tier: 1,
                durability: 8,
                dropMin: 1,
                dropMax: 3,
                respawn: 16f,
                purityMin: 5f,
                purityMax: 25f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 4.5f,
                creditMax: 5.6f,
                weightMin: 3f,
                weightMax: 3.75f
            ),

            new OreConfig(
                assetName: "Halite",
                displayName: "Halita",
                oreId: "Ore.Halite",
                tier: 1,
                durability: 11,
                dropMin: 3,
                dropMax: 5,
                respawn: 20f,
                purityMin: 5f,
                purityMax: 30f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 2f,
                creditMax: 2.5f,
                weightMin: 0.5f,
                weightMax: 0.65f
            ),

            new OreConfig(
                assetName: "Copper",
                displayName: "Cobre",
                oreId: "Ore.Copper",
                tier: 1,
                durability: 14,
                dropMin: 1,
                dropMax: 3,
                respawn: 22f,
                purityMin: 8f,
                purityMax: 32f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 6f,
                creditMax: 7.5f,
                weightMin: 3.5f,
                weightMax: 4.4f
            ),

            new OreConfig(
                assetName: "Turquoise",
                displayName: "Turquesa",
                oreId: "Ore.Turquoise",
                tier: 2,
                durability: 17,
                dropMin: 2,
                dropMax: 4,
                respawn: 26f,
                purityMin: 10f,
                purityMax: 40f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 8f,
                creditMax: 10f,
                weightMin: 3f,
                weightMax: 3.75f
            ),

            new OreConfig(
                assetName: "Fluorite",
                displayName: "Fluorita",
                oreId: "Ore.Fluorite",
                tier: 2,
                durability: 22,
                dropMin: 1,
                dropMax: 3,
                respawn: 30f,
                purityMin: 10f,
                purityMax: 42f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 12f,
                creditMax: 15f,
                weightMin: 4f,
                weightMax: 5f
            ),

            new OreConfig(
                assetName: "Carnelian",
                displayName: "Cornalina",
                oreId: "Ore.Carnelian",
                tier: 2,
                durability: 24,
                dropMin: 1,
                dropMax: 1,
                respawn: 32f,
                purityMin: 12f,
                purityMax: 45f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 22f,
                creditMax: 27.5f,
                weightMin: 6f,
                weightMax: 7.5f
            ),

            new OreConfig(
                assetName: "Pyrite",
                displayName: "Pirita",
                oreId: "Ore.Pyrite",
                tier: 2,
                durability: 28,
                dropMin: 3,
                dropMax: 6,
                respawn: 34f,
                purityMin: 12f,
                purityMax: 45f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 4f,
                creditMax: 5f,
                weightMin: 4f,
                weightMax: 5f
            ),

            new OreConfig(
                assetName: "Tungsten",
                displayName: "Tungsteno",
                oreId: "Ore.Tungsten",
                tier: 3,
                durability: 39,
                dropMin: 1,
                dropMax: 2,
                respawn: 45f,
                purityMin: 18f,
                purityMax: 55f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 50f,
                creditMax: 62.5f,
                weightMin: 12f,
                weightMax: 15f
            ),

            new OreConfig(
                assetName: "Amethyst",
                displayName: "Amatista",
                oreId: "Ore.Amethyst",
                tier: 2,
                durability: 45,
                dropMin: 1,
                dropMax: 4,
                respawn: 50f,
                purityMin: 15f,
                purityMax: 55f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 15f,
                creditMax: 18.75f,
                weightMin: 9f,
                weightMax: 11.25f
            ),

            new OreConfig(
                assetName: "Gold",
                displayName: "Oro",
                oreId: "Ore.Gold",
                tier: 2,
                durability: 52,
                dropMin: 1,
                dropMax: 3,
                respawn: 55f,
                purityMin: 18f,
                purityMax: 60f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 23f,
                creditMax: 28.75f,
                weightMin: 10f,
                weightMax: 12.5f
            ),

            new OreConfig(
                assetName: "Ruby",
                displayName: "Rubí",
                oreId: "Ore.Ruby",
                tier: 3,
                durability: 60,
                dropMin: 1,
                dropMax: 2,
                respawn: 60f,
                purityMin: 22f,
                purityMax: 65f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 25f,
                creditMax: 31.25f,
                weightMin: 8f,
                weightMax: 10f
            ),

            new OreConfig(
                assetName: "Uranium",
                displayName: "Uranio",
                oreId: "Ore.Uranium",
                tier: 3,
                durability: 72,
                dropMin: 4,
                dropMax: 7,
                respawn: 75f,
                purityMin: 20f,
                purityMax: 60f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 15f,
                creditMax: 18.75f,
                weightMin: 9f,
                weightMax: 11.25f
            ),

            new OreConfig(
                assetName: "Emerald",
                displayName: "Esmeralda",
                oreId: "Ore.Emerald",
                tier: 3,
                durability: 82,
                dropMin: 1,
                dropMax: 2,
                respawn: 90f,
                purityMin: 25f,
                purityMax: 75f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 75f,
                creditMax: 93.75f,
                weightMin: 12f,
                weightMax: 15f
            ),

            new OreConfig(
                assetName: "Obsidian",
                displayName: "Obsidiana",
                oreId: "Ore.Obsidian",
                tier: 3,
                durability: 96,
                dropMin: 2,
                dropMax: 4,
                respawn: 95f,
                purityMin: 20f,
                purityMax: 65f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 30f,
                creditMax: 37.5f,
                weightMin: 15f,
                weightMax: 18.75f
            ),

            new OreConfig(
                assetName: "Diamond",
                displayName: "Diamante",
                oreId: "Ore.Diamond",
                tier: 3,
                durability: 112,
                dropMin: 1,
                dropMax: 1,
                respawn: 120f,
                purityMin: 35f,
                purityMax: 90f,
                sizeMin: 0.85f,
                sizeMax: 1.25f,
                creditMin: 200f,
                creditMax: 250f,
                weightMin: 13f,
                weightMax: 16.25f
            )
        };
    }

    private static GameObject FindDroppedOrePrefab(string mineralName)
    {
        string expectedPath = CombineAssetPath(
            OrePrefabsFolder,
            $"Ore_{mineralName}_a01.prefab"
        );

        GameObject direct = AssetDatabase.LoadAssetAtPath<GameObject>(expectedPath);

        if (direct != null)
            return direct;

        return FindAssetByExactName<GameObject>(
            $"Ore_{mineralName}_a01",
            new[] { OrePrefabsFolder }
        );
    }

    private static void SetString(
        SerializedObject serializedObject,
        PropertyEditContext context,
        string value,
        params string[] aliases)
    {
        SerializedProperty property = FindProperty(serializedObject, aliases);

        if (property == null)
        {
            context.Warn($"No encontré propiedad string: {string.Join(" / ", aliases)}");
            return;
        }

        if (property.propertyType != SerializedPropertyType.String)
        {
            context.Error($"La propiedad '{property.displayName}' no es string. Tipo real: {property.propertyType}");
            return;
        }

        property.stringValue = value;
    }

    private static void SetNumber(
        SerializedObject serializedObject,
        PropertyEditContext context,
        float value,
        params string[] aliases)
    {
        SerializedProperty property = FindProperty(serializedObject, aliases);

        if (property == null)
        {
            context.Warn($"No encontré propiedad numérica: {string.Join(" / ", aliases)}");
            return;
        }

        if (property.propertyType == SerializedPropertyType.Integer)
        {
            property.intValue = Mathf.RoundToInt(value);
            return;
        }

        if (property.propertyType == SerializedPropertyType.Float)
        {
            property.floatValue = value;
            return;
        }

        context.Error($"La propiedad '{property.displayName}' no es int/float. Tipo real: {property.propertyType}");
    }

    private static void SetObjectReference(
        SerializedObject serializedObject,
        PropertyEditContext context,
        Object value,
        params string[] aliases)
    {
        SerializedProperty property = FindProperty(serializedObject, aliases);

        if (property == null)
        {
            context.Warn($"No encontré propiedad ObjectReference: {string.Join(" / ", aliases)}");
            return;
        }

        if (property.propertyType != SerializedPropertyType.ObjectReference)
        {
            context.Error($"La propiedad '{property.displayName}' no es ObjectReference. Tipo real: {property.propertyType}");
            return;
        }

        property.objectReferenceValue = value;
    }

    private static void SetMiningTier(
        SerializedObject serializedObject,
        PropertyEditContext context,
        int tier,
        params string[] aliases)
    {
        SerializedProperty property = FindProperty(serializedObject, aliases);

        if (property == null)
        {
            context.Warn($"No encontré propiedad enum de tier: {string.Join(" / ", aliases)}");
            return;
        }

        if (property.propertyType == SerializedPropertyType.Enum)
        {
            int enumIndex = FindTierEnumIndex(property, tier);

            if (enumIndex >= 0)
            {
                property.enumValueIndex = enumIndex;
                return;
            }

            int fallbackIndex = GetTierFallbackEnumIndex(property, tier);
            property.enumValueIndex = fallbackIndex;

            context.Warn(
                $"No pude resolver exactamente el enum de tier para Tier {tier}. " +
                $"Usé fallback enum index {fallbackIndex}."
            );

            return;
        }

        if (property.propertyType == SerializedPropertyType.Integer)
        {
            property.intValue = tier;
            return;
        }

        context.Error($"La propiedad '{property.displayName}' no es enum/int. Tipo real: {property.propertyType}");
    }

    private static int FindTierEnumIndex(SerializedProperty property, int tier)
    {
        string roman = ToRoman(tier);

        string wantedRoman = NormalizeForSearch("Tier" + roman);
        string wantedNumber = NormalizeForSearch("Tier" + tier.ToString());
        string wantedRawRoman = NormalizeForSearch(roman);
        string wantedRawNumber = NormalizeForSearch(tier.ToString());

        for (int i = 0; i < property.enumDisplayNames.Length; i++)
        {
            string displayName = NormalizeForSearch(property.enumDisplayNames[i]);
            string enumName = i < property.enumNames.Length
                ? NormalizeForSearch(property.enumNames[i])
                : "";

            if (IsTierOptionMatch(displayName, wantedRoman, wantedNumber, wantedRawRoman, wantedRawNumber))
                return i;

            if (IsTierOptionMatch(enumName, wantedRoman, wantedNumber, wantedRawRoman, wantedRawNumber))
                return i;
        }

        return -1;
    }

    private static bool IsTierOptionMatch(
        string option,
        string wantedRoman,
        string wantedNumber,
        string wantedRawRoman,
        string wantedRawNumber)
    {
        if (string.IsNullOrEmpty(option))
            return false;

        if (option == wantedRoman || option.EndsWith(wantedRoman))
            return true;

        if (option == wantedNumber || option.EndsWith(wantedNumber) || option.Contains(wantedNumber))
            return true;

        if (option == wantedRawRoman)
            return true;

        if (option == wantedRawNumber)
            return true;

        return false;
    }

    private static int GetTierFallbackEnumIndex(SerializedProperty property, int tier)
    {
        bool firstLooksLikeNone = property.enumDisplayNames.Length > 0 &&
                                  ContainsCI(property.enumDisplayNames[0], "None");

        int index = firstLooksLikeNone ? tier : tier - 1;

        return Mathf.Clamp(index, 0, property.enumDisplayNames.Length - 1);
    }

    private static SerializedProperty FindProperty(
        SerializedObject serializedObject,
        params string[] aliases)
    {
        if (serializedObject == null || aliases == null || aliases.Length == 0)
            return null;

        HashSet<string> normalizedAliases = new HashSet<string>(
            aliases
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(NormalizeForSearch)
                .Where(a => !string.IsNullOrEmpty(a))
        );

        List<SerializedProperty> candidates = new List<SerializedProperty>();

        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = true;

            if (iterator.name == "m_Script")
                continue;

            candidates.Add(iterator.Copy());
        }

        foreach (SerializedProperty candidate in candidates)
        {
            string propertyName = NormalizeForSearch(candidate.name);
            string displayName = NormalizeForSearch(candidate.displayName);

            if (normalizedAliases.Contains(propertyName) || normalizedAliases.Contains(displayName))
                return candidate.Copy();
        }

        foreach (SerializedProperty candidate in candidates)
        {
            string propertyName = NormalizeForSearch(candidate.name);
            string displayName = NormalizeForSearch(candidate.displayName);

            foreach (string alias in normalizedAliases)
            {
                if (propertyName.Contains(alias) || displayName.Contains(alias))
                    return candidate.Copy();
            }
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
        string[] guids = roots == null
            ? AssetDatabase.FindAssets(assetName)
            : AssetDatabase.FindAssets(assetName, roots);

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
            T typedAsset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (typedAsset != null)
                return typedAsset;

            Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path);

            if (mainAsset is T)
                return (T)mainAsset;
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

    private static string CombineAssetPath(string a, string b)
    {
        return a.TrimEnd('/') + "/" + b.TrimStart('/');
    }

    private static string NormalizeForSearch(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        value = value
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
            .Replace("ñ", "n")
            .Replace("Ñ", "n");

        char[] chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();

        return new string(chars);
    }

    private static bool ContainsCI(string source, string value)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
            return false;

        return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ToRoman(int value)
    {
        switch (value)
        {
            case 1:
                return "I";
            case 2:
                return "II";
            case 3:
                return "III";
            case 4:
                return "IV";
            case 5:
                return "V";
            default:
                return value.ToString();
        }
    }

    private sealed class OreConfig
    {
        public readonly string AssetName;
        public readonly string PrefabMineralName;
        public readonly string OreDefinitionAssetName;
        public readonly string DisplayName;
        public readonly string OreId;

        public readonly int RequiredMiningTier;
        public readonly int BaseMiningDurability;
        public readonly int BaseDropCountMin;
        public readonly int BaseDropCountMax;

        public readonly float BaseRespawnTime;
        public readonly float MinPurityPercent;
        public readonly float MaxPurityPercent;
        public readonly float MinSizeScale;
        public readonly float MaxSizeScale;

        public readonly float MinCreditValue;
        public readonly float MaxCreditValue;
        public readonly float PurityCreditContribution;
        public readonly float SizeCreditContribution;

        public readonly float MinWeightValue;
        public readonly float MaxWeightValue;
        public readonly float PurityWeightContribution;
        public readonly float SizeWeightContribution;

        public OreConfig(
            string assetName,
            string displayName,
            string oreId,
            int tier,
            int durability,
            int dropMin,
            int dropMax,
            float respawn,
            float purityMin,
            float purityMax,
            float sizeMin,
            float sizeMax,
            float creditMin,
            float creditMax,
            float weightMin,
            float weightMax)
        {
            AssetName = assetName;
            PrefabMineralName = assetName;
            OreDefinitionAssetName = "OD_" + assetName;
            DisplayName = displayName;
            OreId = oreId;

            RequiredMiningTier = tier;
            BaseMiningDurability = durability;
            BaseDropCountMin = dropMin;
            BaseDropCountMax = dropMax;

            BaseRespawnTime = respawn;
            MinPurityPercent = purityMin;
            MaxPurityPercent = purityMax;
            MinSizeScale = sizeMin;
            MaxSizeScale = sizeMax;

            MinCreditValue = creditMin;
            MaxCreditValue = creditMax;
            PurityCreditContribution = 0.5f;
            SizeCreditContribution = 0.5f;

            MinWeightValue = weightMin;
            MaxWeightValue = weightMax;
            PurityWeightContribution = 0.5f;
            SizeWeightContribution = 0.5f;
        }
    }

    private sealed class PropertyEditContext
    {
        private readonly string assetName;
        private readonly StringBuilder report;

        public int Warnings;
        public int Errors;

        public PropertyEditContext(string assetName, StringBuilder report)
        {
            this.assetName = assetName;
            this.report = report;
        }

        public void Warn(string message)
        {
            Warnings++;
            report.AppendLine($"WARN  {assetName}: {message}");
        }

        public void Error(string message)
        {
            Errors++;
            report.AppendLine($"ERROR {assetName}: {message}");
        }
    }

    private sealed class RunStats
    {
        public int Configured;
        public int Warnings;
        public int Errors;
    }
}
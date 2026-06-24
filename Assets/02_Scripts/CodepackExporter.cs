// Assets/Editor/CodepackExporter.cs
// Unity Editor tool to export C# scripts into ChatGPT-friendly text bundles
// and, optionally, copy the original .cs files to a Desktop folder.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public class CodepackExporter : EditorWindow
{
    private DefaultAsset scriptsFolder; // Reference to a folder inside Assets

    private bool generateInfoBundle = true;
    private bool includeLineNumbers = false;

    // Copies the original .cs files, preserving the selected folder structure.
    private bool copyOriginalScripts = false;
    private bool overwriteExistingCopiedScripts = false;

    // Safe default chunk size to avoid massive single files.
    private int maxCharsPerChunk = 250_000;

    private string outputFolderName = "UNITY_CODEPACK";
    private string copiedScriptsFolderName = "COPIED_SCRIPTS";

    [MenuItem("Tools/Codepack/Export Scripts (Assets/Scripts) to Desktop")]
    public static void ExportDefault()
    {
        // One-click default export: keeps old behaviour, only generates info bundle.
        ExportFromFolder(
            folderAssetPath: "Assets/Scripts",
            generateInfoBundle: true,
            includeLineNumbers: false,
            maxCharsPerChunk: 250_000,
            outputFolderName: "UNITY_CODEPACK",
            copyOriginalScripts: false,
            copiedScriptsFolderName: "COPIED_SCRIPTS",
            overwriteExistingCopiedScripts: false
        );
    }

    [MenuItem("Tools/Codepack/Exporter Window")]
    public static void OpenWindow()
    {
        var w = GetWindow<CodepackExporter>("Codepack Exporter");
        w.minSize = new Vector2(560, 380);
        w.InitializeDefaults();
        w.Show();
    }

    private void InitializeDefaults()
    {
        if (scriptsFolder == null)
        {
            scriptsFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Scripts");
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Unity Codepack Exporter", EditorStyles.boldLabel);
        EditorGUILayout.Space(6);

        EditorGUILayout.HelpBox(
            "Exports all .cs files under the selected folder.\n\n" +
            "You can generate INDEX/TREE/CODE files for review and/or copy the original scripts to a Desktop folder.\n" +
            "The script copy preserves the folder structure from the selected source folder.",
            MessageType.Info);

        EditorGUILayout.Space(6);

        scriptsFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            new GUIContent("Scripts Folder", "Folder inside Assets to scan, e.g. Assets/Scripts"),
            scriptsFolder,
            typeof(DefaultAsset),
            false);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

        outputFolderName = EditorGUILayout.TextField(
            new GUIContent("Desktop Folder Name", "Folder name created on Desktop"),
            outputFolderName);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Info Export", EditorStyles.boldLabel);

        generateInfoBundle = EditorGUILayout.Toggle(
            new GUIContent("Generate Info Bundle", "Creates TREE.txt, INDEX.md and CODE_###.md files"),
            generateInfoBundle);

        using (new EditorGUI.DisabledScope(!generateInfoBundle))
        {
            includeLineNumbers = EditorGUILayout.Toggle(
                new GUIContent("Include Line Numbers", "Prefixes each line with ####| for precise references"),
                includeLineNumbers);

            maxCharsPerChunk = EditorGUILayout.IntField(
                new GUIContent("Max Chars Per Chunk", "Maximum characters per CODE_###.md file"),
                Mathf.Max(50_000, maxCharsPerChunk));
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Script Copy", EditorStyles.boldLabel);

        copyOriginalScripts = EditorGUILayout.Toggle(
            new GUIContent("Copy Original .cs Scripts", "Copies the actual .cs files to a Desktop subfolder"),
            copyOriginalScripts);

        using (new EditorGUI.DisabledScope(!copyOriginalScripts))
        {
            copiedScriptsFolderName = EditorGUILayout.TextField(
                new GUIContent("Copy Subfolder Name", "Subfolder inside the Desktop output folder"),
                copiedScriptsFolderName);

            overwriteExistingCopiedScripts = EditorGUILayout.Toggle(
                new GUIContent("Overwrite Existing Copies", "OFF is safer: existing files are skipped instead of overwritten"),
                overwriteExistingCopiedScripts);
        }

        EditorGUILayout.Space(8);

        using (new EditorGUI.DisabledScope(!generateInfoBundle && !copyOriginalScripts))
        {
            if (GUILayout.Button("Export to Desktop", GUILayout.Height(34)))
            {
                string folderPath = scriptsFolder ? AssetDatabase.GetAssetPath(scriptsFolder) : "Assets/Scripts";

                ExportFromFolder(
                    folderAssetPath: folderPath,
                    generateInfoBundle: generateInfoBundle,
                    includeLineNumbers: includeLineNumbers,
                    maxCharsPerChunk: maxCharsPerChunk,
                    outputFolderName: outputFolderName,
                    copyOriginalScripts: copyOriginalScripts,
                    copiedScriptsFolderName: copiedScriptsFolderName,
                    overwriteExistingCopiedScripts: overwriteExistingCopiedScripts
                );
            }
        }

        if (!generateInfoBundle && !copyOriginalScripts)
        {
            EditorGUILayout.HelpBox("Enable at least one export option.", MessageType.Warning);
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Safety:", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Copy mode never deletes source files. With overwrite OFF, existing copies are skipped.");
    }

    private static void ExportFromFolder(
        string folderAssetPath,
        bool generateInfoBundle,
        bool includeLineNumbers,
        int maxCharsPerChunk,
        string outputFolderName,
        bool copyOriginalScripts,
        string copiedScriptsFolderName,
        bool overwriteExistingCopiedScripts)
    {
        if (!generateInfoBundle && !copyOriginalScripts)
        {
            EditorUtility.DisplayDialog("Codepack Export", "Enable at least one export option.", "OK");
            return;
        }

        folderAssetPath = NormalizeAssetPath(folderAssetPath);

        if (string.IsNullOrWhiteSpace(folderAssetPath) || !folderAssetPath.StartsWith("Assets", StringComparison.Ordinal))
        {
            EditorUtility.DisplayDialog("Codepack Export", "Folder must be under Assets/ . Example: Assets/Scripts", "OK");
            return;
        }

        if (!AssetDatabase.IsValidFolder(folderAssetPath))
        {
            EditorUtility.DisplayDialog("Codepack Export", $"Selected path is not a valid Assets folder:\n{folderAssetPath}", "OK");
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? "";
        if (string.IsNullOrEmpty(projectRoot))
        {
            EditorUtility.DisplayDialog("Codepack Export", "Could not resolve project root.", "OK");
            return;
        }

        string folderAbsolute = Path.GetFullPath(Path.Combine(projectRoot, folderAssetPath));
        if (!Directory.Exists(folderAbsolute))
        {
            EditorUtility.DisplayDialog("Codepack Export", $"Folder does not exist:\n{folderAssetPath}", "OK");
            return;
        }

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
        {
            EditorUtility.DisplayDialog("Codepack Export", "Could not resolve Desktop folder.", "OK");
            return;
        }

        string safeOutputFolderName = SanitizeFolderName(outputFolderName, "UNITY_CODEPACK");
        string safeCopyFolderName = SanitizeFolderName(copiedScriptsFolderName, "COPIED_SCRIPTS");

        string outputDir = Path.GetFullPath(Path.Combine(desktop, safeOutputFolderName));
        Directory.CreateDirectory(outputDir);

        try
        {
            var files = Directory.GetFiles(folderAbsolute, "*.cs", SearchOption.AllDirectories)
                                 .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                 .ToList();

            if (files.Count == 0)
            {
                EditorUtility.DisplayDialog("Codepack Export", "No .cs files found under the selected folder.", "OK");
                return;
            }

            var generated = new List<string>();

            if (generateInfoBundle)
            {
                WriteTreeTxt(outputDir, files, projectRoot);
                WriteIndexMd(outputDir, files, projectRoot);
                WriteCodeChunks(outputDir, files, projectRoot, includeLineNumbers, Mathf.Max(50_000, maxCharsPerChunk));

                generated.Add("TREE.txt");
                generated.Add("INDEX.md");
                generated.Add("CODE_###.md");
            }

            CopyReport copyReport = null;
            if (copyOriginalScripts)
            {
                copyReport = CopyOriginalScriptsToFolder(
                    outputDir: outputDir,
                    sourceFolderAbsolute: folderAbsolute,
                    files: files,
                    copySubfolderName: safeCopyFolderName,
                    overwriteExistingCopiedScripts: overwriteExistingCopiedScripts);

                WriteCopyReportMd(outputDir, copyReport);
                generated.Add(copyReport.CopyRootDisplayName + "/");
                generated.Add("COPY_REPORT.md");
            }

            AssetDatabase.Refresh();

            string message = BuildSuccessMessage(outputDir, files.Count, generated, copyReport);
            EditorUtility.DisplayDialog("Codepack Export", message, "OK");
            EditorUtility.RevealInFinder(outputDir);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Codepack Export", $"Export failed:\n{ex.Message}", "OK");
        }
    }

    private static CopyReport CopyOriginalScriptsToFolder(
        string outputDir,
        string sourceFolderAbsolute,
        List<string> files,
        string copySubfolderName,
        bool overwriteExistingCopiedScripts)
    {
        string copyRoot = Path.Combine(outputDir, copySubfolderName);
        Directory.CreateDirectory(copyRoot);

        var report = new CopyReport
        {
            CopyRootAbsolute = copyRoot,
            CopyRootDisplayName = copySubfolderName,
            SourceRootAbsolute = sourceFolderAbsolute,
            OverwriteEnabled = overwriteExistingCopiedScripts
        };

        foreach (string sourceFile in files)
        {
            string relativeFromSelectedFolder = ToRelativePath(sourceFile, sourceFolderAbsolute);
            string destinationFile = Path.Combine(copyRoot, relativeFromSelectedFolder.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? copyRoot);

                if (File.Exists(destinationFile) && !overwriteExistingCopiedScripts)
                {
                    report.SkippedExisting.Add(relativeFromSelectedFolder);
                    continue;
                }

                bool existedBefore = File.Exists(destinationFile);
                File.Copy(sourceFile, destinationFile, overwriteExistingCopiedScripts);

                if (existedBefore)
                    report.Overwritten.Add(relativeFromSelectedFolder);
                else
                    report.Copied.Add(relativeFromSelectedFolder);
            }
            catch (Exception ex)
            {
                report.Errors.Add($"{relativeFromSelectedFolder} -> {ex.Message}");
            }
        }

        return report;
    }

    private static void WriteCopyReportMd(string outputDir, CopyReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# COPY REPORT");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- Source: `{report.SourceRootAbsolute}`");
        sb.AppendLine($"- Destination: `{report.CopyRootAbsolute}`");
        sb.AppendLine($"- Overwrite enabled: `{report.OverwriteEnabled}`");
        sb.AppendLine($"- Copied: {report.Copied.Count}");
        sb.AppendLine($"- Overwritten: {report.Overwritten.Count}");
        sb.AppendLine($"- Skipped existing: {report.SkippedExisting.Count}");
        sb.AppendLine($"- Errors: {report.Errors.Count}");
        sb.AppendLine();

        AppendList(sb, "Copied", report.Copied);
        AppendList(sb, "Overwritten", report.Overwritten);
        AppendList(sb, "Skipped Existing", report.SkippedExisting);
        AppendList(sb, "Errors", report.Errors);

        File.WriteAllText(Path.Combine(outputDir, "COPY_REPORT.md"), sb.ToString(), new UTF8Encoding(false));
    }

    private static void AppendList(StringBuilder sb, string title, List<string> values)
    {
        if (values.Count == 0)
            return;

        sb.AppendLine($"## {title}");
        sb.AppendLine();

        foreach (string value in values)
        {
            sb.AppendLine($"- `{value}`");
        }

        sb.AppendLine();
    }

    private static string BuildSuccessMessage(string outputDir, int sourceFileCount, List<string> generated, CopyReport copyReport)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Export completed.");
        sb.AppendLine();
        sb.AppendLine($"Source .cs files found: {sourceFileCount}");
        sb.AppendLine();
        sb.AppendLine("Output:");
        sb.AppendLine(outputDir);
        sb.AppendLine();
        sb.AppendLine("Generated:");

        foreach (string item in generated)
        {
            sb.AppendLine($"- {item}");
        }

        if (copyReport != null)
        {
            sb.AppendLine();
            sb.AppendLine("Copy summary:");
            sb.AppendLine($"- Copied: {copyReport.Copied.Count}");
            sb.AppendLine($"- Overwritten: {copyReport.Overwritten.Count}");
            sb.AppendLine($"- Skipped existing: {copyReport.SkippedExisting.Count}");
            sb.AppendLine($"- Errors: {copyReport.Errors.Count}");
        }

        return sb.ToString();
    }

    private static void WriteTreeTxt(string outputDir, List<string> files, string projectRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# TREE");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        foreach (var f in files)
        {
            string rel = ToProjectRelativePath(f, projectRoot);
            sb.AppendLine(rel);
        }

        File.WriteAllText(Path.Combine(outputDir, "TREE.txt"), sb.ToString(), new UTF8Encoding(false));
    }

    private static void WriteIndexMd(string outputDir, List<string> files, string projectRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# INDEX");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- File count: {files.Count}");
        sb.AppendLine();
        sb.AppendLine("## Files");
        sb.AppendLine();

        foreach (var f in files)
        {
            string rel = ToProjectRelativePath(f, projectRoot);
            sb.AppendLine($"- `{rel}`");
        }

        File.WriteAllText(Path.Combine(outputDir, "INDEX.md"), sb.ToString(), new UTF8Encoding(false));
    }

    private static void WriteCodeChunks(string outputDir, List<string> files, string projectRoot, bool includeLineNumbers, int maxCharsPerChunk)
    {
        int chunkIndex = 1;
        int currentLen = 0;
        var sb = new StringBuilder();

        void Flush()
        {
            if (sb.Length == 0)
                return;

            string name = $"CODE_{chunkIndex:000}.md";
            File.WriteAllText(Path.Combine(outputDir, name), sb.ToString(), new UTF8Encoding(false));
            chunkIndex++;
            sb.Clear();
            currentLen = 0;
        }

        foreach (var f in files)
        {
            string rel = ToProjectRelativePath(f, projectRoot);
            string content = SafeReadAllText(f);

            if (includeLineNumbers)
                content = AddLineNumbers(content);

            string header =
                "\n" +
                "============================================================\n" +
                $"FILE: {rel}\n" +
                "============================================================\n";

            string block = header + content + (content.EndsWith("\n", StringComparison.Ordinal) ? "" : "\n");

            if (currentLen + block.Length > maxCharsPerChunk && sb.Length > 0)
                Flush();

            sb.Append(block);
            currentLen += block.Length;
        }

        Flush();
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return (assetPath ?? string.Empty).Replace('\\', '/').Trim().TrimEnd('/');
    }

    private static string SanitizeFolderName(string folderName, string fallback)
    {
        string value = string.IsNullOrWhiteSpace(folderName) ? fallback : folderName.Trim();

        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');

        value = value.Replace('/', '_').Replace('\\', '_').Trim();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string ToProjectRelativePath(string absoluteFilePath, string projectRoot)
    {
        return ToRelativePath(absoluteFilePath, projectRoot);
    }

    private static string ToRelativePath(string absoluteFilePath, string rootAbsolutePath)
    {
        string fullFile = Path.GetFullPath(absoluteFilePath);
        string fullRoot = Path.GetFullPath(rootAbsolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string rootWithSeparator = fullRoot + Path.DirectorySeparatorChar;

        if (fullFile.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return fullFile.Substring(rootWithSeparator.Length).Replace('\\', '/');
        }

        return Path.GetFileName(fullFile);
    }

    private static string SafeReadAllText(string path)
    {
        // Attempt UTF-8 first, then fallback to default encoding.
        try
        {
            return File.ReadAllText(path, new UTF8Encoding(false, true));
        }
        catch
        {
            return File.ReadAllText(path);
        }
    }

    private static string AddLineNumbers(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        int width = Math.Max(4, lines.Length.ToString().Length);

        var sb = new StringBuilder(text.Length + lines.Length * (width + 3));
        for (int i = 0; i < lines.Length; i++)
        {
            string n = (i + 1).ToString().PadLeft(width, '0');
            sb.Append(n).Append("| ").AppendLine(lines[i]);
        }

        return sb.ToString();
    }

    private class CopyReport
    {
        public string CopyRootAbsolute;
        public string CopyRootDisplayName;
        public string SourceRootAbsolute;
        public bool OverwriteEnabled;
        public readonly List<string> Copied = new List<string>();
        public readonly List<string> Overwritten = new List<string>();
        public readonly List<string> SkippedExisting = new List<string>();
        public readonly List<string> Errors = new List<string>();
    }
}
#endif

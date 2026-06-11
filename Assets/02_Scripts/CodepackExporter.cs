// Assets/Editor/CodepackExporter.cs
// Unity Editor tool to export C# scripts into ChatGPT-friendly text bundles.

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
    private DefaultAsset scriptsFolder; // reference to a folder inside Assets
    private bool includeLineNumbers = false;

    // Safe default chunk size to avoid massive single files
    private int maxCharsPerChunk = 250_000;

    private string outputFolderAbsolute; // e.g., Desktop\UNITY_CODEPACK
    private string outputFolderName = "UNITY_CODEPACK";

    [MenuItem("Tools/Codepack/Export Scripts (Assets/Scripts) to Desktop")]
    public static void ExportDefault()
    {
        // One-click default export
        ExportFromFolder("Assets/Scripts", includeLineNumbers: false, maxCharsPerChunk: 250_000, outputFolderName: "UNITY_CODEPACK");
    }

    [MenuItem("Tools/Codepack/Exporter Window")]
    public static void OpenWindow()
    {
        var w = GetWindow<CodepackExporter>("Codepack Exporter");
        w.minSize = new Vector2(520, 260);
        w.InitializeDefaults();
        w.Show();
    }

    private void InitializeDefaults()
    {
        if (scriptsFolder == null)
        {
            scriptsFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Scripts");
        }

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        outputFolderAbsolute = Path.Combine(desktop, outputFolderName);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Unity Codepack Exporter", EditorStyles.boldLabel);
        EditorGUILayout.Space(6);

        EditorGUILayout.HelpBox(
            "Exports all .cs files under the selected folder into INDEX + chunked CODE files.\n" +
            "Recommended: select Assets/Scripts.\n\n" +
            "Output defaults to Desktop/UNITY_CODEPACK.",
            MessageType.Info);

        EditorGUILayout.Space(6);

        scriptsFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            new GUIContent("Scripts Folder (inside Assets)", "Folder to scan, e.g. Assets/Scripts"),
            scriptsFolder, typeof(DefaultAsset), false);

        includeLineNumbers = EditorGUILayout.Toggle(
            new GUIContent("Include Line Numbers", "Prefixes each line with ####| for precise references"),
            includeLineNumbers);

        maxCharsPerChunk = EditorGUILayout.IntField(
            new GUIContent("Max Chars Per Chunk", "Maximum characters per CODE_###.md file"),
            Mathf.Max(50_000, maxCharsPerChunk));

        EditorGUILayout.Space(6);

        outputFolderName = EditorGUILayout.TextField(
            new GUIContent("Output Folder Name", "Folder name created on Desktop"),
            outputFolderName);

        if (GUILayout.Button("Export to Desktop", GUILayout.Height(32)))
        {
            string folderPath = scriptsFolder ? AssetDatabase.GetAssetPath(scriptsFolder) : "Assets/Scripts";
            ExportFromFolder(folderPath, includeLineNumbers, maxCharsPerChunk, outputFolderName);
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Tip:", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("If you change code later, re-run export and re-send updated CODE_### files.");
    }

    private static void ExportFromFolder(string folderAssetPath, bool includeLineNumbers, int maxCharsPerChunk, string outputFolderName)
    {
        if (string.IsNullOrWhiteSpace(folderAssetPath) || !folderAssetPath.StartsWith("Assets"))
        {
            EditorUtility.DisplayDialog("Codepack Export", "Folder must be under Assets/ (e.g. Assets/Scripts).", "OK");
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? "";
        if (string.IsNullOrEmpty(projectRoot))
        {
            EditorUtility.DisplayDialog("Codepack Export", "Could not resolve project root.", "OK");
            return;
        }

        string folderAbsolute = Path.Combine(projectRoot, folderAssetPath);
        if (!Directory.Exists(folderAbsolute))
        {
            EditorUtility.DisplayDialog("Codepack Export", $"Folder does not exist:\n{folderAssetPath}", "OK");
            return;
        }

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string outputDir = Path.Combine(desktop, outputFolderName);

        Directory.CreateDirectory(outputDir);

        try
        {
            // Collect .cs files
            var files = Directory.GetFiles(folderAbsolute, "*.cs", SearchOption.AllDirectories)
                                 .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                 .ToList();

            if (files.Count == 0)
            {
                EditorUtility.DisplayDialog("Codepack Export", "No .cs files found under the selected folder.", "OK");
                return;
            }

            // Build TREE.txt
            WriteTreeTxt(outputDir, files, projectRoot);

            // Build INDEX.md
            WriteIndexMd(outputDir, files, projectRoot);

            // Build CODE_###.md chunks
            WriteCodeChunks(outputDir, files, projectRoot, includeLineNumbers, maxCharsPerChunk);

            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Codepack Export",
                $"Export completed.\n\nOutput:\n{outputDir}\n\nFiles:\nTREE.txt\nINDEX.md\nCODE_###.md",
                "OK");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Codepack Export", $"Export failed:\n{ex.Message}", "OK");
        }
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

        File.WriteAllText(Path.Combine(outputDir, "TREE.txt"), sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
            if (sb.Length == 0) return;

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

            string block = header + content + (content.EndsWith("\n") ? "" : "\n");

            if (currentLen + block.Length > maxCharsPerChunk && sb.Length > 0)
                Flush();

            sb.Append(block);
            currentLen += block.Length;
        }

        Flush();
    }

    private static string ToProjectRelativePath(string absoluteFilePath, string projectRoot)
    {
        string rel = absoluteFilePath.Replace(projectRoot, "").TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Normalize to forward slashes for consistency
        return rel.Replace('\\', '/');
    }

    private static string SafeReadAllText(string path)
    {
        // Attempt UTF-8 first, then fallback to default
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
}
#endif

//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Reflection;
//using UnityEditor;
//using UnityEditor.ShortcutManagement;
//using UnityEngine;

//public class BottomProjectDrawer : EditorWindow
//{
//    private const string ShortcutId = "Custom/Open Bottom Project Drawer";

//    private const float TargetHeight = 360f;
//    private const float Margin = 10f;
//    private const float AnimationSpeed = 14f;

//    private const float ToolbarHeight = 30f;
//    private const float FooterHeight = 22f;
//    private const float LeftPanelWidth = 240f;
//    private const float TileSize = 72f;
//    private const float TilePadding = 12f;

//    private static BottomProjectDrawer instance;

//    private bool isOpening;
//    private bool isClosing;
//    private float currentHeight = 1f;
//    private double lastTime;

//    private Vector2 folderScroll;
//    private Vector2 contentScroll;

//    private string currentFolder = "Assets";
//    private string selectedPath = "";
//    private string searchText = "";

//    private bool showPackages = false;

//    private readonly List<string> visibleEntries = new();
//    private readonly HashSet<string> expandedFolders = new();

//    [Shortcut(ShortcutId, KeyCode.Space, ShortcutModifiers.Action)]
//    private static void ToggleDrawer()
//    {
//        if (instance != null)
//        {
//            instance.ToggleClose();
//            return;
//        }

//        OpenDrawer();
//    }

//    [MenuItem("Tools/Bottom Project Drawer/Toggle")]
//    private static void ToggleDrawerMenu()
//    {
//        ToggleDrawer();
//    }

//    private static void OpenDrawer()
//    {
//        instance = CreateInstance<BottomProjectDrawer>();
//        instance.titleContent = new GUIContent("Project Drawer");
//        instance.currentFolder = "Assets";
//        instance.selectedPath = "";
//        instance.searchText = "";
//        instance.currentHeight = 1f;
//        instance.isOpening = true;
//        instance.isClosing = false;
//        instance.lastTime = EditorApplication.timeSinceStartup;

//        Rect editorRect = GetEditorMainWindowRect();
//        float width = Mathf.Max(900f, editorRect.width - Margin * 2f);
//        float x = editorRect.x + (editorRect.width - width) * 0.5f;
//        float y = editorRect.yMax - Margin;

//        instance.position = new Rect(x, y, width, 1f);
//        instance.ShowPopup();
//        instance.Focus();
//        instance.RebuildVisibleEntries();
//    }

//    private void OnEnable()
//    {
//        instance = this;
//        wantsMouseMove = true;
//        EditorApplication.update += EditorUpdate;

//        if (!expandedFolders.Contains("Assets"))
//            expandedFolders.Add("Assets");

//        if (!string.IsNullOrEmpty(currentFolder))
//            ExpandParents(currentFolder);
//    }

//    private void OnDisable()
//    {
//        EditorApplication.update -= EditorUpdate;

//        if (instance == this)
//            instance = null;
//    }

//    private void OnLostFocus()
//    {
//        if (!isClosing)
//            BeginClose();
//    }

//    private void EditorUpdate()
//    {
//        double now = EditorApplication.timeSinceStartup;
//        float dt = (float)(now - lastTime);
//        lastTime = now;

//        if (isOpening)
//        {
//            float t = 1f - Mathf.Exp(-AnimationSpeed * dt);
//            currentHeight = Mathf.Lerp(currentHeight, TargetHeight, t);

//            if (Mathf.Abs(currentHeight - TargetHeight) < 0.5f)
//            {
//                currentHeight = TargetHeight;
//                isOpening = false;
//            }

//            ApplyWindowRect();
//            Repaint();
//        }
//        else if (isClosing)
//        {
//            float t = 1f - Mathf.Exp(-AnimationSpeed * dt);
//            currentHeight = Mathf.Lerp(currentHeight, 1f, t);

//            if (currentHeight <= 2f)
//            {
//                isClosing = false;
//                Close();
//                return;
//            }

//            ApplyWindowRect();
//            Repaint();
//        }
//    }

//    private void ApplyWindowRect()
//    {
//        Rect editorRect = GetEditorMainWindowRect();
//        float width = Mathf.Max(900f, editorRect.width - Margin * 2f);
//        float x = editorRect.x + (editorRect.width - width) * 0.5f;
//        float y = editorRect.yMax - Margin - currentHeight;

//        position = new Rect(x, y, width, currentHeight);
//    }

//    private void ToggleClose()
//    {
//        if (isClosing)
//        {
//            isClosing = false;
//            isOpening = true;
//            lastTime = EditorApplication.timeSinceStartup;
//            Focus();
//            return;
//        }

//        BeginClose();
//    }

//    private void BeginClose()
//    {
//        isOpening = false;
//        isClosing = true;
//        lastTime = EditorApplication.timeSinceStartup;
//    }

//    private void OnGUI()
//    {
//        DrawWindowBackground();
//        ConsumeWindowEvents();
//        HandleKeyboard();

//        DrawToolbar();
//        DrawBody();
//        DrawFooter();
//    }

//    private void DrawWindowBackground()
//    {
//        Rect full = new Rect(0, 0, position.width, position.height);
//        EditorGUI.DrawRect(full, EditorGUIUtility.isProSkin
//            ? new Color(0.22f, 0.22f, 0.22f, 1f)
//            : new Color(0.76f, 0.76f, 0.76f, 1f));
//    }

//    private void ConsumeWindowEvents()
//    {
//        Event e = Event.current;
//        Rect full = new Rect(0, 0, position.width, position.height);

//        if ((e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ScrollWheel) && full.Contains(e.mousePosition))
//        {
//            Focus();
//        }
//    }

//    private void DrawToolbar()
//    {
//        Rect toolbarRect = new Rect(0, 0, position.width, ToolbarHeight);

//        GUILayout.BeginArea(toolbarRect, EditorStyles.toolbar);
//        GUILayout.BeginHorizontal(EditorStyles.toolbar);

//        GUILayout.Label("Project", GUILayout.Width(50));

//        if (GUILayout.Button("Assets", EditorStyles.toolbarButton, GUILayout.Width(55)))
//        {
//            showPackages = false;
//            if (!AssetDatabase.IsValidFolder(currentFolder) || currentFolder.StartsWith("Packages"))
//                currentFolder = "Assets";

//            ExpandParents(currentFolder);
//            RebuildVisibleEntries();
//        }

//        if (GUILayout.Button("Packages", EditorStyles.toolbarButton, GUILayout.Width(70)))
//        {
//            showPackages = true;
//            if (!currentFolder.StartsWith("Packages"))
//                currentFolder = "Packages";

//            RebuildVisibleEntries();
//        }

//        GUILayout.Space(8);

//        DrawBreadcrumbs();

//        GUILayout.FlexibleSpace();

//        GUI.SetNextControlName("ProjectDrawerSearch");
//        string newSearch = GUILayout.TextField(
//            searchText,
//            GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarSearchField,
//            GUILayout.Width(220)
//        );

//        if (newSearch != searchText)
//        {
//            searchText = newSearch;
//            RebuildVisibleEntries();
//        }

//        if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSeachCancelButton") ?? EditorStyles.toolbarButton, GUILayout.Width(20)))
//        {
//            if (!string.IsNullOrEmpty(searchText))
//            {
//                searchText = "";
//                GUI.FocusControl(null);
//                RebuildVisibleEntries();
//            }
//        }

//        if (GUILayout.Button("Close", EditorStyles.toolbarButton, GUILayout.Width(50)))
//        {
//            BeginClose();
//        }

//        GUILayout.EndHorizontal();
//        GUILayout.EndArea();
//    }

//    private void DrawBreadcrumbs()
//    {
//        var parts = currentFolder.Split('/');

//        string accum = parts[0];
//        if (GUILayout.Button(parts[0], EditorStyles.toolbarButton, GUILayout.Width(Mathf.Max(55, parts[0].Length * 8))))
//        {
//            OpenFolder(parts[0]);
//        }

//        for (int i = 1; i < parts.Length; i++)
//        {
//            GUILayout.Label(">", GUILayout.Width(10));
//            accum += "/" + parts[i];

//            if (GUILayout.Button(parts[i], EditorStyles.toolbarButton, GUILayout.Width(Mathf.Max(50, parts[i].Length * 8))))
//            {
//                OpenFolder(accum);
//            }
//        }
//    }

//    private void DrawBody()
//    {
//        float bodyY = ToolbarHeight;
//        float bodyHeight = position.height - ToolbarHeight - FooterHeight;

//        Rect leftRect = new Rect(0, bodyY, LeftPanelWidth, bodyHeight);
//        Rect rightRect = new Rect(LeftPanelWidth + 1, bodyY, position.width - LeftPanelWidth - 1, bodyHeight);

//        EditorGUI.DrawRect(leftRect, EditorGUIUtility.isProSkin
//            ? new Color(0.20f, 0.20f, 0.20f, 1f)
//            : new Color(0.82f, 0.82f, 0.82f, 1f));

//        EditorGUI.DrawRect(new Rect(LeftPanelWidth, bodyY, 1, bodyHeight), new Color(0, 0, 0, 0.35f));

//        EditorGUI.DrawRect(rightRect, EditorGUIUtility.isProSkin
//            ? new Color(0.23f, 0.23f, 0.23f, 1f)
//            : new Color(0.87f, 0.87f, 0.87f, 1f));

//        DrawFolderPanel(leftRect);
//        DrawContentPanel(rightRect);
//    }

//    private void DrawFolderPanel(Rect rect)
//    {
//        GUILayout.BeginArea(rect);
//        folderScroll = GUILayout.BeginScrollView(folderScroll);

//        if (!showPackages)
//        {
//            DrawFolderNode("Assets", 0);
//        }
//        else
//        {
//            DrawPackagesRoot();
//        }

//        GUILayout.EndScrollView();
//        GUILayout.EndArea();
//    }

//    private void DrawPackagesRoot()
//    {
//        Rect rowRect = GUILayoutUtility.GetRect(10f, 20f, GUILayout.ExpandWidth(true));
//        bool selected = currentFolder == "Packages";

//        if (selected)
//            EditorGUI.DrawRect(rowRect, new Color(0.24f, 0.49f, 0.90f, 0.35f));

//        if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
//        {
//            currentFolder = "Packages";
//            selectedPath = "Packages";
//            RebuildVisibleEntries();
//            Event.current.Use();
//        }

//        GUI.Label(new Rect(rowRect.x + 6, rowRect.y + 1, rowRect.width - 6, rowRect.height), "Packages", EditorStyles.label);
//    }

//    private void DrawFolderNode(string folderPath, int indent)
//    {
//        if (!AssetDatabase.IsValidFolder(folderPath))
//            return;

//        string folderName = folderPath == "Assets" ? "Assets" : System.IO.Path.GetFileName(folderPath);
//        bool hasChildren = AssetDatabase.GetSubFolders(folderPath).Length > 0;
//        bool expanded = expandedFolders.Contains(folderPath);
//        bool selected = currentFolder == folderPath;

//        Rect rowRect = GUILayoutUtility.GetRect(10f, 20f, GUILayout.ExpandWidth(true));

//        if (selected)
//            EditorGUI.DrawRect(rowRect, new Color(0.24f, 0.49f, 0.90f, 0.35f));

//        Rect foldRect = new Rect(rowRect.x + indent * 14 + 2, rowRect.y + 2, 16, 16);
//        Rect labelRect = new Rect(rowRect.x + indent * 14 + 18, rowRect.y + 1, rowRect.width - indent * 14 - 18, rowRect.height);

//        Event e = Event.current;
//        if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
//        {
//            if (hasChildren && foldRect.Contains(e.mousePosition))
//            {
//                if (expanded)
//                    expandedFolders.Remove(folderPath);
//                else
//                    expandedFolders.Add(folderPath);
//            }
//            else
//            {
//                OpenFolder(folderPath);
//            }

//            e.Use();
//        }

//        if (hasChildren)
//            EditorGUI.Foldout(foldRect, expanded, GUIContent.none);

//        GUI.Label(labelRect, folderName, EditorStyles.label);

//        if (expanded)
//        {
//            var subFolders = AssetDatabase.GetSubFolders(folderPath).OrderBy(p => p).ToArray();
//            foreach (var subFolder in subFolders)
//                DrawFolderNode(subFolder, indent + 1);
//        }
//    }

//    private void DrawContentPanel(Rect rect)
//    {
//        GUILayout.BeginArea(rect);
//        contentScroll = GUILayout.BeginScrollView(contentScroll);

//        var entries = GetEntriesForCurrentView();

//        int columns = Mathf.Max(1, Mathf.FloorToInt((rect.width - 16f) / (TileSize + TilePadding)));
//        int index = 0;

//        while (index < entries.Count)
//        {
//            GUILayout.BeginHorizontal();

//            for (int c = 0; c < columns && index < entries.Count; c++, index++)
//            {
//                DrawAssetTile(entries[index]);
//            }

//            GUILayout.FlexibleSpace();
//            GUILayout.EndHorizontal();
//            GUILayout.Space(4);
//        }

//        if (entries.Count == 0)
//        {
//            GUILayout.Space(20);
//            GUILayout.Label("No items", EditorStyles.centeredGreyMiniLabel);
//        }

//        GUILayout.EndScrollView();
//        GUILayout.EndArea();
//    }

//    private void DrawAssetTile(string path)
//    {
//        bool isFolder = AssetDatabase.IsValidFolder(path);
//        UnityEngine.Object obj = isFolder
//            ? AssetDatabase.LoadAssetAtPath<DefaultAsset>(path)
//            : AssetDatabase.LoadMainAssetAtPath(path);

//        Texture icon = AssetDatabase.GetCachedIcon(path);

//        Rect tileRect = GUILayoutUtility.GetRect(TileSize + 10f, 96f, GUILayout.Width(TileSize + 10f));

//        bool selected = selectedPath == path;
//        if (selected)
//            EditorGUI.DrawRect(tileRect, new Color(0.24f, 0.49f, 0.90f, 0.30f));

//        Event e = Event.current;

//        switch (e.type)
//        {
//            case EventType.MouseDown:
//                {
//                    if (tileRect.Contains(e.mousePosition) && e.button == 0)
//                    {
//                        selectedPath = path;
//                        GUI.FocusControl(null);
//                        Repaint();

//                        if (e.clickCount == 2)
//                        {
//                            if (isFolder)
//                            {
//                                OpenFolder(path);
//                            }
//                            else if (obj != null)
//                            {
//                                OpenAsset(obj);
//                            }
//                        }

//                        e.Use();
//                    }

//                    break;
//                }

//            case EventType.MouseDrag:
//                {
//                    if (tileRect.Contains(e.mousePosition) && e.button == 0 && obj != null)
//                    {
//                        selectedPath = path;

//                        DragAndDrop.PrepareStartDrag();
//                        DragAndDrop.objectReferences = new UnityEngine.Object[] { obj };
//                        DragAndDrop.paths = new string[] { path };
//                        DragAndDrop.StartDrag(obj.name);

//                        e.Use();
//                    }

//                    break;
//                }
//        }

//        Rect iconRect = new Rect(tileRect.x + (tileRect.width - 48f) * 0.5f, tileRect.y + 6f, 48f, 48f);
//        Rect labelRect = new Rect(tileRect.x + 4f, tileRect.y + 58f, tileRect.width - 8f, 34f);

//        if (icon != null)
//            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);

//        GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel)
//        {
//            alignment = TextAnchor.UpperCenter,
//            wordWrap = true,
//            clipping = TextClipping.Clip
//        };

//        GUI.Label(labelRect, System.IO.Path.GetFileName(path), labelStyle);
//    }

//    private void DrawFooter()
//    {
//        Rect footerRect = new Rect(0, position.height - FooterHeight, position.width, FooterHeight);

//        GUILayout.BeginArea(footerRect, EditorStyles.toolbar);
//        GUILayout.BeginHorizontal(EditorStyles.toolbar);

//        GUILayout.Label(string.IsNullOrEmpty(selectedPath) ? currentFolder : selectedPath, EditorStyles.miniLabel);
//        GUILayout.FlexibleSpace();
//        GUILayout.Label("Enter: Open    Esc: Close    Ctrl+F: Search", EditorStyles.miniLabel);

//        GUILayout.EndHorizontal();
//        GUILayout.EndArea();
//    }

//    private void HandleKeyboard()
//    {
//        Event e = Event.current;
//        if (e.type != EventType.KeyDown)
//            return;

//        if (e.keyCode == KeyCode.Escape)
//        {
//            BeginClose();
//            e.Use();
//            return;
//        }

//        if ((e.control || e.command) && e.keyCode == KeyCode.F)
//        {
//            EditorGUI.FocusTextInControl("ProjectDrawerSearch");
//            e.Use();
//            return;
//        }

//        if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
//        {
//            if (!string.IsNullOrEmpty(selectedPath))
//            {
//                if (AssetDatabase.IsValidFolder(selectedPath))
//                    OpenFolder(selectedPath);
//                else
//                {
//                    var obj = AssetDatabase.LoadMainAssetAtPath(selectedPath);
//                    if (obj != null)
//                        OpenAsset(obj);
//                }
//            }

//            e.Use();
//        }

//        if (e.keyCode == KeyCode.Backspace)
//        {
//            GoToParentFolder();
//            e.Use();
//        }
//    }

//    private void OpenFolder(string folderPath)
//    {
//        if (string.IsNullOrEmpty(folderPath))
//            return;

//        currentFolder = folderPath;
//        selectedPath = folderPath;
//        ExpandParents(folderPath);
//        RebuildVisibleEntries();
//    }

//    private void GoToParentFolder()
//    {
//        if (currentFolder == "Assets" || currentFolder == "Packages")
//            return;

//        int slash = currentFolder.LastIndexOf('/');
//        if (slash > 0)
//            OpenFolder(currentFolder.Substring(0, slash));
//    }

//    private void ExpandParents(string folderPath)
//    {
//        string[] parts = folderPath.Split('/');
//        string accum = parts[0];
//        expandedFolders.Add(accum);

//        for (int i = 1; i < parts.Length; i++)
//        {
//            accum += "/" + parts[i];
//            expandedFolders.Add(accum);
//        }
//    }

//    private void RebuildVisibleEntries()
//    {
//        visibleEntries.Clear();

//        if (showPackages && currentFolder == "Packages")
//        {
//            visibleEntries.AddRange(GetPackageEntries());
//            return;
//        }

//        if (!AssetDatabase.IsValidFolder(currentFolder))
//            currentFolder = "Assets";

//        string[] subFolders = AssetDatabase.GetSubFolders(currentFolder);
//        visibleEntries.AddRange(subFolders.OrderBy(p => p));

//        string[] guids = AssetDatabase.FindAssets("", new[] { currentFolder });
//        foreach (string guid in guids)
//        {
//            string path = AssetDatabase.GUIDToAssetPath(guid);

//            if (path == currentFolder)
//                continue;

//            if (AssetDatabase.IsValidFolder(path))
//                continue;

//            string parent = System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/");
//            if (parent == currentFolder)
//                visibleEntries.Add(path);
//        }

//        visibleEntries.Sort(CompareProjectLike);
//    }

//    private List<string> GetEntriesForCurrentView()
//    {
//        IEnumerable<string> entries = visibleEntries;

//        if (!string.IsNullOrWhiteSpace(searchText))
//        {
//            string lower = searchText.Trim().ToLowerInvariant();
//            entries = entries.Where(p => System.IO.Path.GetFileName(p).ToLowerInvariant().Contains(lower));
//        }

//        return entries.ToList();
//    }

//    private IEnumerable<string> GetPackageEntries()
//    {
//        List<string> results = new();

//        foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
//        {
//            if (string.IsNullOrEmpty(package.assetPath))
//                continue;

//            results.Add(package.assetPath);
//        }

//        results.Sort();
//        return results;
//    }

//    private static int CompareProjectLike(string a, string b)
//    {
//        bool aFolder = AssetDatabase.IsValidFolder(a);
//        bool bFolder = AssetDatabase.IsValidFolder(b);

//        if (aFolder && !bFolder) return -1;
//        if (!aFolder && bFolder) return 1;

//        return string.CompareOrdinal(
//            System.IO.Path.GetFileName(a).ToLowerInvariant(),
//            System.IO.Path.GetFileName(b).ToLowerInvariant()
//        );
//    }

//    private static void OpenAsset(UnityEngine.Object asset)
//    {
//        Selection.activeObject = asset;
//        EditorGUIUtility.PingObject(asset);
//        AssetDatabase.OpenAsset(asset);
//    }

//    private static Rect GetEditorMainWindowRect()
//    {
//        Type containerWinType = Type.GetType("UnityEditor.ContainerWindow,UnityEditor");
//        if (containerWinType == null)
//            return new Rect(100, 100, 1400, 900);

//        FieldInfo showModeField = containerWinType.GetField("m_ShowMode", BindingFlags.NonPublic | BindingFlags.Instance);
//        PropertyInfo positionProperty = containerWinType.GetProperty("position", BindingFlags.Public | BindingFlags.Instance);

//        UnityEngine.Object[] windows = Resources.FindObjectsOfTypeAll(containerWinType);
//        foreach (UnityEngine.Object win in windows)
//        {
//            if (showModeField == null || positionProperty == null)
//                continue;

//            int showMode = (int)showModeField.GetValue(win);
//            if (showMode == 4)
//                return (Rect)positionProperty.GetValue(win, null);
//        }

//        return new Rect(100, 100, 1400, 900);
//    }
//}
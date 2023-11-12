using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor.EditorTools;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace UnityEditor.Tilemaps
{
    /// <summary>
    /// GridPaintingState controls the state of objects for painting with a Tile Palette.
    /// </summary>
    /// <remarks>
    /// Utilize this class to get and set the current painting target and brush for painting
    /// with the Tile Palette.
    /// </remarks>
    public class GridPaintingState : ScriptableSingleton<GridPaintingState>
    {
        [SerializeField] private GameObject m_EditModeScenePaintTarget; // Which GameObject in scene was the last painting target in EditMode
        [SerializeField] private GameObject m_ScenePaintTarget; // Which GameObject in scene is considered as painting target
        [SerializeField] private EditorTool[] m_BrushTools;
        [SerializeField] private GridBrushBase m_Brush; // Which brush will handle painting callbacks
        [SerializeField] private PaintableGrid m_ActiveGrid; // Grid that has painting focus (can be palette, too)
        [SerializeField] private PaintableGrid m_LastActiveGrid; // Grid that last had painting focus (can be palette, too)
        [SerializeField] private HashSet<System.Object> m_InterestedPainters = new HashSet<System.Object>(); // A list of objects that can paint using the GridPaintingState

        [SerializeField] private GameObject m_Palette;

        [SerializeField] private bool m_DrawGridGizmo = true;
        [SerializeField] private bool m_DrawGizmos;

        [SerializeField] private bool m_IsEditing;

        private GameObject[] m_CachedPaintTargets;
        private bool m_FlushPaintTargetCache;
        private Editor m_CachedEditor;
        private bool m_SavingPalette;
        private float m_BrushToolbarSize;

        private GridBrushEditorBase m_PreviousToolActivatedEditor;
        private GridBrushBase.Tool m_PreviousToolActivated;

        private PaintableSceneViewGrid m_PaintableSceneViewGrid;

        /// <summary>
        /// Callback when the Tile Palette's active target has changed
        /// </summary>
        public static event Action<GameObject> scenePaintTargetChanged;
        /// <summary>
        /// Callback when the Tile Palette's active brush has changed.
        /// </summary>
        public static event Action<GridBrushBase> brushChanged;
        /// <summary>
        /// Callback when the Tile Palette's active brush tools have changed.
        /// </summary>
        public static event Action brushToolsChanged;
        /// <summary>
        /// Callback before the Tile Palette's active palette GameObject has changed.
        /// </summary>
        public static event Action beforePaletteChanged;
        /// <summary>
        /// Callback when the Tile Palette's active palette GameObject has changed.
        /// </summary>
        public static event Action<GameObject> paletteChanged;
        /// <summary>
        /// Callback when the Tile Palette's list of palettes has changed
        /// </summary>
        public static event Action palettesChanged;
        /// <summary>
        /// Callback when the Tile Palette's valid targets has changed.
        /// </summary>
        public static event Action validTargetsChanged;
        /// <summary>
        /// Callback when Tile Palette edit mode has changed.
        /// </summary>
        public static event Action editModeChanged;

        private static readonly string k_TilemapLastPaletteEditorPref = "TilemapLastPalette";
        private string lastTilemapPalette
        {
            get
            {
                return EditorPrefs.GetString(k_TilemapLastPaletteEditorPref, "");
            }
            set
            {
                EditorPrefs.SetString(k_TilemapLastPaletteEditorPref, value);
            }
        }

        readonly TilemapEditorTool.ShortcutContext m_ShortcutContext = new TilemapEditorTool.ShortcutContext { active = true };

        private void OnEnable()
        {
            EditorApplication.hierarchyChanged += HierarchyChanged;
            EditorApplication.playModeStateChanged += PlayModeStateChanged;
            Selection.selectionChanged += OnSelectionChange;

            m_FlushPaintTargetCache = true;
        }

        private void OnDisable()
        {
            m_InterestedPainters.Clear();
            EditorApplication.hierarchyChanged -= HierarchyChanged;
            EditorApplication.playModeStateChanged -= PlayModeStateChanged;
            Selection.selectionChanged -= OnSelectionChange;
            FlushCache();
        }

        private void OnEditEnable()
        {
            isEditing = true;
            if (palette == null && !String.IsNullOrEmpty(lastTilemapPalette))
            {
                var lastPalette = GridPalettes.palettes
                    .Where((paletteInList, _) => (AssetDatabase.GetAssetPath(paletteInList) == lastTilemapPalette))
                    .FirstOrDefault();
                if (lastPalette != null)
                    palette = lastPalette;
            }
            if (palette == null && GridPalettes.palettes.Count > 0)
            {
                palette = GridPalettes.palettes[0];
            }

            if (m_PaintableSceneViewGrid == null)
            {
                m_PaintableSceneViewGrid = CreateInstance<PaintableSceneViewGrid>();
                m_PaintableSceneViewGrid.hideFlags = HideFlags.HideAndDontSave;
            }

            m_FlushPaintTargetCache = true;
            GridPaletteBrushes.FlushCache();
            GridPalettes.palettesChanged += PalettesChanged;
            ShortcutIntegration.instance.profileManager.shortcutBindingChanged += UpdateTooltips;

            scenePaintTargetChanged += TilemapFocusModeUtility.OnScenePaintTargetChanged;
            brushChanged += TilemapFocusModeUtility.OnBrushChanged;
            paletteChanged += PaletteChanged;
            SceneView.duringSceneGui += TilemapFocusModeUtility.OnSceneViewGUI;

            ToolManager.activeToolChanged += ActiveToolChanged;
            ToolManager.activeToolChanging += ActiveToolChanging;

            AssetPreview.SetPreviewTextureCacheSize(256, GetInstanceID());

            ShortcutIntegration.instance.contextManager.RegisterToolContext(m_ShortcutContext);
        }

        private void PaletteChanged(GameObject obj)
        {
            lastTilemapPalette = AssetDatabase.GetAssetPath(palette);
        }

        private void PalettesChanged()
        {
            palettesChanged?.Invoke();
        }

        private void OnEditDisable()
        {
            TilemapFocusModeUtility.SetFocusMode(TilemapFocusModeUtility.TilemapFocusMode.None);

            CallOnToolDeactivated();

            gridBrush = null;

            DestroyImmediate(m_PaintableSceneViewGrid);

            if (PaintableGrid.InGridEditMode())
            {
                // Set Editor Tool to an always available Tool, as Tile Palette Tools are not available any more
                ToolManager.SetActiveTool<ViewModeTool>();
            }

            ShortcutIntegration.instance.profileManager.shortcutBindingChanged -= UpdateTooltips;
            ToolManager.activeToolChanged -= ActiveToolChanged;
            ToolManager.activeToolChanging -= ActiveToolChanging;
            SceneView.duringSceneGui -= TilemapFocusModeUtility.OnSceneViewGUI;
            brushChanged -= TilemapFocusModeUtility.OnBrushChanged;
            paletteChanged -= PaletteChanged;
            GridPalettes.palettesChanged -= PalettesChanged;

            ShortcutIntegration.instance.contextManager.DeregisterToolContext(m_ShortcutContext);

            isEditing = false;
        }

        private void ActiveToolChanged()
        {
            if (gridBrush != null && PaintableGrid.InGridEditMode() && activeBrushEditor != null)
            {
                GridBrushBase.Tool tool = PaintableGrid.EditTypeToBrushTool(ToolManager.activeToolType);
                activeBrushEditor.OnToolActivated(tool);
                m_PreviousToolActivatedEditor = activeBrushEditor;
                m_PreviousToolActivated = tool;

                for (int i = 0; i < TilePaletteMouseCursorUtility.MouseStyles.sceneViewEditModes.Length; ++i)
                {
                    if (TilePaletteMouseCursorUtility.MouseStyles.sceneViewEditModes[i] == tool)
                    {
                        Cursor.SetCursor(TilePaletteMouseCursorUtility.MouseStyles.mouseCursorTextures[i],
                            TilePaletteMouseCursorUtility.MouseStyles.mouseCursorTextures[i] != null ? TilePaletteMouseCursorUtility.MouseStyles.mouseCursorOSHotspot[(int)SystemInfo.operatingSystemFamily] : Vector2.zero,
                            CursorMode.Auto);
                        break;
                    }
                }
            }
            else
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
        }

        private void ActiveToolChanging()
        {
            if (GridSelection.active
                && !TilemapEditorTool.IsActive(typeof(MoveTool))
                && !TilemapEditorTool.IsActive(typeof(SelectTool))
                && !ToolManager.activeToolType.IsSubclassOf(typeof(GridSelectionTool)))
            {
                GridSelection.Clear();
            }
            CallOnToolDeactivated();
        }

        private void CallOnToolDeactivated()
        {
            if (gridBrush != null && m_PreviousToolActivatedEditor != null)
            {
                m_PreviousToolActivatedEditor.OnToolDeactivated(m_PreviousToolActivated);
                m_PreviousToolActivatedEditor = null;

                if (!PaintableGrid.InGridEditMode())
                    Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
        }

        private void OnSelectionChange()
        {
            if (!hasInterestedPainters)
                return;

            var selectedObject = Selection.activeGameObject;
            if (ValidatePaintTarget(selectedObject))
            {
                scenePaintTarget = selectedObject;
            }

            if (selectedObject != null)
            {
                bool isPrefab = EditorUtility.IsPersistent(selectedObject) || (selectedObject.hideFlags & HideFlags.NotEditable) != 0;
                if (isPrefab)
                {
                    var assetPath = AssetDatabase.GetAssetPath(selectedObject);
                    var allAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
                    foreach (var asset in allAssets)
                    {
                        if (asset != null && asset.GetType() == typeof(GridPalette))
                        {
                            var targetPalette = (GameObject)AssetDatabase.LoadMainAssetAtPath(assetPath);
                            if (targetPalette != palette)
                                palette = targetPalette;
                            break;
                        }
                    }
                }
            }
        }

        private void PlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                m_EditModeScenePaintTarget = scenePaintTarget;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                if (GridPaintActiveTargetsPreferences.restoreEditModeSelection && m_EditModeScenePaintTarget != null)
                {
                    scenePaintTarget = m_EditModeScenePaintTarget;
                }
            }
        }

        private void HierarchyChanged()
        {
            if (hasInterestedPainters)
            {
                m_FlushPaintTargetCache = true;
                if (validTargets == null || validTargets.Length == 0 || !validTargets.Contains(scenePaintTarget))
                {
                    // case 1102618: Try to use current Selection as scene paint target if possible
                    if (Selection.activeGameObject != null && hasInterestedPainters && ValidatePaintTarget(Selection.activeGameObject))
                    {
                        scenePaintTarget = Selection.activeGameObject;
                    }
                    else
                    {
                        AutoSelectPaintTarget();
                    }
                }
            }
        }

        private GameObject[] GetValidTargets()
        {
            if (m_FlushPaintTargetCache)
            {
                m_CachedPaintTargets = null;
                if (activeBrushEditor != null)
                    m_CachedPaintTargets = activeBrushEditor.validTargets;
                if (m_CachedPaintTargets == null || m_CachedPaintTargets.Length == 0)
                    scenePaintTarget = null;
                else
                {
                    var comparer = GridPaintActiveTargetsPreferences.GetTargetComparer();
                    if (comparer != null)
                        Array.Sort(m_CachedPaintTargets, comparer);
                }
                m_FlushPaintTargetCache = false;
                validTargetsChanged?.Invoke();
            }
            return m_CachedPaintTargets;
        }

        private static void UpdateTooltips(IShortcutProfileManager obj, Identifier identifier, ShortcutBinding oldBinding, ShortcutBinding newBinding)
        {
            TilemapEditorTool.UpdateTooltips();
        }

        internal static void RegisterShortcutContext()
        {
            // ShortcutIntegration instance is recreated after LoadLayout which wipes the OnEnable registration
            ShortcutIntegration.instance.contextManager.RegisterToolContext(instance.m_ShortcutContext);
        }

        internal static void AutoSelectPaintTarget()
        {
            if (activeBrushEditor != null)
            {
                if (validTargets != null && validTargets.Length > 0)
                {
                    scenePaintTarget = validTargets[0];
                }
            }
        }

        /// <summary>
        /// The currently active target for the Tile Palette
        /// </summary>
        public static GameObject scenePaintTarget
        {
            get { return instance.m_ScenePaintTarget; }
            set
            {
                if (value != instance.m_ScenePaintTarget)
                {
                    instance.m_ScenePaintTarget = value;
                    if (scenePaintTargetChanged != null)
                        scenePaintTargetChanged(instance.m_ScenePaintTarget);
                    RepaintGridPaintPaletteWindow();
                }
            }
        }

        /// <summary>
        /// The currently active brush for the Tile Palette
        /// </summary>
        public static GridBrushBase gridBrush
        {
            get
            {
                if (instance.m_Brush == null)
                {
                    instance.m_Brush = GridPaletteBrushes.instance.GetLastUsedBrush();
                    UpdateBrushToolbar();
                }
                return instance.m_Brush;
            }
            set
            {
                if (instance.m_Brush != value)
                {
                    instance.m_Brush = value;
                    instance.m_FlushPaintTargetCache = true;

                    if (value != null)
                    {
                        GridPaletteBrushes.instance.StoreLastUsedBrush(value);
                        UpdateBrushToolbar();
                    }

                    // Ensure that current scenePaintTarget is still a valid target after a brush change
                    if (scenePaintTarget != null && !ValidatePaintTarget(scenePaintTarget))
                        scenePaintTarget = null;

                    // Use Selection if previous scenePaintTarget was not valid
                    if (scenePaintTarget == null)
                        scenePaintTarget = ValidatePaintTarget(Selection.activeGameObject) ? Selection.activeGameObject : null;

                    // Auto select a valid target if there is still no scenePaintTarget
                    if (scenePaintTarget == null)
                        AutoSelectPaintTarget();

                    if (null != brushChanged)
                        brushChanged(value);

                    RepaintGridPaintPaletteWindow();
                }
            }
        }

        /// <summary>
        ///  Returns all available brushes for the Tile Palette
        /// </summary>
        public static IList<GridBrushBase> brushes
        {
            get { return GridPaletteBrushes.brushes; }
        }

        internal static GridBrush defaultBrush
        {
            get { return gridBrush as GridBrush; }
            set { gridBrush = value; }
        }

        /// <summary>
        /// The currently active palette GameObject for the Tile Palette
        /// </summary>
        public static GameObject palette
        {
            get
            {
                return instance.m_Palette;
            }
            set
            {
                if (value == null || !GridPalettes.palettes.Contains(value))
                    throw new ArgumentException(L10n.Tr("Unable to set invalid palette"));
                if (instance.m_Palette != value)
                {
                    OnBeforePaletteChanged();
                    instance.m_Palette = value;
                    OnPaletteChanged(instance.m_Palette);
                }
            }
        }

        /// <summary>
        /// Checks if target GameObject is part of the active Palette.
        /// </summary>
        /// <param name="target">GameObject to check.</param>
        /// <returns>True if the target GameObject is part of the active palette. False if not.</returns>
        public static bool IsPartOfActivePalette(GameObject target)
        {
            foreach (var clipboard in GridPaintPaletteClipboard.instances)
            {
                if (target == clipboard.paletteInstance)
                    return true;
            }
            if (target == palette)
                return true;
            var parent = target.transform.parent;
            return parent != null && IsPartOfActivePalette(parent.gameObject);
        }

        /// <summary>
        /// Returns all available Palette GameObjects for the Tile Palette
        /// </summary>
        public static IList<GameObject> palettes
        {
            get { return GridPalettes.palettes; }
        }

        /// <summary>
        /// The currently active editor for the active brush for the Tile Palette
        /// </summary>
        public static GridBrushEditorBase activeBrushEditor
        {
            get
            {
                Editor.CreateCachedEditor(gridBrush, null, ref instance.m_CachedEditor);
                GridBrushEditorBase baseEditor = instance.m_CachedEditor as GridBrushEditorBase;
                return baseEditor;
            }
        }

        internal static Editor fallbackEditor
        {
            get
            {
                Editor.CreateCachedEditor(gridBrush, null, ref instance.m_CachedEditor);
                return instance.m_CachedEditor;
            }
        }

        internal static PaintableGrid activeGrid
        {
            get { return instance.m_ActiveGrid; }
            set
            {
                instance.m_ActiveGrid = value;
                if (instance.m_ActiveGrid != null)
                    instance.m_LastActiveGrid = value;
            }
        }

        internal static PaintableGrid lastActiveGrid
        {
            get { return instance.m_LastActiveGrid; }
        }

        internal static PaintableSceneViewGrid paintableSceneViewGrid
        {
            get => instance.m_PaintableSceneViewGrid;
        }

        /// <summary>
        /// The last active mouse position on the `SceneView`
        /// when the `GridPaintingState` is active.
        /// </summary>
        public static Vector2 lastSceneViewMousePosition
        {
            get => paintableSceneViewGrid.mousePosition;
        }

        /// <summary>
        /// The last active grid position on the `SceneView`
        /// when the `GridPaintingState` is active.
        /// </summary>
        public static Vector3Int lastSceneViewGridPosition
        {
            get => new Vector3Int(paintableSceneViewGrid.mouseGridPosition.x
                , paintableSceneViewGrid.mouseGridPosition.y
                , paintableSceneViewGrid.zPosition);
        }

        internal static EditorTool[] activeBrushTools
        {
            get { return instance.m_BrushTools; }
            set
            {
                instance.m_BrushTools = value;
                brushToolsChanged?.Invoke();
            }
        }

        internal static float activeBrushToolbarSize
        {
            get
            {
                if (instance.m_BrushToolbarSize == 0.0f)
                    CalculateToolbarSize();
                return instance.m_BrushToolbarSize;
            }
            set { instance.m_BrushToolbarSize = value;  }
        }

        internal static bool drawGridGizmo
        {
            get => instance.m_DrawGridGizmo;
            set => instance.m_DrawGridGizmo = value;
        }

        internal static bool drawGizmos
        {
            get => instance.m_DrawGizmos;
            set => instance.m_DrawGizmos = value;
        }

        /// <summary>
        /// Returns whether GridPaintingState is active for editing.
        /// </summary>
        public static bool isEditing
        {
            get => instance.m_IsEditing;
            internal set
            {
                if (value != instance.m_IsEditing)
                {
                    instance.m_IsEditing = value;
                    editModeChanged?.Invoke();
                }
            }
        }

        private static void CalculateToolbarSize()
        {
            GUIStyle toolbarStyle = "Command";
            activeBrushToolbarSize = activeBrushTools.Sum(x => toolbarStyle.CalcSize(x.toolbarIcon).x);
        }

        internal static void SetBrushTools(EditorTool[] editorTools)
        {
            activeBrushTools = editorTools;
            activeBrushToolbarSize = 0.0f;
        }

        private static bool ValidatePaintTarget(GameObject candidate)
        {
            if (candidate == null)
                return false;

            // Case 1327021: Do not allow disabled GameObjects as a paint target
            if (!candidate.activeInHierarchy)
                return false;

            if (candidate.GetComponentInParent<Grid>() == null && candidate.GetComponent<Grid>() == null)
                return false;

            if (validTargets != null && validTargets.Length > 0 && !validTargets.Contains(candidate))
                return false;

            if (PrefabUtility.IsPartOfPrefabAsset(candidate))
                return false;

            return true;
        }

        internal static void FlushCache()
        {
            if (instance.m_CachedEditor != null)
            {
                DestroyImmediate(instance.m_CachedEditor);
                instance.m_CachedEditor = null;
            }
            instance.m_FlushPaintTargetCache = true;
        }

        /// <summary>
        /// A list of all valid targets that can be set as an active target for the Tile Palette
        /// </summary>
        public static GameObject[] validTargets
        {
            get { return instance.GetValidTargets(); }
        }

        internal static bool savingPalette
        {
            get { return instance.m_SavingPalette; }
            set { instance.m_SavingPalette = value; }
        }

        internal static void OnBeforePaletteChanged()
        {
            if (null != beforePaletteChanged)
                beforePaletteChanged();
        }

        internal static void OnPaletteChanged(GameObject changedPalette)
        {
            if (null != paletteChanged)
                paletteChanged(changedPalette);
        }

        internal static void UpdateBrushToolbar()
        {
            BrushToolsAttribute toolAttribute = null;
            if (instance.m_Brush != null)
                toolAttribute = (BrushToolsAttribute)instance.m_Brush.GetType().GetCustomAttribute(typeof(BrushToolsAttribute), false);
            TilemapEditorTool.UpdateEditorTools(toolAttribute);
        }

        internal static void UpdateActiveGridPalette()
        {
            foreach (var clipboard in GridPaintPaletteClipboard.instances)
            {
                clipboard.DelayedResetPreviewInstance();
            }
        }

        internal static void RepaintGridPaintPaletteWindow()
        {
            foreach (var clipboard in GridPaintPaletteClipboard.instances)
            {
                clipboard.Repaint();
            }
        }

        internal static void UnlockGridPaintPaletteClipboardForEditing()
        {
            foreach (var clipboard in GridPaintPaletteClipboard.instances)
            {
                clipboard.UnlockAndEdit();
            }
        }

        internal static void RegisterPainterInterest(System.Object painter)
        {
            var added = instance.m_InterestedPainters.Add(painter);
            if (added && instance.m_InterestedPainters.Count == 1)
                instance.OnEditEnable();
        }

        internal static void UnregisterPainterInterest(System.Object painter)
        {
            var removed = instance.m_InterestedPainters.Remove(painter);
            if (removed && instance.m_InterestedPainters.Count == 0)
                instance.OnEditDisable();
        }

        internal static bool hasInterestedPainters
        {
            get { return instance != null && instance.m_InterestedPainters.Count > 0; }
        }
    }
}

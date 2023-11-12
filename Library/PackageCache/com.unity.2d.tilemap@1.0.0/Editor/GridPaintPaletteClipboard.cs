using System;
using System.Collections.Generic;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using Event = UnityEngine.Event;

namespace UnityEditor.Tilemaps
{
    [Serializable]
    internal class GridPaintPaletteClipboard : PaintableGrid
    {
        static class Styles
        {
            public static readonly GUIContent emptyProjectInfo = EditorGUIUtility.TrTextContent("Create a new palette in the dropdown above.");
            public static readonly GUIContent emptyPaletteInfo = EditorGUIUtility.TrTextContent("Drag Tile, Sprite or Sprite Texture assets here.");
            public static readonly GUIContent invalidPaletteInfo = EditorGUIUtility.TrTextContent("This is an invalid palette. Did you delete the palette asset?");
            public static readonly GUIContent invalidGridInfo = EditorGUIUtility.TrTextContent("The palette has an invalid Grid. Did you add a Grid to the palette asset?");
        }



        private static List<GridPaintPaletteClipboard> s_Instances;
        public static List<GridPaintPaletteClipboard> instances
        {
            get
            {
                if (s_Instances == null)
                    s_Instances = new List<GridPaintPaletteClipboard>();
                return s_Instances;
            }
        }

        private bool disableOnBrushPicked;
        public event Action onBrushPicked;

        private static readonly string paletteSavedOutsideClipboard = L10n.Tr("Palette Asset {0} was changed outside of the Tile Palette. All changes in the Tile Palette made will be reverted.");

        private bool m_PaletteNeedsSave;
        private const float k_ZoomSpeed = 7f;
        private const float k_MinZoom = 10f; // How many pixels per cell at minimum
        private const float k_MaxZoom = 100f; // How many pixels per cell at maximum
        private const float k_Padding = 0.75f; // How many percentages of window size is the empty padding around the palette content

        private int m_KeyboardPanningID;
        private int m_MousePanningID;
        private Vector2 m_MouseZoomInitialPosition;

        private float k_KeyboardPanningSpeed = 3.0f;

        private Vector3 m_KeyboardPanning;

        private Rect m_GUIRect = new Rect(0, 0, 200, 200);

        public Rect guiRect
        {
            get { return m_GUIRect; }
            set
            {
                if (m_GUIRect != value)
                {
                    Rect oldValue = m_GUIRect;
                    m_GUIRect = value;
                    OnViewSizeChanged(oldValue, m_GUIRect);
                }
            }
        }

        private VisualElement m_VisualElement;

        public bool activeDragAndDrop { get { return DragAndDrop.objectReferences.Length > 0 && guiRect.Contains(Event.current.mousePosition); } }

        [SerializeField] private bool m_CameraInitializedToBounds;
        [SerializeField] public bool m_CameraPositionSaved;
        [SerializeField] public Vector3 m_CameraPosition;
        [SerializeField] public float m_CameraOrthographicSize;

        private BoundsInt? m_ActivePick;
        private Vector3Int m_ActivePivot;
        private Dictionary<Vector2Int, TileDragAndDropHoverData> m_HoverData;
        private bool m_Unlocked;

        private GameObject palette => GridPaintingState.palette;
        private GridBrushBase gridBrush => GridPaintingState.gridBrush;

        private PreviewRenderUtility m_PreviewUtility;

        internal Vector3 cameraPosition
        {
            get => m_PreviewUtility.camera.transform.position;
            set
            {
                m_PreviewUtility.camera.transform.position = value;
                ClampZoomAndPan();
            }
        }

        internal float cameraSize
        {
            get => m_PreviewUtility.camera.orthographicSize;
            set
            {
                m_PreviewUtility.camera.orthographicSize = value;
                ClampZoomAndPan();
            }
        }

        internal TransparencySortMode cameraTransparencySortMode
        {
            get => m_PreviewUtility.camera.transparencySortMode;
            set => m_PreviewUtility.camera.transparencySortMode = value;
        }

        internal Vector3 cameraTransparencySortAxis
        {
            get => m_PreviewUtility.camera.transparencySortAxis;
            set => m_PreviewUtility.camera.transparencySortAxis = value;
        }

        [SerializeField] private GameObject m_PaletteInstance;

        internal GameObject paletteInstance
        {
            get
            {
                if (m_PaletteInstance == null && palette != null && m_PreviewUtility != null)
                    ResetPreviewInstance();
                return m_PaletteInstance;
            }
        }

        private Tilemap tilemap { get { return paletteInstance != null ? paletteInstance.GetComponentInChildren<Tilemap>() : null; } }
        private Grid grid { get { return paletteInstance != null ? paletteInstance.GetComponent<Grid>() : null; } }
        private Grid prefabGrid { get { return palette != null ? palette.GetComponent<Grid>() : null; } }

        private Mesh m_GridMesh;
        private int m_LastGridHash;
        private Material m_GridMaterial;
        private static readonly Color k_GridColor = Color.white.AlphaMultiplied(0.1f);
        private static readonly PrefColor tilePaletteBackgroundColor = new PrefColor("2D/Tile Palette Background"
            , 118.0f / 255.0f // Light
            , 118.0f / 255.0f
            , 118.0f / 255.0f
            , 127.0f / 255.0f
            , 31.0f / 255.0f // Dark
            , 31.0f / 255.0f
            , 31.0f / 255.0f
            , 127.0f / 255.0f);

        private bool m_PaletteUsed; // We mark palette used, when it has been changed in any way during being actively open.
        private Vector2? m_PreviousMousePosition;

        private bool m_DelayedResetPaletteInstance;
        internal void DelayedResetPreviewInstance()
        {
            m_DelayedResetPaletteInstance = true;
        }

        public TileBase activeTile
        {
            get
            {
                if (m_ActivePick.HasValue && m_ActivePick.Value.size == Vector3Int.one && GridPaintingState.defaultBrush != null && GridPaintingState.defaultBrush.cellCount > 0)
                    return GridPaintingState.defaultBrush.cells[0].tile;
                return null;
            }
        }

        private RectInt bounds
        {
            get
            {
                RectInt r = default;
                if (tilemap == null || TilemapIsEmpty(tilemap))
                    return r;

                tilemap.CompressBounds();
                var origin = tilemap.origin;
                var size = tilemap.size;
                r = new RectInt(origin.x, origin.y, size.x, size.y);
                return r;
            }
        }

        // Max area we are ever showing. Depends on the zoom level and content of palette.
        private Rect paddedBounds
        {
            get
            {
                var GUIAspect = m_GUIRect.width / m_GUIRect.height;
                var orthographicSize = m_PreviewUtility.camera.orthographicSize;
                var paddingW = orthographicSize * GUIAspect * k_Padding * 2f;
                var paddingH = orthographicSize * k_Padding * 2f;

                Bounds localBounds = grid.GetBoundsLocal(
                    new Vector3(bounds.xMin, bounds.yMin, 0.0f),
                    new Vector3(bounds.size.x, bounds.size.y, 0.0f));
                Rect result = new Rect(
                    new Vector2(localBounds.min.x - paddingW, localBounds.min.y - paddingH),
                    new Vector2(localBounds.size.x + paddingW * 2f, localBounds.size.y + paddingH * 2f));

                return result;
            }
        }

        private RectInt paddedBoundsInt
        {
            get
            {
                Vector3Int min = grid.LocalToCell(paddedBounds.min);
                Vector3Int max = grid.LocalToCell(paddedBounds.max) + Vector3Int.one;
                return new RectInt(min.x, min.y, max.x - min.x, max.y - min.y);
            }
        }

        private GameObject brushTarget
        {
            get
            {
                return (tilemap != null) ? tilemap.gameObject : (grid != null) ? grid.gameObject : null;
            }
        }

        public bool unlocked
        {
            get { return m_Unlocked; }
            set
            {
                if (value == false && m_Unlocked)
                {
                    if (tilemap != null)
                        tilemap.ClearAllEditorPreviewTiles();
                    SavePaletteIfNecessary();
                }
                m_Unlocked = value;
                unlockedChanged?.Invoke(m_Unlocked);
            }
        }
        public event Action<bool> unlockedChanged;

        public bool isReceivingDragAndDrop { get { return m_HoverData != null && m_HoverData.Count > 0; } }

        public bool showNewEmptyClipboardInfo
        {
            get
            {
                if (paletteInstance == null)
                    return false;

                if (tilemap == null)
                    return false;

                if (unlocked && inEditMode)
                    return false;

                if (!TilemapIsEmpty(tilemap))
                    return false;

                if (tilemap.transform.childCount > 0)
                    return false;

                if (isReceivingDragAndDrop)
                    return false;

                // If user happens to erase the last content of used palette, we don't want to show the new palette info anymore
                if (m_PaletteUsed)
                    return false;

                return true;
            }
        }

        public bool isModified { get { return m_PaletteNeedsSave; } }

        public VisualElement attachedVisualElement
        {
            set { m_VisualElement = value; }
        }

        public void OnBeforePaletteSelectionChanged()
        {
            SavePaletteIfNecessary();
            DestroyPreviewInstance();
            FlushHoverData();
        }

        private void FlushHoverData()
        {
            if (m_HoverData != null)
            {
                m_HoverData.Clear();
                m_HoverData = null;
            }
        }

        public void OnAfterPaletteSelectionChanged()
        {
            m_PaletteUsed = false;
            ResetPreviewInstance();

            if (palette != null)
                ResetPreviewCamera();
        }

        public void SetupPreviewCameraOnInit()
        {
            if (m_CameraPositionSaved)
                LoadSavedCameraPosition();
            else
                ResetPreviewCamera();
        }

        private void LoadSavedCameraPosition()
        {
            m_PreviewUtility.camera.transform.position = m_CameraPosition;
            m_PreviewUtility.camera.orthographicSize = m_CameraOrthographicSize;
            m_PreviewUtility.camera.nearClipPlane = 0.01f;
            m_PreviewUtility.camera.farClipPlane = 100f;
        }

        private void ResetPreviewCamera()
        {
            var transform = m_PreviewUtility.camera.transform;
            transform.position = new Vector3(0, 0, -10f);
            transform.rotation = Quaternion.identity;
            m_PreviewUtility.camera.nearClipPlane = 0.01f;
            m_PreviewUtility.camera.farClipPlane = 100f;
            FrameEntirePalette();
        }

        public void InitPreviewUtility()
        {
            m_PreviewUtility = new PreviewRenderUtility(true, true);
            m_PreviewUtility.camera.orthographic = true;
            m_PreviewUtility.camera.orthographicSize = 5f;
            m_PreviewUtility.camera.transform.position = new Vector3(0f, 0f, -10f);
            m_PreviewUtility.ambientColor = new Color(1f, 1f, 1f, 0);
        }

        public void ResetPreviewInstance()
        {
            // Store GridSelection for current Palette Instance
            Stack<int> childPositions = null;
            BoundsInt previousGridSelectionPosition = default;
            if (m_PaletteInstance != null && GridSelection.active && GridSelection.target.transform.IsChildOf(m_PaletteInstance.transform))
            {
                childPositions = new Stack<int>();
                var transform = GridSelection.target.transform;
                while (transform != null && transform != m_PaletteInstance.transform)
                {
                    childPositions.Push(transform.GetSiblingIndex());
                    transform = transform.parent;
                }
                previousGridSelectionPosition = GridSelection.position;
                ClearGridSelection();
            }

            DestroyPreviewInstance();
            if (palette != null)
            {
                m_PaletteInstance = m_PreviewUtility.InstantiatePrefabInScene(palette);

                // Disconnecting prefabs is no longer possible.
                // If performance of overrides on palette palette instance turns out to be a problem.
                // unpack the prefab instance here, and overwrite the prefab later instead of reconnecting.
                PrefabUtility.UnpackPrefabInstance(m_PaletteInstance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);

                EditorUtility.InitInstantiatedPreviewRecursive(m_PaletteInstance);
                m_PaletteInstance.transform.position = new Vector3(0, 0, 0);
                m_PaletteInstance.transform.rotation = Quaternion.identity;
                m_PaletteInstance.transform.localScale = Vector3.one;

                GridPalette paletteAsset = GridPaletteUtility.GetGridPaletteFromPaletteAsset(palette);
                if (paletteAsset != null)
                {
                    if (paletteAsset.cellSizing == GridPalette.CellSizing.Automatic)
                    {
                        var paletteGrid = m_PaletteInstance.GetComponent<Grid>();
                        if (paletteGrid != null)
                        {
                            paletteGrid.cellSize = GridPaletteUtility.CalculateAutoCellSize(paletteGrid, paletteGrid.cellSize);
                        }
                        else
                        {
                            Debug.LogWarning("Grid component not found from: " + palette.name);
                        }
                    }

                    m_PreviewUtility.camera.transparencySortMode = paletteAsset.transparencySortMode;
                    m_PreviewUtility.camera.transparencySortAxis = paletteAsset.transparencySortAxis;
                }
                else
                {
                    Debug.LogWarning("GridPalette subasset not found from: " + palette.name);
                    m_PreviewUtility.camera.transparencySortMode = TransparencySortMode.Default;
                    m_PreviewUtility.camera.transparencySortAxis = new Vector3(0f, 0f, 1f);
                }

                foreach (var transform in m_PaletteInstance.GetComponentsInChildren<Transform>())
                    transform.gameObject.hideFlags = HideFlags.HideAndDontSave;

                // Show all renderers from Palettes from previous versions
                PreviewRenderUtility.SetEnabledRecursive(m_PaletteInstance, true);

                // Update preview Grid Mesh for new palette instance
                ResetPreviewGridMesh();

                // Restore GridSelection for new palette instance
                if (childPositions != null)
                {
                    var transform = m_PaletteInstance.transform;
                    while (childPositions.Count > 0)
                    {
                        var siblingIndex = childPositions.Pop();
                        if (siblingIndex < transform.childCount)
                            transform = transform.GetChild(siblingIndex);
                    }
                    GridSelection.Select(transform.gameObject, previousGridSelectionPosition);
                }
            }
        }

        public void DestroyPreviewInstance()
        {
            if (m_PaletteInstance != null)
            {
                Undo.ClearUndo(m_PaletteInstance);
                if (GridSelection.active && GridSelection.target == tilemap.gameObject)
                {
                    GridSelection.TransferToStandalone(palette);
                }
                else
                {
                    DestroyImmediate(m_PaletteInstance);
                }
                m_PaletteInstance = null;
            }
        }

        private void ResetPreviewGridMesh()
        {
            if (m_GridMesh != null)
            {
                DestroyImmediate(m_GridMesh);
                m_GridMesh = null;
            }
            m_GridMaterial = null;
        }

        public void FrameEntirePalette()
        {
            Frame(bounds);
        }

        private void Frame(RectInt rect)
        {
            if (grid == null)
                return;

            var position = grid.CellToLocalInterpolated(new Vector3(rect.center.x, rect.center.y, 0));
            position.z = -10f;

            var height = (grid.CellToLocal(new Vector3Int(0, rect.yMax, 0)) - grid.CellToLocal(new Vector3Int(0, rect.yMin, 0))).magnitude;
            var width = (grid.CellToLocal(new Vector3Int(rect.xMax, 0, 0)) - grid.CellToLocal(new Vector3Int(rect.xMin, 0, 0))).magnitude;

            var cellSize = grid.cellSize;
            width += cellSize.x;
            height += cellSize.y;

            var GUIAspect = m_GUIRect.width / m_GUIRect.height;
            var contentAspect = width / height;

            m_PreviewUtility.camera.transform.position = position;
            m_PreviewUtility.camera.orthographicSize = (GUIAspect > contentAspect ? height : width / GUIAspect) / 2f;

            ClampZoomAndPan();
        }

        private void RefreshAllTiles()
        {
            if (tilemap != null)
                tilemap.RefreshAllTiles();
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            instances.Add(this);

            EditorApplication.editorApplicationQuit += EditorApplicationQuit;
            PrefabUtility.prefabInstanceUpdated += PrefabInstanceUpdated;
            Undo.undoRedoPerformed += UndoRedoPerformed;

            m_KeyboardPanningID = GUIUtility.GetPermanentControlID();
            m_MousePanningID = GUIUtility.GetPermanentControlID();

            InitPreviewUtility();
            ResetPreviewInstance();
            SetupPreviewCameraOnInit();
        }

        protected override void OnDisable()
        {
            SavePaletteIfNecessary();
            unlocked = false;
            DestroyPreviewInstance();

            if (m_PreviewUtility != null && m_PreviewUtility.camera != null)
            {
                // Save Preview camera coordinates
                m_CameraPosition = m_PreviewUtility.camera.transform.position;
                m_CameraOrthographicSize = m_PreviewUtility.camera.orthographicSize;
                m_CameraPositionSaved = true;
            }
            if (m_PreviewUtility != null)
                m_PreviewUtility.Cleanup();
            m_PreviewUtility = null;

            Undo.undoRedoPerformed -= UndoRedoPerformed;
            PrefabUtility.prefabInstanceUpdated -= PrefabInstanceUpdated;
            EditorApplication.editorApplicationQuit -= EditorApplicationQuit;

            instances.Remove(this);

            base.OnDisable();
        }

        private void DisplayClipboardText(GUIContent clipboardText, Rect textPosition)
        {
            Color old = GUI.color;
            GUI.color = Color.gray;
            var infoSize = GUI.skin.label.CalcSize(clipboardText);
            Rect rect = new Rect(textPosition.center.x - infoSize.x * .5f, textPosition.center.y - infoSize.y * .5f, infoSize.x, infoSize.y);
            GUI.Label(rect, clipboardText);
            GUI.color = old;
        }

        public void OnClipboardGUI(Rect clipboardPosition)
        {
            if (Event.current.type != EventType.Layout && clipboardPosition.Contains(Event.current.mousePosition) && GridPaintingState.activeGrid != this && unlocked)
            {
                GridPaintingState.activeGrid = this;
                SceneView.RepaintAll();
            }

            // Validate palette (case 1017965)
            GUIContent paletteError = null;
            if (palette == null)
            {
                if (GridPaintingState.palettes.Count == 0)
                    paletteError = Styles.emptyProjectInfo;
                else
                    paletteError = Styles.invalidPaletteInfo;
            }
            else if (palette.GetComponent<Grid>() == null)
            {
                paletteError = Styles.invalidGridInfo;
            }

            if (paletteError != null)
            {
                DisplayClipboardText(paletteError, clipboardPosition);
                return;
            }

            bool oldEnabled = GUI.enabled;
            GUI.enabled = !showNewEmptyClipboardInfo || DragAndDrop.objectReferences.Length > 0;
            if (Event.current.type == EventType.Repaint)
                guiRect = clipboardPosition;

            EditorGUI.BeginChangeCheck();
            OnGUI();
            if (EditorGUI.EndChangeCheck())
                Repaint();

            GUI.enabled = oldEnabled;

            if (showNewEmptyClipboardInfo)
            {
                DisplayClipboardText(Styles.emptyPaletteInfo, clipboardPosition);
            }
        }

        public override void OnGUI()
        {
            if (Mathf.Approximately(guiRect.width, 0f) || Mathf.Approximately(guiRect.height, 0f))
                return;

            UpdateMouseGridPosition();

            HandleDragAndDrop();

            if (m_DelayedResetPaletteInstance)
            {
                ResetPreviewInstance();
                m_DelayedResetPaletteInstance = false;
            }

            if (palette == null)
                return;

            HandlePanAndZoom();
            HandleKeyboardMousePick();

            if (showNewEmptyClipboardInfo)
                return;

            if (Event.current.type == EventType.Repaint && !m_CameraInitializedToBounds)
            {
                Frame(bounds);
                m_CameraInitializedToBounds = true;
            }

            HandleMouseEnterLeave();

            if (guiRect.Contains(Event.current.mousePosition) || Event.current.type != EventType.MouseDown)
                base.OnGUI();

            if (Event.current.type == EventType.Repaint
                || (unlocked && (inEditMode || GridSelectionTool.IsActive())))
            {
                Render();
            }
            else
            {
                RenderSelectedBrushMarquee();
                CallOnPaintSceneGUI(mouseGridPosition);
            }
            m_PreviousMousePosition = Event.current.mousePosition;
        }

        private void OnViewSizeChanged(Rect oldSize, Rect newSize)
        {
            if (Mathf.Approximately(oldSize.height * oldSize.width * newSize.height * newSize.width, 0f))
                return;

            Camera cam = m_PreviewUtility.camera;

            Vector2 sizeDelta = new Vector2(
                newSize.width / LocalToScreenRatio(newSize.height) - oldSize.width / LocalToScreenRatio(oldSize.height),
                newSize.height / LocalToScreenRatio(newSize.height) - oldSize.height / LocalToScreenRatio(oldSize.height));

            cam.transform.Translate(sizeDelta / 2f);

            ClampZoomAndPan();
        }

        private void EditorApplicationQuit()
        {
            SavePaletteIfNecessary();
        }

        private void UndoRedoPerformed()
        {
            if (!unlocked)
                return;

            m_PaletteNeedsSave = true;
            RefreshAllTiles();
            Repaint();
        }

        private void PrefabInstanceUpdated(GameObject updatedPrefab)
        {
            // case 947462: Reset the palette instance after its prefab has been updated as it could have been changed
            if (m_PaletteInstance != null && PrefabUtility.GetCorrespondingObjectFromSource(updatedPrefab) == palette && !GridPaintingState.savingPalette)
            {
                m_PaletteNeedsSave = true;
                RefreshAllTiles();
                Repaint();
            }
        }

        private void HandlePanAndZoom()
        {
            switch (Event.current.type)
            {
                case EventType.MouseDown:
                    if (MousePanningEvent() && guiRect.Contains(Event.current.mousePosition) && GUIUtility.hotControl == 0)
                    {
                        GUIUtility.hotControl = m_MousePanningID;
                        m_MouseZoomInitialPosition = Event.current.mousePosition;
                        Event.current.Use();
                    }
                    break;
                case EventType.ValidateCommand:
                    if (Event.current.commandName == EventCommandNames.FrameSelected)
                    {
                        Event.current.Use();
                    }
                    break;
                case EventType.ExecuteCommand:
                    if (Event.current.commandName == EventCommandNames.FrameSelected)
                    {
                        if (m_ActivePick.HasValue)
                        {
                            var rect = new RectInt(m_ActivePick.Value.x, m_ActivePick.Value.y,
                                m_ActivePick.Value.size.x, m_ActivePick.Value.size.y);
                            Frame(rect);
                        }
                        else
                            FrameEntirePalette();
                        Event.current.Use();
                    }
                    break;
                case EventType.ScrollWheel:
                    if (guiRect.Contains(Event.current.mousePosition))
                    {
                        HandleMouseZoom(Event.current.mousePosition);
                        Event.current.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == m_MousePanningID)
                    {
                        if (Event.current.alt && Event.current.button == 1)
                        {
                            HandleMouseZoom(m_MouseZoomInitialPosition);
                        }
                        else
                        {
                            Vector3 delta = new Vector3(-Event.current.delta.x, Event.current.delta.y, 0f) / LocalToScreenRatio();
                            m_PreviewUtility.camera.transform.Translate(delta);
                            ClampZoomAndPan();
                        }
                        Event.current.Use();
                    }
                    break;
                case EventType.MouseMove: // Fix mouse cursor being stuck when panning ended outside our window
                    if (GUIUtility.hotControl == m_MousePanningID && !MousePanningEvent())
                        GUIUtility.hotControl = 0;
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == m_MousePanningID)
                    {
                        ClampZoomAndPan();
                        GUIUtility.hotControl = 0;
                        Event.current.Use();
                    }
                    break;
                case EventType.KeyDown:
                    if ((GUIUtility.hotControl == 0 || GUIUtility.hotControl == m_KeyboardPanningID) && !Event.current.shift)
                    {
                        switch (Event.current.keyCode)
                        {
                            case KeyCode.LeftArrow:
                                m_KeyboardPanning.x = -k_KeyboardPanningSpeed / LocalToScreenRatio();
                                GUIUtility.hotControl = m_KeyboardPanningID;
                                Event.current.Use();
                                break;
                            case KeyCode.RightArrow:
                                m_KeyboardPanning.x = k_KeyboardPanningSpeed / LocalToScreenRatio();
                                GUIUtility.hotControl = m_KeyboardPanningID;
                                Event.current.Use();
                                break;
                            case KeyCode.UpArrow:
                                m_KeyboardPanning.y = k_KeyboardPanningSpeed / LocalToScreenRatio();
                                GUIUtility.hotControl = m_KeyboardPanningID;
                                Event.current.Use();
                                break;
                            case KeyCode.DownArrow:
                                m_KeyboardPanning.y = -k_KeyboardPanningSpeed / LocalToScreenRatio();
                                GUIUtility.hotControl = m_KeyboardPanningID;
                                Event.current.Use();
                                break;
                        }
                    }
                    break;
                case EventType.KeyUp:
                    if (GUIUtility.hotControl == m_KeyboardPanningID)
                    {
                        m_KeyboardPanning = Vector3.zero;
                        GUIUtility.hotControl = 0;
                        Event.current.Use();
                    }
                    break;
                case EventType.Repaint:
                    if (GUIUtility.hotControl == m_KeyboardPanningID)
                    {
                        m_PreviewUtility.camera.transform.Translate(m_KeyboardPanning);
                        ClampZoomAndPan();
                        Repaint();
                    }

                    if (GUIUtility.hotControl == m_MousePanningID)
                        EditorGUIUtility.AddCursorRect(guiRect, MouseCursor.Pan);

                    break;
            }
        }

        private void HandleMouseZoom(Vector2 currentMousePosition)
        {
            float zoomDelta = HandleUtility.niceMouseDeltaZoom * (Event.current.shift ? -9 : -3) * k_ZoomSpeed;
            Camera camera = m_PreviewUtility.camera;
            Vector3 oldLocalPos = ScreenToLocal(currentMousePosition);
            camera.orthographicSize = Mathf.Max(.0001f, camera.orthographicSize * (1 + zoomDelta * .001f));
            ClampZoomAndPan();
            Vector3 newLocalPos = ScreenToLocal(currentMousePosition);
            Vector3 localDelta = newLocalPos - oldLocalPos;
            camera.transform.position -= localDelta;
            ClampZoomAndPan();
        }

        private void HandleKeyboardMousePick()
        {
            if (GUIUtility.hotControl == 0 || GUIUtility.hotControl == m_KeyboardPanningID)
            {
                if (Event.current.type == EventType.KeyDown && Event.current.shift && m_ActivePick.HasValue)
                {
                    var delta = Vector3Int.zero;
                    switch (Event.current.keyCode)
                    {
                        case KeyCode.LeftArrow:
                            delta = Vector3Int.left;
                            break;
                        case KeyCode.RightArrow:
                            delta = Vector3Int.right;
                            Event.current.Use();
                            break;
                        case KeyCode.UpArrow:
                            delta = Vector3Int.up;
                            Event.current.Use();
                            break;
                        case KeyCode.DownArrow:
                            delta = Vector3Int.down;
                            break;
                    }

                    if (delta != Vector3Int.zero)
                    {
                        disableOnBrushPicked = true;
                        PickBrush(new BoundsInt(m_ActivePick.Value.position + delta, m_ActivePick.Value.size),
                            m_ActivePivot);
                        disableOnBrushPicked = false;
                        Event.current.Use();
                    }
                }
            }
        }

        private static bool MousePanningEvent()
        {
            return (Event.current.button == 0 && Event.current.alt || Event.current.button > 0);
        }

        private void ClampZoomAndPan()
        {
            float pixelsPerCell = grid.cellSize.y * LocalToScreenRatio();
            if (pixelsPerCell < k_MinZoom)
                m_PreviewUtility.camera.orthographicSize = (grid.cellSize.y * guiRect.height) / (k_MinZoom * 2f);
            else if (pixelsPerCell > k_MaxZoom)
                m_PreviewUtility.camera.orthographicSize = (grid.cellSize.y * guiRect.height) / (k_MaxZoom * 2f);

            Camera cam = m_PreviewUtility.camera;
            float cameraOrthographicSize = cam.orthographicSize;
            Rect r = paddedBounds;

            Vector3 camPos = cam.transform.position;
            Vector2 camMin = camPos - new Vector3(cameraOrthographicSize * (guiRect.width / guiRect.height), cameraOrthographicSize);
            Vector2 camMax = camPos + new Vector3(cameraOrthographicSize * (guiRect.width / guiRect.height), cameraOrthographicSize);

            if (camMin.x < r.min.x)
            {
                camPos += new Vector3(r.min.x - camMin.x, 0f, 0f);
            }
            if (camMin.y < r.min.y)
            {
                camPos += new Vector3(0f, r.min.y - camMin.y, 0f);
            }
            if (camMax.x > r.max.x)
            {
                camPos += new Vector3(r.max.x - camMax.x, 0f, 0f);
            }
            if (camMax.y > r.max.y)
            {
                camPos += new Vector3(0f, r.max.y - camMax.y, 0f);
            }

            camPos.Set(camPos.x, camPos.y, -10f);

            cam.transform.position = camPos;

            DestroyImmediate(m_GridMesh);
            m_GridMesh = null;
        }

        private void Render()
        {
            if (guiRect.width <= 0f || guiRect.height <= 0f)
                return;

            if (m_GridMesh != null && GetGridHash() != m_LastGridHash)
            {
                ResetPreviewInstance();
                ResetPreviewGridMesh();
            }

            using (new PreviewInstanceScope(guiRect, m_PreviewUtility, paletteInstance, GridPaintingState.drawGizmos))
            {
                m_PreviewUtility.Render(true);
                if (GridPaintingState.drawGridGizmo)
                    RenderGrid();
                CallOnPaintSceneGUI(mouseGridPosition);
                if (GridPaintingState.drawGizmos)
                {
                    // Set CameraType to SceneView to force Gizmos to be drawn
                    var storedType = m_PreviewUtility.camera.cameraType;
                    m_PreviewUtility.camera.cameraType = CameraType.SceneView;
                    Handles.Internal_DoDrawGizmos(m_PreviewUtility.camera);
                    m_PreviewUtility.camera.cameraType = storedType;
                }
            }

            RenderDragAndDropPreview();
            RenderSelectedBrushMarquee();
            CallOnSceneGUI();

            m_PreviewUtility.EndAndDrawPreview(guiRect);
            m_LastGridHash = GetGridHash();
        }

        private int GetGridHash()
        {
            var gridToHash = prefabGrid;
            if (gridToHash == null)
                return 0;

            int hash = gridToHash.GetHashCode();
            unchecked
            {
                hash = hash * 33 + gridToHash.cellGap.GetHashCode();
                hash = hash * 33 + gridToHash.cellLayout.GetHashCode();
                hash = hash * 33 + gridToHash.cellSize.GetHashCode();
                hash = hash * 33 + gridToHash.cellSwizzle.GetHashCode();
                hash = hash * 33 + SceneViewGridManager.sceneViewGridComponentGizmo.Color.GetHashCode();
            }
            return hash;
        }

        private void RenderDragAndDropPreview()
        {
            if (!activeDragAndDrop || m_HoverData == null || m_HoverData.Count == 0)
                return;

            var rect = TileDragAndDrop.GetMinMaxRect(m_HoverData.Keys);
            rect.position += mouseGridPosition;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            GridEditorUtility.DrawGridMarquee(grid, new BoundsInt(new Vector3Int(rect.xMin, rect.yMin, zPosition), new Vector3Int(rect.width, rect.height, 1)), Color.white);
        }

        private void RenderGrid()
        {
            // MeshTopology.Lines doesn't give nice pixel perfect grid so we have to have separate codepath with MeshTopology.Quads specially for palette window here
            if (m_GridMesh == null && grid.cellLayout == GridLayout.CellLayout.Rectangle)
                m_GridMesh = GridEditorUtility.GenerateCachedGridMesh(grid, k_GridColor, 1f / LocalToScreenRatio(), paddedBoundsInt, MeshTopology.Quads);

            GridEditorUtility.DrawGridGizmo(grid, grid.transform, k_GridColor, ref m_GridMesh, ref m_GridMaterial);
        }

        private class PreviewInstanceScope : IDisposable
        {
            private readonly bool m_OldFog;
            private readonly bool m_DrawGizmos;
            private readonly Transform[] m_PaletteTransforms;

            public PreviewInstanceScope(Rect guiRect, PreviewRenderUtility previewRenderUtility, GameObject paletteInstance, bool drawGizmos)
            {
                m_DrawGizmos = drawGizmos;
                m_OldFog = RenderSettings.fog;

                previewRenderUtility.BeginPreview(guiRect, null);

                // Draw Background here with user preference color
                Graphics.DrawTexture(new Rect(0.0f, 0.0f
                        , (float) 2 * EditorGUIUtility.pixelsPerPoint * guiRect.width
                        , (float) 2 * EditorGUIUtility.pixelsPerPoint * guiRect.height)
                    , (Texture) Texture2D.grayTexture, new Rect(0.0f, 0.0f, 1f, 1f)
                    , 0, 0, 0, 0
                    , tilePaletteBackgroundColor.Color, (Material) null);

                Unsupported.SetRenderSettingsUseFogNoDirty(false);
                if (m_DrawGizmos)
                {
                    m_PaletteTransforms = paletteInstance.GetComponentsInChildren<Transform>();
                    foreach (var transform in m_PaletteTransforms)
                        transform.gameObject.hideFlags = HideFlags.None;
                    // Case 1199516: Set Dirty on palette instance to force a refresh on gizmo drawing
                    EditorUtility.SetDirty(paletteInstance);
                    Unsupported.SceneTrackerFlushDirty();
                }
                var renderers = paletteInstance.GetComponentsInChildren<Renderer>();
                foreach (var renderer in renderers)
                {
                    renderer.allowOcclusionWhenDynamic = false;
                }
                previewRenderUtility.AddManagedGO(paletteInstance);
                Handles.DrawCameraImpl(guiRect, previewRenderUtility.camera, DrawCameraMode.Textured, false, new DrawGridParameters(), true, false);
            }

            public void Dispose()
            {
                if (m_DrawGizmos && m_PaletteTransforms != null)
                {
                    foreach (var transform in m_PaletteTransforms)
                        transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
                }
                Unsupported.SetRenderSettingsUseFogNoDirty(m_OldFog);
            }
        }

        private void HandleDragAndDrop()
        {
            if (DragAndDrop.objectReferences.Length == 0 || !guiRect.Contains(Event.current.mousePosition))
                return;

            switch (Event.current.type)
            {
                //TODO: Cache this
                case EventType.DragUpdated:
                {
                    List<Texture2D> sheets = TileDragAndDrop.GetValidSpritesheets(DragAndDrop.objectReferences);
                    List<Sprite> sprites = TileDragAndDrop.GetValidSingleSprites(DragAndDrop.objectReferences);
                    List<TileBase> tiles = TileDragAndDrop.GetValidTiles(DragAndDrop.objectReferences);
                    m_HoverData = TileDragAndDrop.CreateHoverData(sheets, sprites, tiles, tilemap.cellLayout);

                    if (m_HoverData != null && m_HoverData.Count > 0)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        Event.current.Use();
                        GUI.changed = true;
                    }
                }
                break;
                case EventType.DragPerform:
                {
                    if (m_HoverData == null || m_HoverData.Count == 0)
                        return;

                    RegisterUndo();

                    var wasEmpty = TilemapIsEmpty(tilemap);

                    var targetPosition = mouseGridPosition;
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    var tileSheet = TileDragAndDrop.ConvertToTileSheet(m_HoverData);
                    var i = 0;
                    foreach (var item in m_HoverData)
                    {
                        if (i >= tileSheet.Count)
                            break;

                        var offset = Vector3.zero;
                        if (item.Value.hasOffset)
                        {
                            offset = item.Value.positionOffset - tilemap.tileAnchor;

                            var cellSize = tilemap.cellSize;
                            if (wasEmpty)
                            {
                                cellSize = item.Value.scaleFactor;
                            }
                            offset.x *= cellSize.x;
                            offset.y *= cellSize.y;
                            offset.z *= cellSize.z;
                        }

                        SetTile(tilemap
                            , targetPosition + item.Key
                            , tileSheet[i++]
                            , Color.white
                            , Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one));
                    }
                    OnPaletteChanged();

                    m_PaletteNeedsSave = true;
                    FlushHoverData();
                    GUI.changed = true;
                    SavePaletteIfNecessary();

                    if (wasEmpty)
                    {
                        ResetPreviewInstance();
                        FrameEntirePalette();
                    }

                    Event.current.Use();
                    GUIUtility.ExitGUI();
                }
                break;
                case EventType.Repaint:
                    // Handled in Render()
                    break;
            }

            if (m_HoverData != null && (
                Event.current.type == EventType.DragExited ||
                Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.None;
                FlushHoverData();
                Event.current.Use();
            }
        }

        internal void SetTile(Tilemap tilemapTarget, Vector2Int position, TileBase tile, Color color, Matrix4x4 matrix)
        {
            Vector3Int pos3 = new Vector3Int(position.x, position.y, zPosition);
            tilemapTarget.SetTile(pos3, tile);
            tilemapTarget.SetColor(pos3, color);
            tilemapTarget.SetTransformMatrix(pos3, tilemapTarget.GetTransformMatrix(pos3) * matrix);
        }

        protected override void Paint(Vector3Int position)
        {
            if (gridBrush == null)
                return;

            gridBrush.Paint(grid, brushTarget, position);
            OnPaletteChanged();
        }

        protected override void Erase(Vector3Int position)
        {
            if (gridBrush == null)
                return;

            gridBrush.Erase(grid, brushTarget, position);
            OnPaletteChanged();
        }

        protected override void BoxFill(BoundsInt position)
        {
            if (gridBrush == null)
                return;

            gridBrush.BoxFill(grid, brushTarget, position);
            OnPaletteChanged();
        }

        protected override void BoxErase(BoundsInt position)
        {
            if (gridBrush == null)
                return;

            gridBrush.BoxErase(grid, brushTarget, position);
            OnPaletteChanged();
        }

        protected override void FloodFill(Vector3Int position)
        {
            if (gridBrush == null)
                return;

            gridBrush.FloodFill(grid, brushTarget, position);
            OnPaletteChanged();
        }

        protected override void PickBrush(BoundsInt position, Vector3Int pickingStart)
        {
            if (grid == null || gridBrush == null)
                return;

            gridBrush.Pick(grid, brushTarget, position, pickingStart);

            if (!InGridEditMode())
                TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));

            m_ActivePick = position;
            m_ActivePivot = pickingStart;

            if (!disableOnBrushPicked)
                onBrushPicked?.Invoke();
        }

        protected override void Select(BoundsInt position)
        {
            if (grid)
            {
                GridSelection.Select(brushTarget, position);
                gridBrush.Select(grid, brushTarget, position);
            }
        }

        protected override void Move(BoundsInt from, BoundsInt to)
        {
            if (grid)
                gridBrush.Move(grid, brushTarget, from, to);
        }

        protected override void MoveStart(BoundsInt position)
        {
            if (grid)
                gridBrush.MoveStart(grid, brushTarget, position);
        }

        protected override void MoveEnd(BoundsInt position)
        {
            if (grid)
            {
                gridBrush.MoveEnd(grid, brushTarget, position);
                OnPaletteChanged();
            }
        }

        protected override bool CustomTool(bool isToolHotControl, TilemapEditorTool tool, Vector3Int position)
        {
            var executed = false;
            if (grid)
            {
                executed = tool.HandleTool(isToolHotControl, grid, brushTarget, position);
                if (executed)
                    OnPaletteChanged();
            }
            return executed;
        }

        protected override bool IsMouseUpInWindow()
        {
            return Event.current.type == EventType.MouseUp && guiRect.Contains(Event.current.mousePosition);
        }

        public override void Repaint()
        {
            if (m_VisualElement != null)
                m_VisualElement.MarkDirtyRepaint();
        }

        protected override void ClearGridSelection()
        {
            GridSelection.Clear();
        }

        public override bool isActive => grid != null;

        protected override void OnBrushPickStarted()
        {
        }

        protected override void OnBrushPickDragged(BoundsInt position)
        {
            m_ActivePick = position;
        }

        protected override void OnBrushPickCancelled()
        {
            m_ActivePick = null;
            m_ActivePivot = Vector3Int.zero;
        }

        private void PingTileAsset(RectInt rect)
        {
            // Only able to ping asset if only one asset is selected
            if (rect.size == Vector2Int.zero && tilemap != null)
            {
                TileBase tile = tilemap.GetTile(new Vector3Int(rect.xMin, rect.yMin, zPosition));
                EditorGUIUtility.PingObject(tile);
                Selection.activeObject = tile;
            }
        }

        protected override bool ValidateFloodFillPosition(Vector3Int position)
        {
            return true;
        }

        protected override bool PickingIsDefaultTool()
        {
            return !m_Unlocked;
        }

        protected override bool CanPickOutsideEditMode()
        {
            return true;
        }

        protected override GridLayout.CellLayout CellLayout()
        {
            if (grid != null)
                return grid.cellLayout;
            return GridLayout.CellLayout.Rectangle;
        }

        protected override Vector2Int ScreenToGrid(Vector2 screenPosition)
        {
            Vector2 local = ScreenToLocal(screenPosition);
            Vector3Int result3 = grid.LocalToCell(local);
            Vector2Int result = new Vector2Int(result3.x, result3.y);
            return result;
        }

        private void RenderSelectedBrushMarquee()
        {
            if (!activeDragAndDrop && !unlocked && m_ActivePick.HasValue)
            {
                DrawSelectionGizmo(m_ActivePick.Value);
            }
        }

        private void DrawSelectionGizmo(BoundsInt selectionBounds)
        {
            if (Event.current.type != EventType.Repaint || !GUI.enabled)
                return;

            Color color = Color.white;
            if (isPicking)
                color = Color.cyan;

            GridEditorUtility.DrawGridMarquee(grid, new BoundsInt(new Vector3Int(selectionBounds.xMin, selectionBounds.yMin, 0), new Vector3Int(selectionBounds.size.x, selectionBounds.size.y, 1)), color);
        }

        private void HandleMouseEnterLeave()
        {
            if (guiRect.Contains(Event.current.mousePosition))
            {
                if (m_PreviousMousePosition.HasValue && !guiRect.Contains(m_PreviousMousePosition.Value) || !m_PreviousMousePosition.HasValue)
                {
                    if (GridPaintingState.activeBrushEditor != null)
                    {
                        GridPaintingState.activeBrushEditor.OnMouseEnter();
                    }
                }
            }
            else
            {
                if (m_PreviousMousePosition.HasValue && guiRect.Contains(m_PreviousMousePosition.Value) && !guiRect.Contains(Event.current.mousePosition))
                {
                    if (GridPaintingState.activeBrushEditor != null)
                    {
                        GridPaintingState.activeBrushEditor.OnMouseLeave();
                        Repaint();
                    }
                }
            }
        }

        private void CallOnSceneGUI()
        {
            var gridLayout = tilemap != null ? tilemap : grid as GridLayout;
            bool hasSelection = GridSelection.active  && GridSelection.target == brushTarget;
            if (hasSelection)
            {
                var rect = new RectInt(GridSelection.position.xMin, GridSelection.position.yMin, GridSelection.position.size.x, GridSelection.position.size.y);
                var brushBounds = new BoundsInt(new Vector3Int(rect.x, rect.y, zPosition), new Vector3Int(rect.width, rect.height, 1));
                GridBrushEditorBase.OnSceneGUIInternal(gridLayout, brushTarget, brushBounds, EditTypeToBrushTool(ToolManager.activeToolType), m_MarqueeStart.HasValue || executing);
            }
            if (GridPaintingState.activeBrushEditor != null)
            {
                GridPaintingState.activeBrushEditor.OnSceneGUI(gridLayout, brushTarget);
                if (hasSelection)
                {
                    GridPaintingState.activeBrushEditor.OnSelectionSceneGUI(gridLayout, brushTarget);
                    if (GridSelectionTool.IsActive() && unlocked)
                    {
                        var tool = EditorToolManager.activeTool as GridSelectionTool;
                        tool.OnToolGUI();
                    }
                }
            }
        }

        private void CallOnPaintSceneGUI(Vector2Int position)
        {
            if (!activeDragAndDrop && !unlocked && !TilemapEditorTool.IsActive(typeof(SelectTool)) && !TilemapEditorTool.IsActive(typeof(PickingTool)))
                return;

            var hasSelection = GridSelection.active && GridSelection.target == brushTarget;
            if (!hasSelection && GridPaintingState.activeGrid != this)
                return;

            var brush = GridPaintingState.gridBrush;
            if (brush == null)
                return;

            var rect = new RectInt(position, new Vector2Int(1, 1));

            if (m_MarqueeStart.HasValue)
                rect = GridEditorUtility.GetMarqueeRect(position, m_MarqueeStart.Value);
            else if (hasSelection)
                rect = new RectInt(GridSelection.position.xMin, GridSelection.position.yMin, GridSelection.position.size.x, GridSelection.position.size.y);

            var gridLayout = tilemap != null ? tilemap.layoutGrid : grid as GridLayout;
            var brushBounds = new BoundsInt(new Vector3Int(rect.x, rect.y, zPosition), new Vector3Int(rect.width, rect.height, 1));

            if (GridPaintingState.activeBrushEditor != null)
                GridPaintingState.activeBrushEditor.OnPaintSceneGUI(gridLayout, brushTarget, brushBounds,
                    EditTypeToBrushTool(ToolManager.activeToolType),
                    m_MarqueeStart.HasValue || executing);
            else // Fallback when user hasn't defined custom editor
                GridBrushEditorBase.OnPaintSceneGUIInternal(gridLayout, Selection.activeGameObject, brushBounds,
                    EditTypeToBrushTool(ToolManager.activeToolType),
                    m_MarqueeStart.HasValue || executing);
        }

        protected override void RegisterUndo()
        {
            if (palette != null && paletteInstance != null)
                Undo.RegisterFullObjectHierarchyUndo(paletteInstance, "Edit Palette");
        }

        private void OnPaletteChanged()
        {
            m_PaletteUsed = true;
            m_PaletteNeedsSave = true;
            Undo.FlushUndoRecordObjects();
        }

        public void CheckRevertIfChanged(string[] paths)
        {
            if (paths != null && m_PaletteNeedsSave && palette != null)
            {
                var currentPalettePath = AssetDatabase.GetAssetPath(palette);
                foreach (var path in paths)
                {
                    if (currentPalettePath == path)
                    {
                        m_PaletteNeedsSave = false;
                        ResetPreviewInstance();
                        Debug.LogWarningFormat(palette, paletteSavedOutsideClipboard, palette.name);
                        break;
                    }
                }
            }
        }

        public bool SavePaletteIfNecessary()
        {
            bool needsSave = m_PaletteNeedsSave;
            if (needsSave)
            {
                SavePalette();
                m_PaletteNeedsSave = false;
            }
            return needsSave;
        }

        private void SavePalette()
        {
            if (palette != null && paletteInstance != null)
            {
                TilePaletteSaveUtility.SaveTilePalette(palette, paletteInstance);
                ResetPreviewInstance();
                Repaint();
            }
        }

        public Vector2 GridToScreen(Vector2 gridPosition)
        {
            Vector3 gridPosition3 = new Vector3(gridPosition.x, gridPosition.y, 0);
            return LocalToScreen(grid.CellToLocalInterpolated(gridPosition3));
        }

        public Vector2 ScreenToLocal(Vector2 screenPosition)
        {
            Vector2 viewPosition = m_PreviewUtility.camera.transform.position;
            screenPosition -= new Vector2(guiRect.xMin, guiRect.yMin);
            Vector2 offsetFromCenter = new Vector2(screenPosition.x - guiRect.width * .5f, guiRect.height * .5f - screenPosition.y);
            return viewPosition + offsetFromCenter / LocalToScreenRatio();
        }

        protected Vector2 LocalToScreen(Vector2 localPosition)
        {
            Vector2 viewPosition = m_PreviewUtility.camera.transform.position;
            Vector2 offsetFromCenter = new Vector2(localPosition.x - viewPosition.x, viewPosition.y - localPosition.y);
            return offsetFromCenter * LocalToScreenRatio() + new Vector2(guiRect.width * .5f + guiRect.xMin, guiRect.height * .5f + guiRect.yMin);
        }

        private float LocalToScreenRatio()
        {
            return guiRect.height / (m_PreviewUtility.camera.orthographicSize * 2f);
        }

        private float LocalToScreenRatio(float viewHeight)
        {
            return viewHeight / (m_PreviewUtility.camera.orthographicSize * 2f);
        }

        private static bool TilemapIsEmpty(Tilemap tilemap)
        {
            return tilemap.GetUsedTilesCount() == 0;
        }

        public void UnlockAndEdit()
        {
            unlocked = true;
            m_PaletteNeedsSave = true;
        }

        // TODO: Better way of clearing caches than AssetPostprocessor
        public class AssetProcessor : AssetPostprocessor
        {
            public override int GetPostprocessOrder()
            {
                return int.MaxValue;
            }

            private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromPath)
            {
                foreach (var clipboard in instances)
                {
                    clipboard.DelayedResetPreviewInstance();
                }
            }
        }

        public class PaletteAssetModificationProcessor : AssetModificationProcessor
        {
            static void OnWillCreateAsset(string assetName)
            {
                SavePalettesIfRequired(null);
            }

            static string[] OnWillSaveAssets(string[] paths)
            {
                SavePalettesIfRequired(paths);
                return paths;
            }

            static void SavePalettesIfRequired(string[] paths)
            {
                if (GridPaintingState.savingPalette)
                    return;

                foreach (var clipboard in instances)
                {
                    if (clipboard.isModified)
                    {
                        clipboard.CheckRevertIfChanged(paths);
                        clipboard.SavePaletteIfNecessary();
                        clipboard.Repaint();
                    }
                }
            }
        }
    }
}

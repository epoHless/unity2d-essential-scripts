using System;
using UnityEditor.Experimental;
using UnityEngine;

namespace UnityEditor.Tilemaps
{
    internal class GridPaintTargetsDropdown : PopupWindowContent
    {
        class Styles
        {
            public class IconState
            {
                public GUIContent visible;
                public GUIContent hidden;
                public GUIContent ping;
            }

            public GUIStyle menuItem = "MenuItem";
            public static readonly Color backgroundColor = EditorResources.GetStyle("game-object-tree-view-scene-visibility")
                .GetColor("background-color");

            public static readonly Color hoveredBackgroundColor = EditorResources.GetStyle("game-object-tree-view-scene-visibility")
                .GetColor("-unity-object-hovered-color");

            public static readonly Color selectedBackgroundColor = EditorResources.GetStyle("game-object-tree-view-scene-visibility")
                .GetColor("-unity-object-selected-color");

            public static readonly Color selectedNoFocusBackgroundColor = EditorResources.GetStyle("game-object-tree-view-scene-visibility")
                .GetColor("-unity-object-selected-no-focus-color");

            public static readonly GUIStyle sceneVisibilityStyle = "SceneVisibility";

            public static readonly IconState iconNormal = new()
            {
                visible = EditorGUIUtility.TrIconContent("scenevis_visible"),
                hidden = EditorGUIUtility.TrIconContent("scenevis_hidden"),
                ping = EditorGUIUtility.TrIconContent("Packages/com.unity.2d.tilemap/Editor/Icons/EditorUI.Target.png"),
            };
            public static readonly IconState iconHovered = new()
            {
                visible = EditorGUIUtility.TrIconContent("scenevis_visible_hover"),
                hidden = EditorGUIUtility.TrIconContent("scenevis_hidden_hover"),
                ping = EditorGUIUtility.TrIconContent("Packages/com.unity.2d.tilemap/Editor/Icons/EditorUI.TargetHover.png"),
            };


            public static Color GetItemBackgroundColor(bool isHovered, bool isSelected, bool isFocused)
            {
                if (isSelected)
                {
                    if (isFocused)
                        return selectedBackgroundColor;

                    return selectedNoFocusBackgroundColor;
                }

                if (isHovered)
                    return hoveredBackgroundColor;

                return backgroundColor;
            }
        }
        static Styles s_Styles;

        IFlexibleMenuItemProvider m_ItemProvider;
        FlexibleMenuModifyItemUI m_ModifyItemUI;
        readonly Action<int, object> m_ItemClickedCallback;
        Vector2 m_ScrollPosition = Vector2.zero;
        bool m_ShowAddNewPresetItem;
        int m_HoverIndex;
        int[] m_SeperatorIndices;
        float m_CachedWidth = -1f;
        float m_MinTextWidth;

        const float LineHeight = 18f;
        const float SeparatorHeight = 8f;
        int maxIndex { get { return m_ShowAddNewPresetItem ? m_ItemProvider.Count() : m_ItemProvider.Count() - 1; } }
        public int selectedIndex { get; set; }
        protected float minTextWidth { get { return m_MinTextWidth; } set { m_MinTextWidth = value; ClearCachedWidth(); } }

        internal class MenuItemProvider : IFlexibleMenuItemProvider
        {
            public int Count()
            {
                return GridPaintingState.validTargets != null ? GridPaintingState.validTargets.Length : 0;
            }

            public object GetItem(int index)
            {
                return GridPaintingState.validTargets != null ? GridPaintingState.validTargets[index] : GridPaintingState.scenePaintTarget;
            }

            public int Add(object obj)
            {
                throw new NotImplementedException();
            }

            public void Replace(int index, object newPresetObject)
            {
                throw new NotImplementedException();
            }

            public void Remove(int index)
            {
                throw new NotImplementedException();
            }

            public object Create()
            {
                throw new NotImplementedException();
            }

            public void Move(int index, int destIndex, bool insertAfterDestIndex)
            {
                throw new NotImplementedException();
            }

            public string GetName(int index)
            {
                return GridPaintingState.validTargets != null ? GridPaintingState.validTargets[index].name : GridPaintingState.scenePaintTarget.name;
            }

            public bool IsModificationAllowed(int index)
            {
                return false;
            }

            public int[] GetSeperatorIndices()
            {
                return new int[0];
            }
        }

        // itemClickedCallback arguments is clicked index, clicked item object
        public GridPaintTargetsDropdown(IFlexibleMenuItemProvider itemProvider, int selectionIndex, FlexibleMenuModifyItemUI modifyItemUi, Action<int, object> itemClickedCallback, float minWidth)
        {
            m_ItemProvider = itemProvider;
            m_ModifyItemUI = modifyItemUi;
            m_ItemClickedCallback = itemClickedCallback;
            m_SeperatorIndices = m_ItemProvider.GetSeperatorIndices();
            selectedIndex = selectionIndex;
            m_ShowAddNewPresetItem = m_ModifyItemUI != null;
            m_MinTextWidth = minWidth;
        }

        public override Vector2 GetWindowSize()
        {
            return CalcSize();
        }

        public override void OnGUI(Rect rect)
        {
            if (s_Styles == null)
                s_Styles = new Styles();

            Event evt = Event.current;

            Rect contentRect = new Rect(0, 0, 1, CalcSize().y);
            m_ScrollPosition = GUI.BeginScrollView(rect, m_ScrollPosition, contentRect);
            {
                float curY = 0f;
                for (int i = 0; i <= maxIndex; ++i)
                {
                    int itemControlID = i + 1000000;
                    Rect fullRect = new Rect(0, curY, rect.width, LineHeight);
                    Rect visRect = new Rect(0, curY, 16, LineHeight);
                    Rect pingRect = new Rect(16, curY, 16, LineHeight);
                    Rect backRect = new Rect(0, curY, 32, LineHeight);
                    Rect itemRect = new Rect(16 + 16, curY, rect.width - 16 - 16, LineHeight);
                    bool addSeparator = Array.IndexOf(m_SeperatorIndices, i) >= 0;

                    // Handle event
                    switch (evt.type)
                    {
                        case EventType.Repaint:
                            bool hover = false;
                            if (m_HoverIndex == i)
                            {
                                if (fullRect.Contains(evt.mousePosition))
                                    hover = true;
                                else
                                    m_HoverIndex = -1;
                            }
                            var isItemVisible = IsVisible(i);

                            using (new GUI.BackgroundColorScope(Styles.GetItemBackgroundColor(hover, hover, hover)))
                            {
                                GUI.Label(backRect, GUIContent.none, GameObjectTreeViewGUI.GameObjectStyles.hoveredItemBackgroundStyle);
                            }
                            if (hover || !isItemVisible)
                            {
                                var isVisHover = visRect.Contains(evt.mousePosition);
                                var visIconState = isVisHover
                                    ? Styles.iconHovered
                                    : Styles.iconNormal;
                                var visIcon = isItemVisible ? visIconState.visible : visIconState.hidden;
                                GUI.Button(visRect, visIcon, Styles.sceneVisibilityStyle);
                            }
                            if (hover)
                            {
                                var isPingHover = pingRect.Contains(evt.mousePosition);
                                var pingIconState = isPingHover
                                    ? Styles.iconHovered
                                    : Styles.iconNormal;
                                GUI.Button(pingRect, pingIconState.ping, Styles.sceneVisibilityStyle);
                            }

                            using (new EditorGUI.DisabledScope(!isItemVisible))
                                s_Styles.menuItem.Draw(itemRect, GUIContent.Temp(m_ItemProvider.GetName(i)), hover, false, i == selectedIndex, false);
                            break;

                        case EventType.MouseDown:
                            if (evt.button == 0 && visRect.Contains(evt.mousePosition))
                            {
                                GUIUtility.hotControl = itemControlID;
                                if (evt.clickCount == 1)
                                {
                                    GUIUtility.hotControl = 0;
                                    ToggleVisibility(i, !evt.alt);
                                    evt.Use();
                                }
                            }
                            if (evt.button == 0 && pingRect.Contains(evt.mousePosition))
                            {
                                GUIUtility.hotControl = itemControlID;
                                if (evt.clickCount == 1)
                                {
                                    GUIUtility.hotControl = 0;
                                    PingItem(i);
                                    evt.Use();
                                }
                            }
                            if (evt.button == 0 && itemRect.Contains(evt.mousePosition) && IsVisible(i))
                            {
                                GUIUtility.hotControl = itemControlID;
                                if (evt.clickCount == 1)
                                {
                                    GUIUtility.hotControl = 0;
                                    SelectItem(i);
                                    editorWindow.Close();
                                    evt.Use();
                                }
                            }
                            break;
                        case EventType.MouseUp:
                            if (GUIUtility.hotControl == itemControlID)
                            {
                                GUIUtility.hotControl = 0;
                            }
                            break;
                        case EventType.MouseMove:
                            if (fullRect.Contains(evt.mousePosition))
                            {
                                m_HoverIndex = i;
                            }
                            else if (m_HoverIndex == i)
                            {
                                m_HoverIndex = -1;
                            }
                            Repaint();
                            break;
                    }

                    curY += LineHeight;
                    if (addSeparator)
                        curY += SeparatorHeight;
                } // end foreach item
            } GUI.EndScrollView();
        }

        void SelectItem(int index)
        {
            selectedIndex = index;
            if (m_ItemClickedCallback != null && index >= 0)
                m_ItemClickedCallback(index, m_ItemProvider.GetItem(index));
        }

        bool IsVisible(int index)
        {
            var obj = m_ItemProvider.GetItem(index) as GameObject;
            if (obj != null)
                return !SceneVisibilityManager.instance.IsHidden(obj);
            return false;
        }

        void ToggleVisibility(int index, bool includeDescendants)
        {
            var obj = m_ItemProvider.GetItem(index) as GameObject;
            if (obj != null)
                SceneVisibilityManager.instance.ToggleVisibility(obj, includeDescendants);
        }

        void PingItem(int index)
        {
            var obj = m_ItemProvider.GetItem(index) as UnityEngine.Object;
            if (obj != null)
                EditorGUIUtility.PingObject(obj);
        }

        protected Vector2 CalcSize()
        {
            float height = (maxIndex + 1) * LineHeight + m_SeperatorIndices.Length * SeparatorHeight;
            if (m_CachedWidth < 0)
                m_CachedWidth = Math.Max(m_MinTextWidth, CalcWidth());
            return new Vector2(m_CachedWidth, height);
        }

        void ClearCachedWidth()
        {
            m_CachedWidth = -1f;
        }

        float CalcWidth()
        {
            if (s_Styles == null)
                s_Styles = new Styles();

            float maxWidth = 0;
            for (int i = 0; i < m_ItemProvider.Count(); ++i)
            {
                float w = s_Styles.menuItem.CalcSize(GUIContent.Temp(m_ItemProvider.GetName(i))).x;
                maxWidth = Mathf.Max(w, maxWidth);
            }

            const float rightMargin = 6f;
            return maxWidth + rightMargin;
        }

        void Repaint()
        {
            HandleUtility.Repaint(); // repaints current guiview (needs rename)
        }
    }
}

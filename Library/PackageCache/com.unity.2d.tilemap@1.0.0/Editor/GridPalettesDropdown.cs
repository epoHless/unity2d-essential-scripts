using System;

namespace UnityEditor.Tilemaps
{
    internal class GridPalettesDropdown : FlexibleMenu
    {
        public GridPalettesDropdown(IFlexibleMenuItemProvider itemProvider, int selectionIndex, FlexibleMenuModifyItemUI modifyItemUi, Action<int, object> itemClickedCallback, float minWidth)
            : base(itemProvider, selectionIndex, modifyItemUi, itemClickedCallback)
        {
            minTextWidth = minWidth;
        }

        internal class MenuItemProvider : IFlexibleMenuItemProvider
        {
            public int Count()
            {
                return GridPaintingState.palettes.Count + 1;
            }

            public object GetItem(int index)
            {
                if (index < GridPaintingState.palettes.Count)
                    return GridPaintingState.palettes[index];

                return null;
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
                if (index < GridPaintingState.palettes.Count)
                    return GridPaintingState.palettes[index].name;
                else if (index == GridPaintingState.palettes.Count)
                    return "Create New Palette";
                else
                    return "";
            }

            public bool IsModificationAllowed(int index)
            {
                return false;
            }

            public int[] GetSeperatorIndices()
            {
                return new int[] { GridPaintingState.palettes.Count - 1 };
            }
        }
    }
}

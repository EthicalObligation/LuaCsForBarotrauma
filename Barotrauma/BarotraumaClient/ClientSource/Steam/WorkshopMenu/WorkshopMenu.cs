#nullable enable

namespace Barotrauma.Steam
{
    abstract partial class WorkshopMenu
    {
        public WorkshopMenu(GUIFrame parent) { }

        protected abstract void UpdateModListItemVisibility();
    }
}
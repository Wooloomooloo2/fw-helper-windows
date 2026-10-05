using FwHelper.Features;

namespace FwHelper.UI
{
    /// <summary>Menu items for every profile (built-in and user), with the active one checked. Used by the tray menu and the main window.</summary>
    public static class ProfileMenu
    {
        private const string Tag = "profile";

        /// <summary>Replace any profile items in <paramref name="items"/> with a fresh set starting at <paramref name="index"/>.</summary>
        public static void Fill(ToolStripItemCollection items, int index)
        {
            for (int i = items.Count - 1; i >= 0; i--)
                if (items[i].Tag as string == Tag) items.RemoveAt(i);

            foreach (int mode in Modes.All())
            {
                var item = new ToolStripMenuItem(Modes.Name(mode), TrayIcons.ForMode(mode, 16).ToBitmap(), (_, _) => ModeControl.SetMode(mode))
                {
                    Checked = mode == ModeControl.CurrentMode,
                    Tag = Tag,
                };
                items.Insert(index++, item);
            }
        }
    }
}

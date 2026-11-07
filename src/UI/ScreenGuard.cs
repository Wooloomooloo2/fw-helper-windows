namespace FwHelper.UI
{
    /// <summary>
    /// Keeps windows reachable when displays change. Unplugging the external screen makes Windows minimise the windows that were
    /// on it ("Minimize windows when a monitor is disconnected"), and their restore position is still on the screen that's gone.
    /// Pure: works on rectangles so it can be unit-tested.
    /// </summary>
    public static class ScreenGuard
    {
        /// <summary>How much of the title bar must be on a screen to count as reachable (drag it from there).</summary>
        public const int MinVisibleWidth = 120, TitleBarHeight = 30;

        public static bool Reachable(Rectangle bounds, IEnumerable<Rectangle> workAreas)
        {
            var title = new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Min(TitleBarHeight, bounds.Height));
            return workAreas.Any(a =>
            {
                var r = Rectangle.Intersect(title, a);
                return r.Width >= Math.Min(MinVisibleWidth, bounds.Width) && r.Height > 0;
            });
        }

        /// <summary>The bounds unchanged if reachable; otherwise the same size (shrunk to fit) centred on <paramref name="fallback"/>.</summary>
        public static Rectangle Fit(Rectangle bounds, IEnumerable<Rectangle> workAreas, Rectangle fallback)
        {
            if (Reachable(bounds, workAreas)) return bounds;
            int w = Math.Min(bounds.Width, fallback.Width), h = Math.Min(bounds.Height, fallback.Height);
            return new Rectangle(fallback.Left + (fallback.Width - w) / 2, fallback.Top + (fallback.Height - h) / 2, w, h);
        }
    }
}

using FwHelper.Features;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace FwHelper.UI
{
    /// <summary>Generates mode-colored tray / window icons at runtime (no image resources needed).</summary>
    public static class TrayIcons
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private static readonly Dictionary<(int, int), Icon> _cache = new();

        public static Icon ForMode(int mode, int size = 32)
        {
            if (_cache.TryGetValue((mode, size), out var icon)) return icon;

            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                var color = Modes.ColorOf(mode);
                float pad = size / 16f;
                using var path = RButton.RoundedRect(new RectangleF(pad, pad, size - 2 * pad - 1, size - 2 * pad - 1), size / 5f);
                using var brush = new SolidBrush(color);
                g.FillPath(brush, path);

                using var font = new Font("Segoe UI", size * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("F", font, Brushes.White, new RectangleF(0, size * 0.02f, size, size), sf);
            }

            IntPtr h = bmp.GetHicon();
            icon = (Icon)Icon.FromHandle(h).Clone();
            DestroyIcon(h);
            _cache[(mode, size)] = icon;
            return icon;
        }
    }
}

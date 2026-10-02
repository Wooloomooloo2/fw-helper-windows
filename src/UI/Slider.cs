using System.Drawing.Drawing2D;

namespace FwHelper.UI
{
    /// <summary>Flat slider with round thumb (adapted from G-Helper's Slider).</summary>
    public class Slider : Control
    {
        private float _radius;
        private PointF _thumbPos;
        private SizeF _barSize;
        private PointF _barPos;
        private bool _moving;

        public Color AccentColor { get; set; } = RForm.Accent;

        public event EventHandler? ValueChanged;
        /// <summary>Raised when the user releases the mouse / key, i.e. a good moment to apply to hardware.</summary>
        public event EventHandler? ValueCommitted;

        public Slider()
        {
            DoubleBuffered = true;
            TabStop = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        }

        private int _min, _max = 100, _step = 1, _value = 50;

        public int Min { get => _min; set { _min = value; Recalculate(); } }
        public int Max { get => _max; set { _max = value; Recalculate(); } }
        public int Step { get => _step; set => _step = Math.Max(1, value); }

        public int Value
        {
            get => _value;
            set
            {
                value = Math.Clamp((int)Math.Round(value / (float)_step) * _step, _min, _max);
                if (_value == value) return;
                _value = value;
                ValueChanged?.Invoke(this, EventArgs.Empty);
                Recalculate();
            }
        }

        /// <summary>Set without raising events (for refreshing from hardware).</summary>
        public void SetValueSilently(int value)
        {
            _value = Math.Clamp(value, _min, _max);
            Recalculate();
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Right or Keys.Up) Value += Step;
            if (e.KeyCode is Keys.Left or Keys.Down) Value -= Step;
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down) ValueCommitted?.Invoke(this, EventArgs.Empty);
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var accent = new SolidBrush(Enabled ? AccentColor : RForm.ChartGrid);
            using var empty = new SolidBrush(RForm.ChartGrid);
            using var border = new SolidBrush(RForm.ButtonMain);

            g.FillRectangle(empty, _barPos.X, _barPos.Y, _barSize.Width, _barSize.Height);
            g.FillRectangle(accent, _barPos.X, _barPos.Y, _thumbPos.X - _barPos.X, _barSize.Height);
            g.FillEllipse(border, _thumbPos.X - _radius, _thumbPos.Y - _radius, 2 * _radius, 2 * _radius);
            float inner = _radius * (_moving ? 0.45f : 0.6f);
            g.FillEllipse(accent, _thumbPos.X - inner, _thumbPos.Y - inner, 2 * inner, 2 * inner);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Recalculate();
        }

        private void Recalculate()
        {
            _radius = 0.4f * ClientSize.Height;
            _barSize = new SizeF(Math.Max(1, ClientSize.Width - 2 * _radius), ClientSize.Height * 0.12f);
            _barPos = new PointF(_radius, (ClientSize.Height - _barSize.Height) / 2);
            float t = _max > _min ? (float)(_value - _min) / (_max - _min) : 0;
            _thumbPos = new PointF(_barPos.X + _barSize.Width * t, _barPos.Y + _barSize.Height / 2);
            Invalidate();
        }

        private void ValueFromMouse(int x)
        {
            float t = Math.Clamp((x - _barPos.X) / _barSize.Width, 0, 1);
            Value = (int)Math.Round(_min + t * (_max - _min));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            Focus();
            _moving = true;
            ValueFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_moving) ValueFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_moving) return;
            _moving = false;
            Invalidate();
            ValueCommitted?.Invoke(this, EventArgs.Empty);
        }
    }
}

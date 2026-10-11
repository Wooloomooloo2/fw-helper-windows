using FwHelper.Features;
using FwHelper.Hardware;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace FwHelper.UI
{
    /// <summary>
    /// The battery window's content (ADR 0018): the current discharge, the last charge, battery health, a charge chart
    /// (24 h / 7 days) and a day / month / year table. It reads the battery log, plus one EC read every 5 s while it's visible
    /// so the numbers stay live. Nothing runs while it's closed.
    /// </summary>
    public class BatteryPanel : Control
    {
        private static readonly Color Line = Color.FromArgb(6, 180, 138);

        private BatteryEvent[] _events = Array.Empty<BatteryEvent>();
        private BatteryArchive _archive = BatteryArchive.Empty;
        private StandbySession[] _sessions = Array.Empty<StandbySession>();
        private BatteryPeriod? _week7;
        private BatteryEvent? _live;
        private BatteryInfo? _info;
        private BatterySession? _session;
        private List<BatteryPeriod> _rows = new();

        private bool _week;
        private int _table; // 0 days, 1 months, 2 years
        private int _scroll;
        private readonly List<(RectangleF rect, Action click)> _hits = new();
        private readonly System.Windows.Forms.Timer _minute = new() { Interval = 5_000 };
        private DateTime _updated;

        // On battery: how long Windows stays awake with the screen off before it sleeps (the Windows "Sleep after" setting)
        private static readonly (string text, int seconds)[] SleepChoices =
        {
            ("5 min", 300), ("10 min", 600), ("15 min", 900), ("30 min", 1800), ("1 hour", 3600), ("2 hours", 7200), ("Never", 0),
        };
        private readonly ComboBox _sleepAfter = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
        private bool _fillingSleepAfter;

        private const float StatsH = 172, StandbyH = 108, ChartH = 170, Gap = 10;

        public BatteryPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            _minute.Tick += (_, _) => Reload();
            _sleepAfter.SelectedIndexChanged += (_, _) =>
            {
                if (_fillingSleepAfter || _sleepAfter.SelectedItem is not string text) return;
                var choice = SleepChoices.FirstOrDefault(c => c.text == text);
                if (choice.text is not null) PowerPlan.SetSleepAfterDc(choice.seconds);
            };
            Controls.Add(_sleepAfter);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            _sleepAfter.Width = (int)S(90);
            _sleepAfter.Location = new Point((int)(Width - S(10) - _sleepAfter.Width), (int)(S(StatsH + Gap) + S(4)));
        }

        /// <summary>Shows the current Windows value; leaves the box alone while it's open.</summary>
        private void FillSleepAfter()
        {
            if (_sleepAfter.DroppedDown) return;
            int? current = PowerPlan.GetSleepAfterDc();
            var items = SleepChoices.Select(c => c.text).ToList();
            string? selected = SleepChoices.FirstOrDefault(c => c.seconds == current).text;
            if (selected is null && current is int other)
            {
                selected = other % 60 == 0 ? $"{other / 60} min" : $"{other} s";
                items.Add(selected);
            }
            if (_sleepAfter.Items.Cast<string>().SequenceEqual(items) && Equals(_sleepAfter.SelectedItem, selected)) return;
            _fillingSleepAfter = true;
            _sleepAfter.Items.Clear();
            foreach (var item in items) _sleepAfter.Items.Add(item);
            _sleepAfter.SelectedItem = selected;
            _fillingSleepAfter = false;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                BatteryLog.Changed += OnChanged;
                _minute.Start();
                Reload();
            }
            else
            {
                BatteryLog.Changed -= OnChanged;
                _minute.Stop();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                BatteryLog.Changed -= OnChanged;
                _minute.Dispose();
            }
            base.Dispose(disposing);
        }

        private void OnChanged()
        {
            if (IsHandleCreated) BeginInvoke(Reload);
        }

        public void Reload()
        {
            FillSleepAfter();
            SetData(BatteryLog.Events(), BatteryLog.Archive(), BatteryLog.Read(), FrameworkEc.GetBattery(), BatteryLog.Sessions());
        }

        public void SetData(BatteryEvent[] events, BatteryArchive archive, BatteryEvent? live, BatteryInfo? info, StandbySession[]? sessions = null)
        {
            _events = events;
            _archive = archive;
            _sessions = sessions ?? Array.Empty<StandbySession>();
            _info = info;
            _live = live;
            _updated = DateTime.Now;
            _session = BatteryHistory.Session(_events, _live);
            var all = _live is null ? _events.ToList() : _events.Append(_live).ToList();
            var days = BatteryHistory.Days(all, _archive, _sessions);
            var weekStart = DateTime.Today.AddDays(-6);
            _week7 = days.Where(d => d.Start >= weekStart).DefaultIfEmpty().Aggregate((x, y) => x is null ? y : y is null ? x : x.Merge(y));
            _rows = _table switch
            {
                0 => days,
                1 => BatteryHistory.Months(all, _archive, _sessions),
                _ => BatteryHistory.Years(all, _archive, _sessions),
            };
            _rows.Reverse(); // newest first
            _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _rows.Count - 1));
            Invalidate();
        }

        private float S(float v) => v * DeviceDpi / 96f;

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            foreach (var (rect, click) in _hits)
                if (rect.Contains(e.Location)) { click(); return; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = _hits.Any(h => h.rect.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _scroll = Math.Clamp(_scroll - Math.Sign(e.Delta) * 3, 0, Math.Max(0, _rows.Count - 1));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(RForm.FormBack);
            _hits.Clear();

            float gap = S(Gap), statsH = S(StatsH), standbyH = S(StandbyH), chartH = S(ChartH);
            var stats = new RectangleF(0, 0, Width - 1, statsH);
            var standby = new RectangleF(0, statsH + gap, Width - 1, standbyH);
            var chart = new RectangleF(0, standby.Bottom + gap, Width - 1, chartH);
            var table = new RectangleF(0, chart.Bottom + gap, Width - 1, Height - chart.Bottom - gap - 1);

            using var title = new Font(Font.FontFamily, 9f, FontStyle.Bold);
            using var small = new Font(Font.FontFamily, 8f);
            using var fore = new SolidBrush(RForm.ForeMain);
            using var dim = new SolidBrush(RForm.ForeDim);
            using var accent = new SolidBrush(RForm.Accent);

            DrawStats(g, stats, title, small, fore, dim);
            DrawStandby(g, standby, title, small, fore, dim);
            DrawChart(g, chart, title, small, fore, dim, accent);
            DrawTable(g, table, title, small, fore, dim, accent);
        }

        private void Card(Graphics g, RectangleF r)
        {
            using var bg = new SolidBrush(RForm.ChartMain);
            using var path = Rounded(r, S(6));
            using var border = new Pen(RForm.BorderMain);
            g.FillPath(bg, path);
            g.DrawPath(border, path);
        }

        private static string Clock(DateTime utc)
        {
            var local = utc.ToLocalTime();
            return local.Date == DateTime.Today ? local.ToString("HH:mm", CultureInfo.CurrentCulture)
                : local.ToString("ddd d MMM HH:mm", CultureInfo.CurrentCulture);
        }

        private void DrawStats(Graphics g, RectangleF r, Font title, Font small, Brush fore, Brush dim)
        {
            Card(g, r);
            var s = _session;
            bool onAC = _live?.OnAC ?? _events.LastOrDefault()?.OnAC ?? true;
            var left = new List<(string, string)>();
            var right = new List<(string, string)>();

            if (s?.OnBatterySince is DateTime since)
            {
                left.Add(("On battery", $"{BatteryLog.Duration(s.OnBattery)}, since {Clock(since)}"));
                left.Add(("Screen on / off", $"{BatteryLog.Duration(s.OnBattery - s.ScreenOff)} / {BatteryLog.Duration(s.ScreenOff)}"));
                int? pctNow = onAC ? _events.LastOrDefault(ev => !ev.OnAC)?.Percent : _live?.Percent;
                left.Add(("Energy used", (s.StartPercent is int p && pctNow is int n ? $"{p - n}% · " : "") + $"{s.UsedWh:0.0} Wh"));
                left.Add(("Average draw", (s.AverageW is double w ? $"{w:0.0} W screen on" : "– screen on") +
                                          (s.ScreenOffW is double o ? $" · {o:0.00} W screen off" : "")));
                if (!onAC) left.Add(("Time left", s.HoursLeft is double h ? $"~{BatteryLog.Duration(TimeSpan.FromHours(h))} at the screen-on average" : "–"));
            }
            else left.Add(("On battery", "nothing logged yet"));
            if (_info is { Present: true } now)
                left.Add(("Now", $"{now.Percent}% · {(now.Charging ? $"charging at {now.Watts:0.0} W" : now.Discharging ? $"drawing {-now.Watts:0.0} W" : "on AC, not charging")}"));

            if (s?.ChargedTo is int to)
            {
                right.Add((onAC ? "This charge" : "Last charge", $"{s.ChargedFrom}% → {to}%"));
                if (s.ChargeStarted is DateTime a && s.ChargeEnded is DateTime b)
                    right.Add((onAC ? "Plugged in" : "Unplugged", onAC ? $"{Clock(a)}, {BatteryLog.Duration(DateTime.UtcNow - a)} ago" : $"{Clock(b)}, after {BatteryLog.Duration(b - a)} on AC"));
            }
            if (FrameworkEc.GetChargeLimit() is { } lim) right.Add(("Charge limit", $"{lim.max}%"));
            if (_info is { Present: true } i)
            {
                double v = i.DesignVoltageMv / 1e6;
                right.Add(("Health", $"{i.HealthPercent}% · holds {i.FullMah * v:0.0} of {i.DesignMah * v:0.0} Wh"));
                var (measured, measuredSince) = BatteryHistory.MeasuredCycles(_live is null ? _events : _events.Append(_live).ToList(), _archive, i.DesignMah * v);
                right.Add(("Cycles", measuredSince is DateTime from
                    ? $"{measured:0.0} measured since {from.ToString("d MMM", CultureInfo.CurrentCulture)} · battery says {i.Cycles}"
                    : $"battery says {i.Cycles}"));
            }

            float col = (r.Width - S(16)) / 2;
            g.DrawString(onAC ? "Last time on battery" : "On battery now", title, fore, r.Left + S(8), r.Top + S(6));
            g.DrawString("Charging and health", title, fore, r.Left + S(8) + col, r.Top + S(6));
            if (_updated != default)
            {
                string updated = $"updated {_updated:HH:mm:ss}";
                g.DrawString(updated, small, dim, r.Right - g.MeasureString(updated, small).Width - S(8), r.Top + S(8));
            }
            Rows(left, r.Left + S(8));
            Rows(right, r.Left + S(8) + col);

            void Rows(List<(string label, string value)> rows, float x)
            {
                float y = r.Top + S(30);
                foreach (var (label, value) in rows)
                {
                    g.DrawString(label, small, dim, x, y);
                    g.DrawString(value, small, fore, x + S(100), y);
                    y += S(22);
                }
            }
        }

        private void DrawStandby(Graphics g, RectangleF r, Font title, Font small, Brush fore, Brush dim)
        {
            Card(g, r);
            g.DrawString("Standby (screen off, on battery)", title, fore, r.Left + S(8), r.Top + S(6));
            string label = "Sleep after the screen turns off";
            var size = g.MeasureString(label, small);
            g.DrawString(label, small, dim, _sleepAfter.Left - size.Width - S(6), r.Top + S(8));

            var rows = new List<(string, string)>();
            if (BatteryHistory.LastStandby(_sessions) is { } st)
                rows.Add(("Last standby", $"{Clock(st.EnterUtc)} → {Clock(st.ExitUtc)} · " +
                                          BatteryLog.Standby(st.AwakeSeconds, st.AwakeW, st.AsleepSeconds, st.AsleepW).Replace(" | ", " · ") +
                                          $" · deep idle {st.DeepIdlePercent:0}%"));
            else rows.Add(("Last standby", "none yet: Windows records each one when it ends"));
            if (_week7 is { } w && w.AwakeOffSeconds + w.AsleepSeconds > 0)
                rows.Add(("Last 7 days" + (_events.Length > 0 && _events[0].Time.ToLocalTime() > DateTime.Today.AddDays(-6) ? "*" : ""), BatteryLog.Standby(w.AwakeOffSeconds, w.AwakeOffW, w.AsleepSeconds, w.AsleepW).Replace(" | ", " · ")));
            if (_events.Length > 0 && _events[0].Time.ToLocalTime() > DateTime.Today.AddDays(-6))
                rows.Add(("", $"* since the battery log started on {_events[0].Time.ToLocalTime():d MMM}"));
            rows.Add(("", "Closing the lid sleeps at once. When the screen times out, Windows stays awake until \"Sleep after\" runs out."));

            float y = r.Top + S(30);
            foreach (var (k, v) in rows)
            {
                g.DrawString(k, small, dim, r.Left + S(8), y);
                g.DrawString(v, small, k.Length == 0 ? dim : fore, r.Left + S(108), y);
                y += S(19);
            }
        }

        private void DrawChart(Graphics g, RectangleF r, Font title, Font small, Brush fore, Brush dim, Brush accent)
        {
            Card(g, r);
            g.DrawString("Charge", title, fore, r.Left + S(8), r.Top + S(6));

            // 24 h | 7 days toggle, right-aligned
            float x = r.Right - S(8);
            foreach (var (text, week) in new[] { ("7 days", true), ("24 h", false) })
            {
                var size = g.MeasureString(text, small);
                x -= size.Width;
                var rect = new RectangleF(x, r.Top + S(8), size.Width, size.Height);
                g.DrawString(text, small, _week == week ? accent : dim, rect.Location);
                bool w = week;
                _hits.Add((rect, () => { _week = w; Invalidate(); }));
                x -= S(10);
            }
            using (var legendAc = new SolidBrush(Color.FromArgb(50, RForm.Accent)))
            using (var legendOff = new SolidBrush(Color.FromArgb(40, RForm.ForeDim)))
            using (var legendSleep = new SolidBrush(Color.FromArgb(90, RForm.ForeDim)))
            {
                float lx = r.Left + S(70);
                g.FillRectangle(legendAc, lx, r.Top + S(10), S(10), S(10));
                g.DrawString("on AC", small, dim, lx + S(13), r.Top + S(8));
                lx += S(60);
                g.FillRectangle(legendOff, lx, r.Top + S(10), S(10), S(10));
                g.DrawString("screen off", small, dim, lx + S(13), r.Top + S(8));
                lx += S(80);
                g.FillRectangle(legendSleep, lx, r.Top + S(10), S(10), S(10));
                g.DrawString("asleep", small, dim, lx + S(13), r.Top + S(8));
            }

            var plot = new RectangleF(r.Left + S(38), r.Top + S(28), r.Width - S(46), r.Height - S(48));
            var end = DateTime.UtcNow;
            var start = end - (_week ? TimeSpan.FromDays(7) : TimeSpan.FromHours(24));
            float X(DateTime t) => plot.Left + (float)((t - start).TotalSeconds / (end - start).TotalSeconds) * plot.Width;
            float Y(double pct) => plot.Bottom - (float)(Math.Clamp(pct, 0, 100) / 100) * plot.Height;

            using var grid = new Pen(RForm.ChartGrid);
            for (int pct = 0; pct <= 100; pct += 25)
            {
                g.DrawLine(grid, plot.Left, Y(pct), plot.Right, Y(pct));
                var size = g.MeasureString(pct.ToString(), small);
                g.DrawString(pct.ToString(), small, dim, plot.Left - size.Width - S(2), Y(pct) - size.Height / 2);
            }
            // Time ticks: every 6 h, or every day
            var tick = _week ? start.ToLocalTime().Date.AddDays(1) : start.ToLocalTime().Date.AddHours((start.ToLocalTime().Hour / 6 + 1) * 6);
            for (; tick.ToUniversalTime() < end; tick = _week ? tick.AddDays(1) : tick.AddHours(6))
            {
                float tx = X(tick.ToUniversalTime());
                g.DrawLine(grid, tx, plot.Top, tx, plot.Bottom);
                string label = _week ? tick.ToString("ddd", CultureInfo.CurrentCulture) : tick.ToString("HH:mm", CultureInfo.CurrentCulture);
                g.DrawString(label, small, dim, tx - g.MeasureString(label, small).Width / 2, plot.Bottom + S(2));
            }

            var points = _live is null ? _events.ToList() : _events.Append(_live).ToList();
            int first = Math.Max(0, points.FindLastIndex(p => p.Time < start));
            using var acBrush = new SolidBrush(Color.FromArgb(50, RForm.Accent));
            using var offBrush = new SolidBrush(Color.FromArgb(40, RForm.ForeDim));
            using var pen = new Pen(Line, S(1.6f)) { LineJoin = LineJoin.Round };
            var line = new List<PointF>();
            g.SetClip(plot);
            for (int i = first; i < points.Count; i++)
            {
                var a = points[i];
                line.Add(new PointF(X(a.Time), Y(a.Percent)));
                if (i + 1 >= points.Count) break;
                var b = points[i + 1];
                bool gap = a.Kind == BatteryEventKind.Stop || b.Kind == BatteryEventKind.Start;
                if (!gap && (a.OnAC || !a.ScreenOn))
                    g.FillRectangle(a.OnAC ? acBrush : offBrush, X(a.Time), plot.Top, Math.Max(1, X(b.Time) - X(a.Time)), plot.Height);
                if (gap)
                {
                    if (line.Count >= 2) g.DrawLines(pen, line.ToArray());
                    line.Clear();
                }
            }
            if (line.Count >= 2) g.DrawLines(pen, line.ToArray());
            using (var sleepBrush = new SolidBrush(Color.FromArgb(50, RForm.ForeDim)))
                foreach (var st in _sessions.Where(x => x.ExitUtc > start && x.AsleepSeconds > 0))
                {
                    float x0 = X(st.ExitUtc.AddSeconds(-st.AsleepSeconds)), x1 = X(st.ExitUtc);
                    g.FillRectangle(sleepBrush, x0, plot.Top, Math.Max(1, x1 - x0), plot.Height);
                }
            g.ResetClip();
            if (points.Count == 0) g.DrawString("The log fills in as the battery level changes.", small, dim, plot.Left + S(8), plot.Top + S(8));
        }

        private void DrawTable(Graphics g, RectangleF r, Font title, Font small, Brush fore, Brush dim, Brush accent)
        {
            Card(g, r);
            g.DrawString("History", title, fore, r.Left + S(8), r.Top + S(6));
            float x = r.Right - S(8);
            string[] names = { "Days", "Months", "Years" };
            for (int t = names.Length - 1; t >= 0; t--)
            {
                var size = g.MeasureString(names[t], small);
                x -= size.Width;
                var rect = new RectangleF(x, r.Top + S(8), size.Width, size.Height);
                g.DrawString(names[t], small, _table == t ? accent : dim, rect.Location);
                int which = t;
                _hits.Add((rect, () => { _table = which; _scroll = 0; Reload(); }));
                x -= S(10);
            }

            // Column x positions in 96-DPI pixels, under group headings that say what each number is and its unit
            (string head, float at)[] cols =
            {
                ("", 8), ("Total", 92), ("Screen on", 152), ("Screen off", 214),
                ("Wh", 284),
                ("Screen on", 336), ("Off, awake", 396), ("Asleep", 462),
                ("Unplugs", 530), ("Avg to", 590),
                ("", 652),
            };
            (string group, int first, int last)[] groups =
            {
                ("Time on battery", 1, 3), ("Used", 4, 4), ("Average draw (W)", 5, 7), ("Charging", 8, 9), ("Health", 10, 10),
            };
            using var sep = new Pen(RForm.ChartGrid);
            float y = r.Top + S(28);
            foreach (var (group, first, last) in groups)
            {
                float gx = r.Left + S(cols[first].at);
                float gEnd = last + 1 < cols.Length ? r.Left + S(cols[last + 1].at) - S(10) : r.Right - S(8);
                g.DrawString(group, small, fore, gx, y);
                g.DrawLine(sep, gx, y + S(16), gEnd, y + S(16));
            }
            y += S(18);
            foreach (var (head, at) in cols) g.DrawString(head, small, dim, r.Left + S(at), y);
            y += S(18);
            g.DrawLine(sep, r.Left + S(8), y - S(2), r.Right - S(8), y - S(2));

            int design = _info is { DesignMah: > 0 } i ? i.DesignMah : 0;
            float rowH = S(18);
            int visible = (int)((r.Bottom - y - S(4)) / rowH);
            if (_rows.Count == 0) g.DrawString("Nothing logged yet.", small, dim, r.Left + S(8), y);
            foreach (var p in _rows.Skip(_scroll).Take(visible))
            {
                string period = _table switch
                {
                    0 => p.Start.Date == DateTime.Today ? "Today" : p.Start.ToString("ddd d MMM", CultureInfo.CurrentCulture),
                    1 => p.Start.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
                    _ => p.Start.Year.ToString(CultureInfo.CurrentCulture),
                };
                string[] cells =
                {
                    period,
                    BatteryLog.ShortDuration(TimeSpan.FromSeconds(p.TotalBatterySeconds)),
                    BatteryLog.ShortDuration(TimeSpan.FromSeconds(p.BatterySeconds)),
                    BatteryLog.ShortDuration(TimeSpan.FromSeconds(p.ScreenOffSeconds)),
                    $"{p.TotalBatteryWh:0.0}",
                    p.AverageW is double w ? $"{w:0.0}" : "–",
                    p.AwakeOffW is double o ? $"{o:0.00}" : "–",
                    p.AsleepW is double z ? $"{z:0.00}" : "–",
                    p.Unplugs > 0 ? p.Unplugs.ToString(CultureInfo.CurrentCulture) : "–",
                    p.AverageChargedTo is double c ? $"{c:0}%" : "–",
                    design > 0 && p.FullMah > 0 ? $"{100.0 * p.FullMah / design:0}%" : "–",
                };
                for (int c2 = 0; c2 < cols.Length; c2++)
                    g.DrawString(cells[c2], small, c2 == 0 ? fore : dim, r.Left + S(cols[c2].at), y);
                y += rowH;
            }
            if (_rows.Count > visible)
            {
                string more = $"{_scroll + 1}–{Math.Min(_rows.Count, _scroll + visible)} of {_rows.Count} · scroll for more";
                var size = g.MeasureString(more, small);
                g.DrawString(more, small, dim, r.Right - size.Width - S(8), r.Bottom - size.Height - S(4));
            }
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

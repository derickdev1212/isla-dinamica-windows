// Isla Dinámica para Windows — https://github.com/derickdev1212
// Píldora negra arriba al centro; se expande con lo que suena (controles de medios de Windows / SMTC).
// Compilar: ver compilar.ps1 (csc de .NET Framework 4.x, C# 5). Licencia MIT.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace IslaDinamica
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--salir")
            {
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(Isla.ExitEventName, out ev)) ev.Set();
                return;
            }
            bool demo = Array.IndexOf(args, "--demo") >= 0;
            bool nuevo;
            var mutex = new Mutex(true, "Local\\IslaDinamica", out nuevo);
            if (!nuevo) return;
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Isla(demo));
            GC.KeepAlive(mutex);
        }
    }

    class Spring
    {
        public double X, V, T;
        public Spring(double x) { X = x; T = x; }
        public bool Step(double k, double d)
        {
            V += (T - X) * k; V *= d; X += V;
            if (Math.Abs(T - X) < 0.02 && Math.Abs(V) < 0.02) { X = T; V = 0; return false; }
            return true;
        }
        public bool AtRest { get { return X == T && V == 0; } }
    }

    class Isla : Form
    {
        public const string ExitEventName = "Local\\IslaDinamicaSalir";
        readonly bool demo;
        bool HasMedia { get { return ses != null || demo; } }

        // Tamaños en píxeles lógicos (se multiplican por la escala de pantalla)
        const double IDLE_W = 112, IDLE_H = 26;
        const double COMP_W = 204, COMP_H = 30;
        const double EXP_W = 360, EXP_H = 128, EXP_R = 30;
        const double TOP = 7;
        const int CANVAS_W = 400, CANVAS_H = 170;

        double S = 1; int CW, CH;
        readonly Spring sw = new Spring(IDLE_W), sh = new Spring(IDLE_H), sr = new Spring(IDLE_H / 2), op = new Spring(1);

        IntPtr memDC, hBmp, oldBmp, bits;
        Bitmap canvas; Graphics g;
        readonly System.Windows.Forms.Timer frameTimer, pollTimer;
        int frameCount, pollCount;

        GlobalSystemMediaTransportControlsSessionManager mgr;
        GlobalSystemMediaTransportControlsSession ses;
        string title = "", artist = "", trackKey = "";
        Bitmap art; Color accent = Color.FromArgb(235, 235, 235);
        bool playing, refreshing;
        DateTime lastPlaying = DateTime.MinValue, peekUntil = DateTime.MinValue;
        TimeSpan tlPos, tlEnd; DateTime tlStamp;

        bool hover, clickable, fullscreen;
        DateTime lastInside = DateTime.MinValue;
        RectangleF rPrev, rPlay, rNext;
        readonly Font fTitle, fArtist, fTime;
        readonly DateTime t0 = DateTime.Now;

        public Isla(bool demo)
        {
            this.demo = demo;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Text = "Isla Dinámica";

            S = GetDpiForSystem() / 96.0;
            CW = (int)Math.Round(CANVAS_W * S); CH = (int)Math.Round(CANVAS_H * S);
            Size = new Size(CW, CH);
            Place();

            fTitle = new Font("Segoe UI Semibold", (float)(14 * S), FontStyle.Regular, GraphicsUnit.Pixel);
            fArtist = new Font("Segoe UI", (float)(12.5 * S), FontStyle.Regular, GraphicsUnit.Pixel);
            fTime = new Font("Segoe UI", (float)(10.5 * S), FontStyle.Regular, GraphicsUnit.Pixel);

            frameTimer = new System.Windows.Forms.Timer { Interval = 15 };
            frameTimer.Tick += delegate { Frame(); };
            pollTimer = new System.Windows.Forms.Timer { Interval = 100 };
            pollTimer.Tick += delegate { Poll(); };
            MouseUp += OnClickIsla;
            SystemEvents.DisplaySettingsChanged += delegate { try { BeginInvoke(new Action(Place)); } catch { } };

            var exitEv = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            var th = new Thread(delegate () { exitEv.WaitOne(); try { BeginInvoke(new Action(Close)); } catch { } });
            th.IsBackground = true; th.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TRANSPARENT;
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            InitCanvas();
            Render();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            pollTimer.Start();
            if (demo) SetupDemo(); else InitMedia();
        }

        // Modo demostración (--demo): datos de ejemplo, sin leer los medios del sistema.
        void SetupDemo()
        {
            title = "Canción de ejemplo"; artist = "Isla Dinámica";
            playing = true; lastPlaying = DateTime.Now;
            tlEnd = TimeSpan.FromSeconds(225); tlPos = TimeSpan.FromSeconds(72); tlStamp = DateTime.Now;
            int n = (int)Math.Round(64 * S);
            var b = new Bitmap(n, n, PixelFormat.Format32bppPArgb);
            using (var gg = Graphics.FromImage(b))
            {
                gg.SmoothingMode = SmoothingMode.AntiAlias;
                using (var lg = new LinearGradientBrush(new Rectangle(0, 0, n, n), Color.FromArgb(255, 94, 98), Color.FromArgb(120, 70, 255), 45f))
                    gg.FillRectangle(lg, 0, 0, n, n);
                float u = n / 8f;
                using (var w = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                {
                    gg.FillEllipse(w, 2.2f * u, 4.6f * u, 1.8f * u, 1.4f * u);
                    gg.FillEllipse(w, 4.6f * u, 4.0f * u, 1.8f * u, 1.4f * u);
                    gg.FillRectangle(w, 3.6f * u, 2.0f * u, 0.4f * u, 3.3f * u);
                    gg.FillRectangle(w, 6.0f * u, 1.4f * u, 0.4f * u, 3.3f * u);
                    gg.FillPolygon(w, new[] { new PointF(3.6f * u, 2.0f * u), new PointF(6.4f * u, 1.4f * u), new PointF(6.4f * u, 2.2f * u), new PointF(3.6f * u, 2.8f * u) });
                }
            }
            SetArt(b);
            peekUntil = DateTime.Now.AddSeconds(6);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            frameTimer.Stop(); pollTimer.Stop();
            if (g != null) g.Dispose();
            if (canvas != null) canvas.Dispose();
            if (memDC != IntPtr.Zero) { SelectObject(memDC, oldBmp); DeleteObject(hBmp); DeleteDC(memDC); }
            base.OnFormClosed(e);
        }

        void Place()
        {
            var b = Screen.PrimaryScreen.Bounds;
            Location = new Point(b.Left + (b.Width - CW) / 2, b.Top);
            if (IsHandleCreated) Render();
        }

        // ---------------- Lienzo con transparencia por píxel ----------------
        void InitCanvas()
        {
            var bmi = new BITMAPINFO();
            bmi.biSize = Marshal.SizeOf(typeof(BITMAPINFO));
            bmi.biWidth = CW; bmi.biHeight = -CH; bmi.biPlanes = 1; bmi.biBitCount = 32;
            memDC = CreateCompatibleDC(IntPtr.Zero);
            hBmp = CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
            oldBmp = SelectObject(memDC, hBmp);
            canvas = new Bitmap(CW, CH, CW * 4, PixelFormat.Format32bppPArgb, bits);
            g = Graphics.FromImage(canvas);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
        }

        void Render()
        {
            if (g == null) return;
            g.Clear(Color.Transparent);
            float w = (float)(sw.X * S), h = (float)(sh.X * S);
            float r = (float)Math.Min(sr.X * S, h / 2.0);
            float x = (CW - w) / 2f, y = (float)(TOP * S);

            // sombra suave
            for (int i = 6; i >= 1; i--)
            {
                float sp = (float)(i * 1.5 * S);
                using (var p = RR(x - sp, y - sp * 0.3f, w + 2 * sp, h + sp * 1.7f, r + sp))
                using (var b = new SolidBrush(Color.FromArgb(8, 0, 0, 0))) g.FillPath(b, p);
            }
            using (var p = RR(x, y, w, h, r))
            {
                g.FillPath(Brushes.Black, p);
                g.SetClip(p, CombineMode.Replace);
                DrawContent(x, y, w, h);
                g.ResetClip();
            }
            g.Flush(FlushIntention.Sync);

            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, AlphaFormat = 1,
                SourceConstantAlpha = (byte)Math.Max(0, Math.Min(255, op.X * 255)) };
            var dst = new POINT { x = Left, y = Top };
            var src = new POINT();
            var sz = new SIZE { cx = CW, cy = CH };
            UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref sz, memDC, ref src, 0, ref blend, 2);
        }

        void DrawContent(float x, float y, float w, float h)
        {
            if (!HasMedia) return;
            double e = Clamp((sh.X - COMP_H) / (EXP_H - COMP_H));
            double cA = Clamp((sw.X - IDLE_W - 30) / (COMP_W - IDLE_W - 30)) * (1 - e);
            double eA = Clamp((e - 0.4) / 0.6);
            float s = (float)S;

            if (cA > 0.01)
            {
                float a = 20 * s;
                DrawArt(x + 6 * s, y + (h - a) / 2, a, 6 * s, cA);
                DrawBars(x + w - 10 * s, y + h / 2, 1f, cA);
            }
            if (eA > 0.01)
            {
                float pad = 16 * s, a = 58 * s;
                DrawArt(x + pad, y + pad - 2 * s, a, 13 * s, eA);
                float tx = x + pad + a + 12 * s, tw = w - (tx - x) - pad - 34 * s;
                var sf = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
                using (var b = new SolidBrush(Alpha(Color.White, eA)))
                    g.DrawString(title.Length > 0 ? title : "Reproduciendo", fTitle, b, new RectangleF(tx, y + pad + 6 * s, tw, 20 * s), sf);
                using (var b = new SolidBrush(Alpha(Color.FromArgb(160, 160, 160), eA)))
                    g.DrawString(artist, fArtist, b, new RectangleF(tx, y + pad + 27 * s, tw, 18 * s), sf);
                DrawBars(x + w - pad - 2 * s, y + pad + 18 * s, 1.15f, eA);

                // progreso
                float py = y + 88 * s;
                if (tlEnd.TotalSeconds > 1)
                {
                    TimeSpan pos = tlPos;
                    if (playing) pos += DateTime.Now - tlStamp;
                    if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
                    if (pos > tlEnd) pos = tlEnd;
                    float frac = (float)(pos.TotalSeconds / tlEnd.TotalSeconds);
                    float bx = x + pad + 34 * s, bw = w - 2 * pad - 68 * s;
                    using (var b = new SolidBrush(Alpha(Color.FromArgb(70, 70, 70), eA))) using (var p = RR(bx, py - 2 * s, bw, 4 * s, 2 * s)) g.FillPath(b, p);
                    if (frac > 0) using (var b = new SolidBrush(Alpha(Color.White, eA))) using (var p = RR(bx, py - 2 * s, Math.Max(4 * s, bw * frac), 4 * s, 2 * s)) g.FillPath(b, p);
                    var sfr = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
                    var sfl = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                    using (var b = new SolidBrush(Alpha(Color.FromArgb(150, 150, 150), eA)))
                    {
                        g.DrawString(Fmt(pos), fTime, b, new RectangleF(x + pad - 2 * s, py - 8 * s, 34 * s, 16 * s), sfl);
                        g.DrawString("-" + Fmt(tlEnd - pos), fTime, b, new RectangleF(x + w - pad - 36 * s, py - 8 * s, 38 * s, 16 * s), sfr);
                    }
                }

                // controles
                float cy = y + 110 * s, cx = x + w / 2, gap = 58 * s, hs = 18 * s;
                rPrev = new RectangleF(cx - gap - hs, cy - hs, 2 * hs, 2 * hs);
                rPlay = new RectangleF(cx - hs, cy - hs, 2 * hs, 2 * hs);
                rNext = new RectangleF(cx + gap - hs, cy - hs, 2 * hs, 2 * hs);
                using (var b = new SolidBrush(Alpha(Color.White, eA)))
                {
                    DrawSkip(b, cx - gap, cy, 8 * s, false);
                    if (playing) { g.FillRectangle(b, cx - 6 * s, cy - 8 * s, 4 * s, 16 * s); g.FillRectangle(b, cx + 2 * s, cy - 8 * s, 4 * s, 16 * s); }
                    else g.FillPolygon(b, new[] { new PointF(cx - 6 * s, cy - 9 * s), new PointF(cx + 9 * s, cy), new PointF(cx - 6 * s, cy + 9 * s) });
                    DrawSkip(b, cx + gap, cy, 8 * s, true);
                }
            }
            else { rPrev = rPlay = rNext = RectangleF.Empty; }
        }

        void DrawSkip(Brush b, float cx, float cy, float u, bool next)
        {
            float d = next ? 1 : -1;
            g.FillPolygon(b, new[] { new PointF(cx - d * u, cy - u), new PointF(cx + d * 0.2f * u, cy), new PointF(cx - d * u, cy + u) });
            g.FillPolygon(b, new[] { new PointF(cx - d * 0.1f * u, cy - u), new PointF(cx + d * 1.1f * u, cy), new PointF(cx - d * 0.1f * u, cy + u) });
            float barX = next ? cx + 1.1f * u : cx - 1.1f * u - 0.25f * u;
            g.FillRectangle(b, barX, cy - u, 0.25f * u, 2 * u);
        }

        void DrawArt(float x, float y, float a, float r, double alpha)
        {
            using (var p = RR(x, y, a, a, r))
            {
                var old = g.Clip;
                g.SetClip(p, CombineMode.Intersect);
                if (art != null)
                {
                    var cm = new ColorMatrix { Matrix33 = (float)alpha };
                    using (var ia = new ImageAttributes())
                    {
                        ia.SetColorMatrix(cm);
                        g.DrawImage(art, new Rectangle((int)x, (int)y, (int)Math.Ceiling(a), (int)Math.Ceiling(a)), 0, 0, art.Width, art.Height, GraphicsUnit.Pixel, ia);
                    }
                }
                else
                {
                    using (var b = new SolidBrush(Alpha(Color.FromArgb(45, 45, 48), alpha))) g.FillPath(b, p);
                    using (var b = new SolidBrush(Alpha(Color.FromArgb(150, 150, 150), alpha)))
                    {
                        float u = a / 6;
                        g.FillEllipse(b, x + a / 2 - u * 1.4f, y + a / 2 + u * 0.2f, u * 1.4f, u * 1.1f);
                        g.FillRectangle(b, x + a / 2 - 0.15f * u, y + a / 2 - u * 1.6f, u * 0.3f, u * 2.4f);
                    }
                }
                g.Clip = old;
            }
        }

        void DrawBars(float right, float cy, float k, double alpha)
        {
            double t = (DateTime.Now - t0).TotalSeconds;
            float s = (float)S, bw = 2.6f * s * k, gap = 2.3f * s * k, maxH = 14 * s * k;
            using (var b = new SolidBrush(Alpha(accent, alpha)))
            {
                for (int i = 0; i < 4; i++)
                {
                    double lvl = playing
                        ? 0.28 + 0.72 * Math.Abs(Math.Sin(t * (4.6 + i * 1.7) + i * 1.3)) * (0.55 + 0.45 * Math.Abs(Math.Sin(t * 1.7 + i * 2.1)))
                        : 0.2;
                    float bh = (float)Math.Max(bw, lvl * maxH);
                    float bx = right - (4 - i) * (bw + gap) + gap;
                    using (var p = RR(bx, cy - bh / 2, bw, bh, bw / 2)) g.FillPath(b, p);
                }
            }
        }

        // ---------------- Animación y estado ----------------
        void Frame()
        {
            bool moving = sw.Step(0.16, 0.70) | sh.Step(0.16, 0.70) | sr.Step(0.16, 0.70) | op.Step(0.2, 0.62);
            frameCount++;
            bool barsLive = playing && HasMedia && op.X > 0.01 && sw.X > IDLE_W + 20;
            if (moving || frameCount % 2 == 0) Render(); // en reposo las barras van a ~30 fps
            if (!moving && !barsLive) { Render(); frameTimer.Stop(); }
        }

        void Poll()
        {
            pollCount++;
            if (pollCount % 5 == 0)
            {
                fullscreen = IsFullscreen();
                SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
            }
            var c = Cursor.Position;
            float w = (float)(sw.X * S), h = (float)(sh.X * S);
            var pr = new RectangleF(Left + (CW - w) / 2f, Top + (float)(TOP * S), w, h);
            pr.Inflate((float)(6 * S), (float)(6 * S));
            if (pr.Contains(c)) lastInside = DateTime.Now;
            hover = (DateTime.Now - lastInside).TotalMilliseconds < 400;

            if (pollCount % 7 == 0) RefreshMedia();
            UpdateTargets();
        }

        void UpdateTargets()
        {
            bool hasSes = HasMedia;
            if (demo && playing) lastPlaying = DateTime.Now;
            bool media = hasSes && (playing || (DateTime.Now - lastPlaying).TotalSeconds < 90);
            bool expanded = hasSes && !fullscreen && (hover || DateTime.Now < peekUntil);

            double tw, th, tr;
            if (expanded) { tw = EXP_W; th = EXP_H; tr = EXP_R; }
            else if (media) { tw = COMP_W; th = COMP_H; tr = COMP_H / 2; }
            else { tw = IDLE_W; th = IDLE_H; tr = IDLE_H / 2; }
            sw.T = tw; sh.T = th; sr.T = tr;
            op.T = fullscreen ? 0 : 1;

            bool wantClick = expanded && hover && !fullscreen;
            if (wantClick != clickable)
            {
                clickable = wantClick;
                int ex = GetWindowLong(Handle, -20);
                ex = clickable ? (ex & ~WS_EX_TRANSPARENT) : (ex | WS_EX_TRANSPARENT);
                SetWindowLong(Handle, -20, ex);
            }

            bool barsLive = playing && hasSes && !fullscreen;
            bool need = !sw.AtRest || !sh.AtRest || !sr.AtRest || !op.AtRest || (barsLive && sw.X > IDLE_W + 20);
            if (need && !frameTimer.Enabled) frameTimer.Start();
            if (!need && expanded && pollCount % 5 == 0) Render(); // progreso con la isla abierta y en pausa
        }

        void OnClickIsla(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var m = new ContextMenuStrip();
                m.Items.Add("Cerrar Isla Dinámica", null, delegate { Close(); });
                m.Show(Cursor.Position);
                return;
            }
            if (demo)
            {
                if (rPlay.Contains(e.Location))
                {
                    if (playing) tlPos += DateTime.Now - tlStamp;
                    tlStamp = DateTime.Now; playing = !playing;
                }
                return;
            }
            var s = ses;
            if (s == null) return;
            try
            {
                if (rPlay.Contains(e.Location)) { var op1 = s.TryTogglePlayPauseAsync(); }
                else if (rNext.Contains(e.Location)) { var op2 = s.TrySkipNextAsync(); }
                else if (rPrev.Contains(e.Location)) { var op3 = s.TrySkipPreviousAsync(); }
            }
            catch { }
            pollCount = 6 - (pollCount % 7) + pollCount; // refrescar en el próximo ciclo
        }

        // ---------------- Medios (SMTC) ----------------
        // Espera una operación de WinRT sin depender de System.Runtime.WindowsRuntime,
        // así compila también con los .winmd que trae Windows (sin el SDK).
        static Task<T> W<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            var tcs = new TaskCompletionSource<T>();
            op.Completed = delegate (Windows.Foundation.IAsyncOperation<T> o, Windows.Foundation.AsyncStatus st)
            {
                try
                {
                    if (st == Windows.Foundation.AsyncStatus.Completed) tcs.TrySetResult(o.GetResults());
                    else tcs.TrySetException(new Exception("WinRT: " + st));
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
            };
            return tcs.Task;
        }

        async void InitMedia()
        {
            try { mgr = await W(GlobalSystemMediaTransportControlsSessionManager.RequestAsync()); }
            catch { mgr = null; }
            RefreshMedia();
        }

        GlobalSystemMediaTransportControlsSession Pick()
        {
            var list = mgr.GetSessions();
            foreach (var s in list)
            {
                var pb = s.GetPlaybackInfo();
                if (pb != null && pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return s;
            }
            var cur = mgr.GetCurrentSession();
            if (cur != null) return cur;
            return list.Count > 0 ? list[0] : null;
        }

        async void RefreshMedia()
        {
            if (mgr == null || refreshing) return;
            refreshing = true;
            try
            {
                var s = Pick();
                if (s == null)
                {
                    ses = null; playing = false; title = ""; artist = ""; trackKey = ""; SetArt(null);
                    return;
                }
                ses = s;
                var pb = s.GetPlaybackInfo();
                playing = pb != null && pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if (playing) lastPlaying = DateTime.Now;
                var tl = s.GetTimelineProperties();
                if (tl != null)
                {
                    tlPos = tl.Position - tl.StartTime;
                    tlEnd = tl.EndTime - tl.StartTime;
                    tlStamp = tl.LastUpdatedTime.LocalDateTime;
                    if (tlStamp > DateTime.Now || tlStamp.Year < 2000) tlStamp = DateTime.Now;
                }
                var props = await W(s.TryGetMediaPropertiesAsync());
                if (props == null) return;
                string key = s.SourceAppUserModelId + "|" + props.Title + "|" + props.Artist;
                if (key != trackKey)
                {
                    trackKey = key;
                    title = props.Title ?? "";
                    artist = string.IsNullOrEmpty(props.Artist) ? AppName(s.SourceAppUserModelId) : props.Artist;
                    SetArt(await LoadThumb(props.Thumbnail));
                    if (playing && !fullscreen && title.Length > 0) peekUntil = DateTime.Now.AddSeconds(3);
                }
            }
            catch { }
            finally { refreshing = false; }
        }

        static string AppName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string l = id.ToLowerInvariant();
            if (l.Contains("spotify")) return "Spotify";
            if (l.Contains("chrome")) return "Chrome";
            if (l.Contains("msedge") || l.Contains("edge")) return "Edge";
            if (l.Contains("firefox")) return "Firefox";
            if (l.Contains("vlc")) return "VLC";
            string n = id.Split('!')[0];
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            return n;
        }

        async Task<Bitmap> LoadThumb(IRandomAccessStreamReference r)
        {
            if (r == null) return null;
            try
            {
                using (var ras = await W(r.OpenReadAsync()))
                {
                    uint size = (uint)Math.Min(ras.Size, 20u * 1024 * 1024);
                    if (size == 0) return null;
                    var buf = new byte[size];
                    using (var dr = new DataReader(ras.GetInputStreamAt(0)))
                    {
                        uint got = await W(dr.LoadAsync(size));
                        if (got < size) Array.Resize(ref buf, (int)got);
                        dr.ReadBytes(buf);
                    }
                    var ms = new MemoryStream(buf);
                    using (var src = new Bitmap(ms))
                    {
                        int side = Math.Min(src.Width, src.Height);
                        var srcRect = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
                        int n = (int)Math.Round(64 * S);
                        var b = new Bitmap(n, n, PixelFormat.Format32bppPArgb);
                        using (var gg = Graphics.FromImage(b))
                        {
                            gg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            gg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            gg.DrawImage(src, new Rectangle(0, 0, n, n), srcRect, GraphicsUnit.Pixel);
                        }
                        return b;
                    }
                }
            }
            catch { return null; }
        }

        void SetArt(Bitmap b)
        {
            if (art != null) art.Dispose();
            art = b;
            accent = b != null ? Accent(b) : Color.FromArgb(235, 235, 235);
        }

        static Color Accent(Bitmap b)
        {
            long R = 0, G = 0, B = 0; int n = 0;
            using (var t = new Bitmap(8, 8))
            {
                using (var gg = Graphics.FromImage(t)) { gg.InterpolationMode = InterpolationMode.HighQualityBilinear; gg.DrawImage(b, 0, 0, 8, 8); }
                for (int i = 0; i < 8; i++) for (int j = 0; j < 8; j++)
                {
                    var c = t.GetPixel(i, j);
                    int mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B));
                    int wgt = 1 + (mx - mn) / 16; // dar más peso a los píxeles con color
                    R += c.R * wgt; G += c.G * wgt; B += c.B * wgt; n += wgt;
                }
            }
            var avg = Color.FromArgb((int)(R / n), (int)(G / n), (int)(B / n));
            float sat = avg.GetSaturation();
            if (sat < 0.12f) return Color.FromArgb(235, 235, 235);
            return FromHsl(avg.GetHue(), Math.Max(sat, 0.55f), 0.66f);
        }

        static Color FromHsl(float h, float s, float l)
        {
            float c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs((h / 60f) % 2 - 1)), m = l - c / 2;
            float r = 0, gg = 0, b = 0;
            if (h < 60) { r = c; gg = x; } else if (h < 120) { r = x; gg = c; } else if (h < 180) { gg = c; b = x; }
            else if (h < 240) { gg = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((gg + m) * 255), (int)((b + m) * 255));
        }

        // ---------------- Pantalla completa ----------------
        bool IsFullscreen()
        {
            int st;
            if (SHQueryUserNotificationState(out st) == 0 && (st == 2 || st == 3 || st == 4)) return true;
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == Handle) return false;
            var cls = new StringBuilder(64);
            GetClassName(fg, cls, 64);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;
            RECT r;
            if (!GetWindowRect(fg, out r)) return false;
            var b = Screen.FromHandle(fg).Bounds;
            return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
        }

        // ---------------- Utilidades ----------------
        static double Clamp(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
        static Color Alpha(Color c, double a) { return Color.FromArgb((int)(Math.Max(0, Math.Min(1, a)) * c.A), c.R, c.G, c.B); }
        static string Fmt(TimeSpan t) { if (t < TimeSpan.Zero) t = TimeSpan.Zero; return ((int)t.TotalMinutes) + ":" + t.Seconds.ToString("00"); }

        static GraphicsPath RR(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            r = Math.Max(0.5f, Math.Min(r, Math.Min(w, h) / 2));
            float d = 2 * r;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---------------- Win32 ----------------
        const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20;

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO
        {
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
            public int bmiColors;
        }

        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern uint GetDpiForSystem();
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr hSection, uint offset);
    }
}

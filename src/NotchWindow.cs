// L'encoche : un bloc noir colle au bord haut de l'ecran principal, au centre, qui
// se transforme selon ce qu'il montre (valeurs : design/bip-mascotte/tokens.json).
//
//   Trait       120 x 4      aucune session active
//   Repliee     h 22         un Bip par session (5 max, puis "+N")   clics traversants
//   Entrouverte 320 x 60     4 s a chaque nouvelle demande           clics traversants
//   Depliee     440 x ...    au survol (200 ms) : demandes en tete, puis les sessions
//
// La fenetre est fixe et transparente ; seul le bloc change de taille. Elle ne prend
// jamais le focus (WS_EX_NOACTIVATE), sauf le temps de taper une reponse libre.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace ClaudeNotch
{
    // Une session vue par l'utilisateur, demandes comprises.
    class View
    {
        public Session S;
        public St State;          // etat effectif (une demande en attente force "en attente")
        public DateTime Since;
        public List<Pending> Pending = new List<Pending>();
    }

    enum Form { Line, Collapsed, Peek, Expanded }

    class NotchWindow : Window
    {
        // ---- dimensions (tokens.json -> notch.forms)
        const double LINE_W = 120, LINE_H = 4;
        const double COL_H = 22, COL_PAD = 10, COL_BIP = 16, COL_GAP = 6;
        const int MAX_BIPS = 5;
        const double PEEK_W = 320, PEEK_H = 60;
        const double EXP_W = 440, EXP_PAD = 12;
        const double TOP_PAD = 20;   // depliee : place du bouton de reglages en haut a droite
        const double SHOULDER = 8;
        const double WIN_W = EXP_W + 2 * SHOULDER + 80;   // marge pour l'ombre portee
        const double WIN_MARGIN = 44;                     // sous l'encoche : ombre portee (12 + 28)

        // ---- temps (tokens.json -> motion.notchTransform)
        static readonly TimeSpan HOVER_OPEN = TimeSpan.FromMilliseconds(200);
        static readonly TimeSpan LEAVE_CLOSE = TimeSpan.FromMilliseconds(300);
        static readonly TimeSpan PEEK_HOLD = TimeSpan.FromSeconds(4);
        static readonly TimeSpan ARM_DELAY = TimeSpan.FromMilliseconds(600);
        static readonly TimeSpan FLASH_AFTER = TimeSpan.FromMinutes(5);
        static readonly TimeSpan FLASH_EVERY = TimeSpan.FromSeconds(30);

        // ---- couleurs (tokens.json -> color.notch)
        static SolidColorBrush B(uint rgb, byte a = 0xFF)
        {
            var b = new SolidColorBrush(Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            b.Freeze();
            return b;
        }
        static readonly Brush Black = B(0x000000), Card = B(0x161616), Btn2 = B(0x2A2A2A), Always = B(0x333333), Sep = B(0x1C1C1C);
        static readonly Brush Ink = B(0xF2F2F2), Code = B(0xE6E6E6), Muted = B(0xA3A3A3), WaitRed = B(0xFF5D52);
        static readonly Brush DiffDel = B(0xFF8A8A), DiffAdd = B(0x8AE39A), Hover = B(0xFFFFFF, 0x12), OnGreen = B(0x3FD07F);
        static readonly FontFamily Ui = new FontFamily("Segoe UI Variable Text, Segoe UI");
        static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");

        readonly Config _cfg = new Config(System.IO.Path.Combine(Program.Root, "config.txt"));

        List<Session> _sessions = new List<Session>();
        readonly List<Pending> _pending = new List<Pending>();
        List<View> _views = new List<View>();

        // ---- arbre visuel
        readonly Border _shape = new Border { Background = Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        readonly Border _shadowHost = new Border();
        readonly DropShadowEffect _shadow = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 12, Direction = 270, Color = Colors.Black, Opacity = 0 };
        readonly Grid _inner = new Grid { ClipToBounds = false };
        readonly StackPanel _row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        readonly StackPanel _bips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _more = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        // Sourdine active : un petit croissant a droite des Bip.
        readonly TextBlock _moon = new TextBlock { Text = "☾", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Collapsed };
        readonly Grid _peek = new Grid { Width = PEEK_W, Height = PEEK_H, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Opacity = 0 };
        readonly ScrollViewer _scroll = new ScrollViewer { Width = EXP_W - 2 * EXP_PAD, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                                                           Margin = new Thickness(0, TOP_PAD, 0, EXP_PAD), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                                                           HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Opacity = 0, Visibility = Visibility.Collapsed };
        readonly StackPanel _content = new StackPanel();
        readonly Rectangle _glow = new Rectangle { Height = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0 };
        readonly TranslateTransform _glowX = new TranslateTransform();
        readonly Path _shoulderL = Shoulder(false), _shoulderR = Shoulder(true);

        readonly Dictionary<string, Bip> _rowBips = new Dictionary<string, Bip>();
        readonly HashSet<string> _leaving = new HashSet<string>();
        readonly Dictionary<Pending, FrameworkElement> _cards = new Dictionary<Pending, FrameworkElement>();
        readonly List<Action> _tickers = new List<Action>();

        Form _form = Form.Line;
        readonly DateTime _startedAt = DateTime.Now;
        IntPtr _hwnd;
        double _scale = 1, _glowForWidth = -1, _colW = -1;
        bool _glowOn;
        bool _settingsOpen, _activatable, _clickThrough, _hiddenFullScreen;
        DateTime? _hoverSince, _leaveSince;
        DateTime _expandedAt = DateTime.MinValue, _layoutAt = DateTime.MinValue, _peekUntil = DateTime.MinValue, _lastFlash = DateTime.MinValue;
        string _rowSig = "", _panelSig = "";
        int _anim;   // jeton : une transformation plus recente annule la fin d'une ancienne
        double _peekW = PEEK_W, _peekH = PEEK_H;   // taille de la forme entrouverte en cours
        Pending _peekPending;                      // demande annoncee par l'entrouverture (ou null)
        // Appels : sourdine automatique tant qu'une appli d'appel utilise le micro.
        bool _inCall, _autoDnd, _autoSuppressed;
        string _callApp;                           // application de l'appel en cours (Teams, Zoom...)
        TextBlock _dndTag;                         // "☾ jusqu'a 10:30", en haut a gauche encoche depliee
        Border _dndTagBox;
        readonly Dictionary<string, DateTime> _turnStart = new Dictionary<string, DateTime>();
        // Commandes retirees a la main (croix) alors qu'elles tournent encore.
        readonly HashSet<string> _dismissed = new HashSet<string>();
        DateTime _callStart;
        DateTime? _micOffSince;

        bool Quiet { get { return _cfg.Dnd || (_autoDnd && !_autoSuppressed); } }
        DateTime _animUntil = DateTime.MinValue;   // fin de la transformation en cours

        // ------------------------------------------------------------ construction

        public NotchWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            FontFamily = Ui;
            Foreground = Ink;
            Title = "ClaudeNotch";
            Width = WIN_W;
            Height = LINE_H + WIN_MARGIN;
            Top = 0;
            Left = (SystemParameters.PrimaryScreenWidth - WIN_W) / 2;

            // Barres de defilement fines : le style WPF par defaut detonne sur le noir.
            var bars = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar));
            bars.Setters.Add(new Setter(FrameworkElement.WidthProperty, 6.0));
            bars.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 6.0));
            bars.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35));
            Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = bars;

            // Repliee : les Bip, puis "+N"
            _row.Children.Add(_bips);
            _row.Children.Add(_more);
            _moon.Foreground = Muted;
            _row.Children.Add(_moon);
            _row.Opacity = 0;
            _inner.Children.Add(_row);

            _inner.Children.Add(_peek);

            _scroll.Content = _content;
            _inner.Children.Add(_scroll);

            // Eclat d'attente : trait de 2 px qui balaie le bord bas.
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x5D, 0x52), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0xBF, 0xFF, 0x5D, 0x52), 0.5));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x5D, 0x52), 1));
            g.Freeze();
            _glow.Fill = g;
            _glow.RenderTransform = _glowX;
            _inner.Children.Add(_glow);

            // Bouton de reglages : trois traits en haut a droite, visible encoche depliee.
            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            for (int i = 0; i < 3; i++) lines.Children.Add(new Rectangle { Width = 12, Height = 1.5, Fill = Muted, Margin = new Thickness(0, 1.5, 0, 1.5), RadiusX = 0.75, RadiusY = 0.75 });
            _menuBtn = new Border { Width = 26, Height = 18, CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Child = lines,
                                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 9, 0),
                                    ToolTip = "Réglages", Opacity = 0, Visibility = Visibility.Collapsed };
            Clickable(_menuBtn, ToggleSettings, () => true);
            _inner.Children.Add(_menuBtn);

            // En miroir, en haut a gauche : "ne pas deranger" actif et pourquoi. Un clic la leve.
            _dndTag = new TextBlock { FontSize = 11.5, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
            _dndTagBox = new Border { Height = 18, Padding = new Thickness(6, 0, 6, 0), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent,
                                      Child = _dndTag, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                                      Margin = new Thickness(9, 3, 0, 0), ToolTip = "Lever « ne pas déranger »", Visibility = Visibility.Collapsed };
            Clickable(_dndTagBox, ToggleDnd, () => true);
            _inner.Children.Add(_dndTagBox);

            _shape.Child = _inner;
            _shape.Width = LINE_W;
            _shape.Height = LINE_H;
            _shape.SizeChanged += (s, e) => OnShapeSized(e.NewSize);
            _shape.MouseRightButtonUp += (s, e) => { if (_form == Form.Expanded) { e.Handled = true; ToggleSettings(); } };
            // Encoche vide : un clic n'importe ou (hors boutons et Bip) ouvre Claude.
            _shape.MouseLeftButtonUp += (s, e) => { if (!e.Handled && _form == Form.Expanded && _views.Count == 0) Native.FocusClaude(); };

            // L'ombre est portee par un parent : le decoupage du bloc la couperait.
            _shadowHost.Effect = _shadow;
            _shadowHost.Child = _shape;

            var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            top.Children.Add(_shoulderL);
            top.Children.Add(_shadowHost);
            top.Children.Add(_shoulderR);
            Content = top;

            SourceInitialized += OnSourceInitialized;

            Directory.CreateDirectory(Program.StateDir);
            Sessions.Purge(Program.StateDir);
            Journal.Purge();
            Audit.Purge(Program.Root, Audit.KeepDays);

            new PipeServer(
                p => Dispatcher.BeginInvoke((Action)(() => { _pending.Add(p); Refresh(); Peek(p); })),
                p => Dispatcher.BeginInvoke((Action)(() => { _pending.Remove(p); Refresh(); })),
                Program.Log).Start();

            Every(500, Refresh);
            _track = Every(100, Track);   // 50 ms deplie ou entrouvert (voir GoTo)
            Every(1000, () => { foreach (var t in _tickers) t(); KeepOnTop(); CheckFullScreen(); Escalate(); FitWindow(); });
            Every(2000, CheckCall);

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => Dispatcher.BeginInvoke((Action)Place);

            Refresh();
        }

        // Conge concave de 8 px qui raccorde le bloc au bord de l'ecran.
        static Path Shoulder(bool right)
        {
            var p = new Path
            {
                Data = Geometry.Parse("M0,0 H8 V8 A8,8 0 0 0 0,0 Z"), Fill = Black, Width = SHOULDER, Height = SHOULDER,
                VerticalAlignment = VerticalAlignment.Top, Stretch = Stretch.Fill
            };
            if (right) { p.RenderTransformOrigin = new Point(0.5, 0.5); p.RenderTransform = new ScaleTransform(-1, 1); }
            return p;
        }

        DispatcherTimer _track;

        DispatcherTimer Every(int ms, Action a)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { try { a(); } catch (Exception ex) { Program.Log(ex.ToString()); } };
            t.Start();
            return t;
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var src = PresentationSource.FromVisual(this);
            if (src != null && src.CompositionTarget != null) _scale = src.CompositionTarget.TransformToDevice.M11;
            SetActivatable(false);
            SetClickThrough(true);
            Place();
        }

        void Place()
        {
            Left = (SystemParameters.PrimaryScreenWidth - WIN_W) / 2;
            Top = 0;
            KeepOnTop();
        }

        // ------------------------------------------------------------ styles de fenetre

        void SetExStyle(int flag, bool on)
        {
            if (_hwnd == IntPtr.Zero) return;
            int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW;
            ex = on ? ex | flag : ex & ~flag;
            Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex);
        }

        // Repliee ou entrouverte : les clics traversent vers l'application en dessous.
        void SetClickThrough(bool on) { _clickThrough = on; SetExStyle(Native.WS_EX_TRANSPARENT, on); }

        // WS_EX_NOACTIVATE en temps normal ; levee le temps de taper une reponse.
        void SetActivatable(bool on) { _activatable = on; SetExStyle(Native.WS_EX_NOACTIVATE, !on); }

        void KeepOnTop()
        {
            if (_hwnd == IntPtr.Zero || _hiddenFullScreen) return;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        void CheckFullScreen()
        {
            bool fs = Native.FullScreenApp();
            if (fs == _hiddenFullScreen) return;
            _hiddenFullScreen = fs;
            if (fs) Hide(); else { Show(); KeepOnTop(); }
        }

        // ------------------------------------------------------------ survol (souris suivie toutes les 50 a 100 ms)

        // Le bloc est traverse par les clics quand il est replie : WPF ne voit donc pas
        // la souris. On compare la position du curseur au rectangle du bloc.
        void Track()
        {
            if (_hwnd == IntPtr.Zero || _hiddenFullScreen || !_shape.IsVisible) return;
            Native.POINT pt;
            if (!Native.GetCursorPos(out pt)) return;
            var p = new Point(pt.X / _scale - Left, pt.Y / _scale - Top);

            var origin = _shape.TranslatePoint(new Point(0, 0), this);
            double margin = _form == Form.Expanded ? 10 : 4;
            var r = new Rect(origin.X - SHOULDER - margin, 0, _shape.ActualWidth + 2 * (SHOULDER + margin),
                             Math.Max(_shape.ActualHeight, 10) + margin);
            bool inside = r.Contains(p);
            var now = DateTime.Now;
            if (_form == Form.Peek && now > _peekUntil && !inside) { GoTo(RestForm()); return; }

            if (_form != Form.Expanded)
            {
                if (!inside) { _hoverSince = null; return; }
                if (_hoverSince == null) _hoverSince = now;
                else if (now - _hoverSince.Value >= HOVER_OPEN) { _hoverSince = null; Expand(); }
                return;
            }

            bool typing = IsActive && Keyboard.FocusedElement is TextBox;
            if (inside || typing) { _leaveSince = null; return; }
            if (_leaveSince == null) _leaveSince = now;
            else if (now - _leaveSince.Value >= LEAVE_CLOSE) { _leaveSince = null; GoTo(RestForm()); }
        }

        // ------------------------------------------------------------ donnees

        void Refresh()
        {
            var loaded = Sessions.Load(Program.StateDir);
            if (loaded != null) { _sessions = loaded; JournalTransitions(loaded); }
            _pending.RemoveAll(p => p.Done);

            var views = new List<View>();
            _dismissed.RemoveWhere(id => !_sessions.Any(x => x.Id == id));
            foreach (var s in _sessions)
            {
                if (_dismissed.Contains(s.Id)) continue;
                var v = new View { S = s, State = s.State, Since = s.Since };
                v.Pending = _pending.Where(p => p.SessionId == s.Id).OrderBy(p => p.Since).ToList();
                if (v.Pending.Count > 0) { v.State = St.Waiting; v.Since = v.Pending[0].Since; }
                if (Sessions.IsActive(v.State)) views.Add(v);
            }
            // Demande d'une session sans fichier d'etat (encore jamais vue).
            foreach (var g in _pending.Where(p => !_sessions.Any(s => s.Id == p.SessionId)).GroupBy(p => p.SessionId))
            {
                var first = g.OrderBy(p => p.Since).First();
                views.Add(new View
                {
                    S = new Session { Id = g.Key, Dir = first.Cwd, Name = Session.NameOf(first.Cwd), Order = first.Since },
                    State = St.Waiting, Since = first.Since, Pending = g.OrderBy(p => p.Since).ToList()
                });
            }
            views.Sort((a, b) => a.S.Order.CompareTo(b.S.Order));
            _views = views;

            foreach (var p in _cards.Keys.Where(p => !_pending.Contains(p)).ToList()) _cards.Remove(p);

            SyncRow();
            UpdateGlow();
            if (_form == Form.Expanded) RenderPanel(false);
            else if (_form == Form.Peek && _peekPending != null && _peekPending.Done) GoTo(RestForm());
            else if (_form == Form.Line || _form == Form.Collapsed)
            {
                var rest = RestForm();
                if (rest != _form) GoTo(rest);
                else if (_form == Form.Collapsed)
                {
                    double w = CollapsedWidth();
                    if (Math.Abs(w - _colW) > 0.5) { _colW = w; Resize(w, COL_H, 250); }
                }
            }
        }

        Form RestForm() { return _views.Count == 0 ? Form.Line : Form.Collapsed; }

        readonly Dictionary<string, St> _prev = new Dictionary<string, St>();

        // Journal du jour : debut et fin de travail, session terminee. Seules les
        // transitions observees comptent (un redemarrage ne cree pas de doublon).
        void JournalTransitions(List<Session> sessions)
        {
            var seen = new HashSet<string>();
            foreach (var s in sessions)
            {
                seen.Add(s.Id);
                St prev;
                bool had = _prev.TryGetValue(s.Id, out prev);
                if (s.IsRun)
                {
                    // Commande suivie par bip run : son resultat, et une annonce si elle a dure.
                    // Une commande tres courte peut finir entre deux lectures : vue directement
                    // terminee, elle compte aussi, si elle a fini depuis le demarrage de l'encoche.
                    bool finished = s.State == St.Ready || s.State == St.Error;
                    if (finished && ((had && prev == St.Busy) || (!had && s.Since >= _startedAt)))
                    {
                        Journal.Add(s.State == St.Ready ? "commande-ok" : "commande-ko", s.Id, s.Label ?? s.Name);
                        if (s.RunTime >= TimeSpan.FromSeconds(30) && !_dismissed.Contains(s.Id)) PeekRun(s);
                    }
                    _prev[s.Id] = s.State;
                    continue;
                }
                if (s.State == St.Busy && (!had || prev != St.Busy))
                {
                    Journal.Add("travail", s.Id, s.Display);
                    _turnStart[s.Id] = DateTime.Now;   // debut du tour : base du recap de fin de tour
                }
                if (had && prev == St.Busy && s.State != St.Busy) Journal.Add("pause", s.Id, s.Display);
                if (had && prev != St.Ready && s.State == St.Ready) Journal.Add("termine", s.Id, s.Display);
                _prev[s.Id] = s.State;
            }
            foreach (var id in _prev.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                if (_prev[id] == St.Busy && !id.StartsWith("run-")) Journal.Add("pause", id, "");
                _prev.Remove(id);
            }
        }

        static BipState BipOf(St s)
        {
            switch (s)
            {
                case St.Waiting: return BipState.Waiting;
                case St.Busy:    return BipState.Running;
                case St.Ready:   return BipState.Ready;
                case St.Error:   return BipState.Error;
                default:         return BipState.Idle;
            }
        }

        Bip MakeBip(View v, double size)
        {
            var b = new Bip { Width = size, Height = size };
            if (v.State == St.Waiting) b.WaitingSince = v.Since;
            b.State = BipOf(v.State);
            return b;
        }

        // ------------------------------------------------------------ forme repliee

        // Les Bip restent les memes objets d'un rafraichissement a l'autre : leurs
        // animations continuent, et un changement d'etat se joue (saut, affaissement...).
        void SyncRow()
        {
            var shown = _views.Take(MAX_BIPS).ToList();
            int extra = _views.Count - shown.Count;
            bool extraWaiting = _views.Skip(MAX_BIPS).Any(v => v.State == St.Waiting);
            string sig = string.Join(";", shown.Select(v => v.S.Id + ":" + v.State)) + "|" + extra + extraWaiting;
            if (sig == _rowSig) return;
            _rowSig = sig;

            var ids = shown.Select(v => v.S.Id).ToList();

            // Sorties : fondu de 400 ms, puis retrait.
            foreach (var id in _rowBips.Keys.Where(k => !ids.Contains(k) && !_leaving.Contains(k)).ToList())
            {
                var gone = _rowBips[id];
                var key = id;
                _leaving.Add(key);
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400));
                fade.Completed += (s, e) =>
                {
                    _bips.Children.Remove(gone);
                    _rowBips.Remove(key);
                    _leaving.Remove(key);
                    if (_form == Form.Collapsed) { _colW = CollapsedWidth(); Resize(_colW, COL_H, 250); }
                };
                gone.BeginAnimation(OpacityProperty, fade);
            }

            for (int i = 0; i < shown.Count; i++)
            {
                var v = shown[i];
                Bip b;
                if (_rowBips.TryGetValue(v.S.Id, out b) && !_leaving.Contains(v.S.Id))
                {
                    if (v.State == St.Waiting && b.State != BipState.Waiting) b.WaitingSince = v.Since;
                    b.State = BipOf(v.State);
                    b.ToolTip = v.S.Display + " — " + States.Label(v.State);
                    continue;
                }
                if (b != null) { _bips.Children.Remove(b); _rowBips.Remove(v.S.Id); _leaving.Remove(v.S.Id); }

                b = MakeBip(v, COL_BIP);
                b.Fps = 8;   // 16 px : 8 images/s suffisent, moitie moins de rendu
                b.Margin = new Thickness(COL_GAP / 2, 0, COL_GAP / 2, 0);
                b.Tag = v.S.Id;
                // Arrivee : petit grossissement depuis le centre.
                var grow = new ScaleTransform(0.3, 0.3);
                b.RenderTransformOrigin = new Point(0.5, 0.5);
                b.RenderTransform = grow;
                var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };
                grow.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
                grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });

                // Place dans l'ordre de lancement, apres les Bip qui le precedent.
                int at = 0;
                for (int c = 0; c < _bips.Children.Count; c++)
                {
                    var tag = (string)((FrameworkElement)_bips.Children[c]).Tag;
                    if (ids.IndexOf(tag) >= 0 && ids.IndexOf(tag) < i) at = c + 1;
                }
                _bips.Children.Insert(at, b);
                _rowBips[v.S.Id] = b;
            }

            _more.Text = extra > 0 ? "+" + extra : "";
            _more.Visibility = extra > 0 ? Visibility.Visible : Visibility.Collapsed;
            _more.Foreground = extraWaiting ? WaitRed : Muted;
        }

        double CollapsedWidth()
        {
            int n = _rowBips.Count - _leaving.Count;
            if (n <= 0) n = 1;
            double w = 2 * COL_PAD + n * COL_BIP + (n - 1) * COL_GAP;
            foreach (var extra in new[] { _more, _moon })
                if (extra.Visibility == Visibility.Visible)
                {
                    extra.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    w += extra.Margin.Left + extra.DesiredSize.Width;
                }
            return w;
        }

        // ------------------------------------------------------------ transformations

        // Change de forme. Ouverture : 500 ms, BackEase 0,3 (leger rebond) ; entrouverte :
        // 400 ms, BackEase 0,2 ; fermeture : contenu efface en 100 ms, bloc en 350 ms.
        void GoTo(Form f)
        {
            var from = _form;
            if (f == from && f != Form.Expanded) return;
            _form = f;
            int token = ++_anim;
            UpdateDndTag();

            SetClickThrough(f != Form.Expanded);
            // Souris suivie toutes les 50 ms quand l'encoche est ouverte (depart rapide),
            // 100 ms repliee : l'ouverture attend de toute facon 200 ms de survol.
            if (_track != null) _track.Interval = TimeSpan.FromMilliseconds(f == Form.Expanded || f == Form.Peek ? 50 : 100);
            if (f != Form.Expanded && _activatable) SetActivatable(false);
            _shadow.BeginAnimation(DropShadowEffect.OpacityProperty,
                new DoubleAnimation(f == Form.Expanded || f == Form.Peek ? 0.4 : 0, TimeSpan.FromMilliseconds(250)));
            ShoulderSize(f == Form.Line ? 4 : SHOULDER);

            if (f == Form.Expanded)
            {
                _expandedAt = DateTime.Now;
                _settingsOpen = false;
                _menuBtn.Visibility = Visibility.Visible;
                FadeTo(_menuBtn, 1, 180, 250);
                FadeTo(_row, 0, 100, 0);
                HidePeek(token);
                _scroll.Visibility = Visibility.Visible;
                _scroll.BeginAnimation(OpacityProperty, null);
                _scroll.Opacity = 1;
                RenderPanel(true);
                _scroll.ScrollToTop();
                Resize(EXP_W, ExpandedHeight(), 500, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 });
                return;
            }

            if (f == Form.Peek)
            {
                FadeTo(_row, 0, 100, 0);
                FadeTo(_peek, 1, 180, 150);
                Resize(_peekW, _peekH, 400, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 });
                return;
            }

            // Repliee ou trait : on efface ce qui etait montre, puis le bloc se resserre.
            bool closing = from == Form.Expanded || from == Form.Peek;
            if (from == Form.Expanded)
            {
                _menuBtn.BeginAnimation(OpacityProperty, null);
                _menuBtn.Opacity = 0;
                _menuBtn.Visibility = Visibility.Collapsed;
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
                fade.Completed += (s, e) =>
                {
                    if (token != _anim) return;
                    // Panneau vide une fois cache : ses Bip s'arretent (sinon ils restent animes,
                    // invisibles, et l'encoche consomme 2 a 3 fois plus). Rebati a l'ouverture.
                    _scroll.Visibility = Visibility.Collapsed;
                    _content.Children.Clear();
                    _tickers.Clear();
                    _panelSig = null;
                };
                _scroll.BeginAnimation(OpacityProperty, fade);
            }
            HidePeek(token);
            double w = f == Form.Line ? LINE_W : CollapsedWidth(), h = f == Form.Line ? LINE_H : COL_H;
            _colW = f == Form.Collapsed ? w : -1;
            if (closing) ResizeSpline(w, h, 350, 60);
            else Resize(w, h, 300, new CubicEase { EasingMode = EasingMode.EaseOut });
            FadeTo(_row, f == Form.Collapsed ? 1 : 0, 180, closing ? 300 : 0);
        }

        void Expand()
        {
            if (_form == Form.Expanded) return;
            _leaveSince = null;
            GoTo(Form.Expanded);
        }

        void Resize(double w, double h, int ms, IEasingFunction ease = null)
        {
            Room(h, ms);
            if (ease == null) ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var d = TimeSpan.FromMilliseconds(ms);
            _shape.BeginAnimation(WidthProperty, new DoubleAnimation(w, d) { EasingFunction = ease });
            _shape.BeginAnimation(HeightProperty, new DoubleAnimation(h, d) { EasingFunction = ease });
        }

        // Fermeture : courbe 0.4,0 0.2,1 (KeySpline), sans rebond.
        void ResizeSpline(double w, double h, int ms, int delayMs)
        {
            Room(h, ms + delayMs);
            foreach (var pair in new[] { Tuple.Create(WidthProperty, w), Tuple.Create(HeightProperty, h) })
            {
                var a = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(delayMs) };
                a.KeyFrames.Add(new SplineDoubleKeyFrame(pair.Item2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new KeySpline(0.4, 0, 0.2, 1)));
                _shape.BeginAnimation(pair.Item1, a);
            }
        }

        // La fenetre est transparente et redessinee a chaque image : sa surface fait le
        // cout. Elle grandit juste avant que l'encoche grandisse...
        void Room(double targetH, int ms)
        {
            _animUntil = DateTime.Now.AddMilliseconds(ms + 100);
            double need = Math.Max(targetH, _shape.ActualHeight) + WIN_MARGIN;
            if (need > Height) Height = need;
        }

        // ...et se resserre une fois la transformation finie.
        void FitWindow()
        {
            if (DateTime.Now < _animUntil) return;
            double need = _shape.ActualHeight + WIN_MARGIN;
            if (Height > need + 1) Height = need;
        }

        // Efface l'entrouverture, puis la vide : son Bip s'arrete au lieu de rester anime, invisible.
        void HidePeek(int token)
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            fade.Completed += (s, e) => { if (token == _anim && _form != Form.Peek) _peek.Children.Clear(); };
            _peek.BeginAnimation(OpacityProperty, fade);
        }

        static void FadeTo(UIElement el, double to, int ms, int delayMs)
        {
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
                { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        void ShoulderSize(double s)
        {
            foreach (var p in new[] { _shoulderL, _shoulderR }) { p.Width = s; p.Height = s; }
        }

        // Rayon du bas qui suit la hauteur : 2 (trait), 11 (repliee), 20 (entrouverte), 24 (depliee).
        static double RadiusFor(double h)
        {
            if (h <= COL_H) return h / 2;
            if (h <= PEEK_H) return 11 + (h - COL_H) * 9 / (PEEK_H - COL_H);
            return Math.Min(24, 20 + (h - PEEK_H) / 15);
        }

        // A chaque image de la transformation : coins, decoupage et eclat suivent.
        void OnShapeSized(Size s)
        {
            double r = RadiusFor(s.Height);
            _shape.CornerRadius = new CornerRadius(0, 0, r, r);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, 0), true, true);
                c.LineTo(new Point(s.Width, 0), false, false);
                c.LineTo(new Point(s.Width, s.Height - r), false, false);
                c.ArcTo(new Point(s.Width - r, s.Height), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
                c.LineTo(new Point(r, s.Height), false, false);
                c.ArcTo(new Point(0, s.Height - r), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
            }
            g.Freeze();
            _shape.Clip = g;
            if (_glowOn && Math.Abs(s.Width - _glowForWidth) > 0.5) StartGlow(s.Width);
        }

        double ExpandedHeight()
        {
            _content.Measure(new Size(EXP_W - 2 * EXP_PAD, double.PositiveInfinity));
            double max = SystemParameters.PrimaryScreenHeight * 0.85;
            _scroll.MaxHeight = max - TOP_PAD - EXP_PAD;
            return Math.Min(_content.DesiredSize.Height + TOP_PAD + EXP_PAD, max);
        }

        // ------------------------------------------------------------ eclat d'attente

        void UpdateGlow()
        {
            bool any = !Quiet && _views.Any(v => v.State == St.Waiting);
            if (any == _glowOn) return;
            _glowOn = any;
            var fade = new DoubleAnimation(any ? 1 : 0, TimeSpan.FromMilliseconds(300));
            if (any) StartGlow(_shape.ActualWidth);
            // Plus personne n'attend : le balayage s'arrete une fois le fondu fini.
            else fade.Completed += (s, e) => { if (!_glowOn) { _glowX.BeginAnimation(TranslateTransform.XProperty, null); _glowForWidth = -1; } };
            _glow.BeginAnimation(OpacityProperty, fade);
        }

        // 40 % de la largeur, balaie de gauche a droite en 2,4 s, lineaire, en boucle.
        void StartGlow(double width)
        {
            _glowForWidth = width;
            _glow.Width = width * 0.4;
            _glowX.BeginAnimation(TranslateTransform.XProperty, Bip.Loop(new DoubleAnimation(-width * 0.4, width, TimeSpan.FromMilliseconds(2400))
                { RepeatBehavior = RepeatBehavior.Forever }, 30));
        }

        // Respiration geree par Bip ; au-dela de 5 min d'attente, la fenetre Claude clignote.
        void Escalate()
        {
            var longest = _views.Where(v => v.State == St.Waiting).Select(v => DateTime.Now - v.Since).DefaultIfEmpty(TimeSpan.Zero).Max();
            UpdateGlow();   // fin du "ne pas deranger" : l'eclat reprend s'il le faut
            UpdateMoon();
            if (_cfg.Flash && !Quiet && longest >= FLASH_AFTER && DateTime.Now - _lastFlash > FLASH_EVERY)
            {
                _lastFlash = DateTime.Now;
                Native.FlashClaude();   // clignotement seulement : jamais de premier plan force
            }
        }

        // ------------------------------------------------------------ forme entrouverte

        // Montre un contenu dans la forme entrouverte, puis se replie au bout de "hold".
        void ShowPeek(UIElement content, double w, double h, TimeSpan hold, Pending pending)
        {
            if (_form == Form.Expanded) return;
            _peekPending = pending;
            _peek.Children.Clear();
            _peek.Children.Add(content);
            _peek.Width = w;
            _peek.Height = h;
            _peekUntil = DateTime.Now + hold;
            if (_form == Form.Peek && (Math.Abs(w - _peekW) > 0.5 || Math.Abs(h - _peekH) > 0.5))
            {
                _peekW = w; _peekH = h;
                Resize(w, h, 300, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 });
                return;
            }
            _peekW = w; _peekH = h;
            GoTo(Form.Peek);
        }

        // Une nouvelle demande : l'encoche s'agrandit 4 s pour dire qui attend, puis se replie.
        void Peek(Pending p)
        {
            if (_form == Form.Expanded || p.Done || Quiet) return;
            var v = _views.FirstOrDefault(x => x.S.Id == p.SessionId);
            string name = v != null ? v.S.ShortName : Session.NameOf(p.Cwd);

            _peek.Children.Clear();
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 18, 0) };
            var bip = new Bip { Width = 40, Height = 40, State = BipState.Waiting, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(bip);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            // Largeur adaptee au titre de la session : 320 px au moins, la largeur depliee au plus.
            const double CHROME = 12 + 40 + 12 + 18;
            var line = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = EXP_W - CHROME };
            line.Inlines.Add(new Run(name) { FontFamily = Mono });
            line.Inlines.Add(new Run(" attend ta réponse"));
            text.Children.Add(line);
            text.Children.Add(new TextBlock
            {
                FontSize = 12, Foreground = Muted,
                Text = p.Kind == "ask" ? "Question" : "Permission · " + Safe(ToolLabel(p.ToolName))
            });
            row.Children.Add(text);
            line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = Math.Max(PEEK_W, Math.Min(EXP_W, Math.Ceiling(line.DesiredSize.Width) + CHROME + 2));
            ShowPeek(row, w, PEEK_H, PEEK_HOLD, p);
        }

        // Fin d'une commande longue (bip run) : reussie ou en echec, en 4 s.
        void PeekRun(Session s)
        {
            if (_form == Form.Expanded || Quiet) return;
            bool ok = s.State == St.Ready, cut = s.Code == "interrompu";
            const double W = 380;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 18, 0) };
            row.Children.Add(new Bip { Width = 40, Height = 40, State = ok ? BipState.Ready : BipState.Error, VerticalAlignment = VerticalAlignment.Center });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var line = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = W - 12 - 40 - 12 - 18 };
            line.Inlines.Add(new Run(Safe(s.Label ?? s.Name)) { FontFamily = Mono });
            line.Inlines.Add(new Run(ok ? " a fini" : cut ? " a été interrompue" : " a échoué"));
            text.Children.Add(line);
            text.Children.Add(new TextBlock
            {
                FontSize = 12, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = W - 12 - 40 - 12 - 18,
                Text = RunDur(s.RunTime) + (!ok && !cut ? " · code " + s.Code : "") + " · " + Session.NameOf(s.Dir)
            });
            row.Children.Add(text);
            ShowPeek(row, W, PEEK_H, PEEK_HOLD, null);
        }

        static string RunDur(TimeSpan t)
        {
            if (t.TotalSeconds < 60) return Math.Max(0, (int)t.TotalSeconds) + " s";
            if (t.TotalMinutes < 60) return (int)t.TotalMinutes + " min " + t.Seconds.ToString("00");
            return (int)t.TotalHours + " h " + t.Minutes.ToString("00");
        }

        // ------------------------------------------------------------ appels : sourdine automatique

        // Applications d'appel reconnues : nom exact de l'executable (applis de bureau) ou
        // debut du nom de paquet (applis du Store). L'app Claude (mode vocal) n'en fait pas
        // partie : dicter ne coupe rien.
        static readonly Dictionary<string, string> CallExes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ms-teams.exe", "Teams" }, { "teams.exe", "Teams" }, { "zoom.exe", "Zoom" }, { "cpthost.exe", "Zoom" },
            { "webex.exe", "Webex" }, { "ciscocollabhost.exe", "Webex" }, { "atmgr.exe", "Webex" },
            { "slack.exe", "Slack" }, { "discord.exe", "Discord" }, { "3cxdesktopapp.exe", "3CX" }, { "skype.exe", "Skype" },
            { "whatsapp.exe", "WhatsApp" }, { "msedge.exe", "navigateur" }, { "chrome.exe", "navigateur" },
            { "firefox.exe", "navigateur" }, { "brave.exe", "navigateur" }, { "opera.exe", "navigateur" }, { "vivaldi.exe", "navigateur" }
        };
        static readonly Dictionary<string, string> CallPackages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "MSTeams_", "Teams" }, { "MicrosoftTeams_", "Teams" }, { "3CX", "3CX" }, { "Zoom", "Zoom" },
            { "5319275A.WhatsAppDesktop_", "WhatsApp" }, { "Microsoft.SkypeApp_", "Skype" }, { "Discord", "Discord" }
        };

        // Appli d'appel qui utilise le micro en ce moment, ou null. Windows le note dans le
        // registre (CapabilityAccessManager) : LastUsedTimeStop a 0 = micro ouvert.
        static string CallApp()
        {
            try
            {
                using (var mic = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone"))
                {
                    if (mic == null) return null;
                    foreach (var name in mic.GetSubKeyNames())
                    {
                        if (name == "NonPackaged")
                        {
                            using (var np = mic.OpenSubKey(name))
                                if (np != null)
                                    foreach (var exe in np.GetSubKeyNames())
                                    {
                                        var hit = MicInUse(np, exe, exe.Substring(exe.LastIndexOf('#') + 1), true);
                                        if (hit != null) return hit;
                                    }
                        }
                        else
                        {
                            var hit = MicInUse(mic, name, name, false);
                            if (hit != null) return hit;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        static string MicInUse(Microsoft.Win32.RegistryKey parent, string sub, string label, bool exe)
        {
            string app = null;
            if (exe) CallExes.TryGetValue(label, out app);
            else foreach (var kv in CallPackages) if (label.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)) { app = kv.Value; break; }
            if (app == null) return null;
            using (var k = parent.OpenSubKey(sub))
            {
                if (k == null) return null;
                var stop = k.GetValue("LastUsedTimeStop");
                var start = k.GetValue("LastUsedTimeStart");
                if (!(stop is long) || (long)stop != 0 || !(start is long) || (long)start <= 0) return null;
            }
            return app;
        }

        // Toutes les 2 s. Debut d'appel : sourdine + micro-ouverture. Fin d'appel (micro
        // ferme depuis 1 min) : sourdine levee et resume de ce qui s'est passe.
        void CheckCall()
        {
            var app = CallApp();
            var now = DateTime.Now;
            if (app != null)
            {
                _micOffSince = null;
                if (_inCall) return;
                _inCall = true;
                _callStart = now;
                _callApp = app;
                // Armee meme sous une sourdine manuelle : si celle-ci expire pendant l'appel,
                // l'automatique prend le relais. La micro-ouverture seulement si ca change quelque chose.
                if (_cfg.AutoDnd)
                {
                    bool wasQuiet = Quiet;
                    _autoDnd = true;
                    _autoSuppressed = false;
                    QuietChanged();
                    if (!wasQuiet) MicroPeek("☾  Ne pas déranger · appel en cours (" + app + ")");
                }
                return;
            }
            if (!_inCall) return;
            if (_micOffSince == null) { _micOffSince = now; return; }
            if (now - _micOffSince.Value < TimeSpan.FromMinutes(1)) return;

            _inCall = false;
            _micOffSince = null;
            if (!_autoDnd) return;
            _autoDnd = false;
            _autoSuppressed = false;
            QuietChanged();
            var recap = CallRecap(_callStart);
            if (recap != null) PeekRecap(recap);
        }

        // "Pendant ton appel : 2 sessions ont fini · data-pipeline t'attend depuis 12 min", ou null.
        string CallRecap(DateTime from)
        {
            var ev = Journal.Since(from);
            var parts = new List<string>();
            int done = ev.Count(e => e.Item2 == "termine");
            if (done > 0) parts.Add(done + (done > 1 ? " sessions ont fini" : " session a fini"));
            int ok = ev.Count(e => e.Item2 == "commande-ok"), ko = ev.Count(e => e.Item2 == "commande-ko");
            if (ok > 0) parts.Add(ok + (ok > 1 ? " commandes ont réussi" : " commande a réussi"));
            if (ko > 0) parts.Add(ko + (ko > 1 ? " commandes ont échoué" : " commande a échoué"));
            var waiting = _views.Where(v => v.State == St.Waiting).OrderBy(v => v.Since).ToList();
            if (waiting.Count > 0)
                parts.Add(waiting[0].S.ShortName + " t'attend depuis " + Fmt(DateTime.Now - waiting[0].Since)
                          + (waiting.Count > 1 ? " (+" + (waiting.Count - 1) + ")" : ""));
            return parts.Count == 0 ? null : "Pendant ton appel : " + string.Join(" · ", parts);
        }

        // Micro-ouverture de 2 s, une seule ligne (meme en sourdine : c'est elle qu'on annonce).
        void MicroPeek(string text)
        {
            var t = new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Ink,
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            ShowPeek(t, Math.Max(240, t.DesiredSize.Width + 40), 34, TimeSpan.FromSeconds(2), null);
        }

        void PeekRecap(string recap)
        {
            const double W = 400;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 18, 0) };
            row.Children.Add(new Bip { Width = 40, Height = 40, State = BipState.Neutral, VerticalAlignment = VerticalAlignment.Center });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            text.Children.Add(new TextBlock { Text = "Appel terminé", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ink });
            text.Children.Add(new TextBlock { Text = recap, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap, MaxWidth = W - 12 - 40 - 12 - 18 });
            row.Children.Add(text);
            ShowPeek(row, W, 76, TimeSpan.FromSeconds(6), null);
        }

        // La sourdine a change : eclat, croissant et bouton suivent.
        void QuietChanged()
        {
            UpdateGlow();
            UpdateMoon();
            if (_dndText != null)
            {
                _dndText.Text = DndLabel();
                var b = _dndText.Parent as Border;
                if (b != null) b.Background = Quiet ? Btn2 : Brushes.Transparent;
            }
        }

        // "☾ jusqu'a 10:30" ou "☾ appel en cours (Teams)", encoche depliee seulement.
        void UpdateDndTag()
        {
            if (_dndTagBox == null) return;
            _dndTag.Text = _cfg.Dnd ? "☾ jusqu'à " + _cfg.DndUntil.ToString("HH:mm")
                                    : "☾ appel en cours" + (_callApp != null ? " (" + _callApp + ")" : "");
            _dndTagBox.Visibility = _form == Form.Expanded && Quiet ? Visibility.Visible : Visibility.Collapsed;
        }

        void UpdateMoon()
        {
            UpdateDndTag();
            var v = Quiet ? Visibility.Visible : Visibility.Collapsed;
            if (_moon.Visibility == v) return;
            _moon.Visibility = v;
            if (_form == Form.Collapsed) { _colW = CollapsedWidth(); Resize(_colW, COL_H, 250); }
        }

        static string ToolLabel(string tool)
        {
            if (tool.StartsWith("mcp__"))
            {
                var parts = tool.Split(new[] { "__" }, StringSplitOptions.None);
                if (parts.Length >= 3) return parts[2];
            }
            return tool;
        }

        // ------------------------------------------------------------ forme depliee

        void RenderPanel(bool opening)
        {
            string sig = string.Join(";", _views.Select(v => v.S.Id + ":" + v.State + ":" + v.S.Display + ":" + v.Pending.Count + ":"
                                                         + string.Join(",", VisibleAgents(v.S).Select(a => a.Tuid + (a.End != null ? "+" : "")))))
                       + "|" + string.Join(",", _pending.Select(p => p.GetHashCode()))
                       + "|" + (_settingsOpen ? Quiet + ":" + _cfg.AutoDnd + ":" + _cfg.Flash + ":" + Rules.Created().Count : "");
            if (sig == _panelSig && !opening) return;
            // Pendant une saisie, on ne reconstruit rien : le focus serait perdu.
            if (!opening && IsActive && Keyboard.FocusedElement is TextBox) return;
            _layoutAt = DateTime.Now;   // la liste bouge : les boutons se re-arment

            _tickers.Clear();
            _content.Children.Clear();

            // Les demandes d'abord, en grand ; puis les sessions, en attente d'abord.
            foreach (var v in _views.Where(v => v.Pending.Count > 0).OrderBy(v => v.Since))
                foreach (var p in v.Pending)
                {
                    FrameworkElement card;
                    if (!_cards.TryGetValue(p, out card)) { card = BuildCard(v, p); _cards[p] = card; }
                    _content.Children.Add(card);
                    var since = (TextBlock)card.Tag;
                    var pp = p;
                    _tickers.Add(() => since.Text = "attend depuis " + Fmt(DateTime.Now - pp.Since));
                }

            var rows = _views.Where(v => v.Pending.Count == 0).OrderBy(v => v.State == St.Waiting ? 0 : 1).ThenBy(v => v.S.Order).ToList();
            for (int i = 0; i < rows.Count; i++) _content.Children.Add(Row(rows[i], i > 0));
            if (_views.Count == 0)
                _content.Children.Add(EmptyPanel());
            if (_settingsOpen)
            {
                _content.Children.Add(new Rectangle { Height = 1, Fill = Btn2, Margin = new Thickness(0, 10, 0, 8) });
                _content.Children.Add(_settingsEl = Settings());
            }

            foreach (var t in _tickers) t();
            _panelSig = sig;

            if (opening)
            {
                // Le contenu arrive en cascade : chaque ligne 0 -> 1 et +4 -> 0 px en 180 ms,
                // a partir de 200 ms, decalee de 40 ms.
                for (int i = 0; i < _content.Children.Count; i++)
                {
                    var el = (FrameworkElement)_content.Children[i];
                    var tt = new TranslateTransform(0, 4);
                    el.RenderTransform = tt;
                    el.Opacity = 0;
                    var begin = TimeSpan.FromMilliseconds(200 + 40 * i);
                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { BeginTime = begin, EasingFunction = ease });
                    tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(180)) { BeginTime = begin, EasingFunction = ease });
                }
            }
            else
            {
                // Contenu change pendant que c'est deplie : le bloc suit en douceur.
                foreach (FrameworkElement el in _content.Children) { el.BeginAnimation(OpacityProperty, null); el.Opacity = 1; el.RenderTransform = null; }
                Resize(EXP_W, ExpandedHeight(), 220);
            }
        }

        // ------------------------------------------------------------ reglages (bouton en haut a droite)

        readonly Border _menuBtn;
        FrameworkElement _settingsEl;

        void ToggleSettings()
        {
            _settingsOpen = !_settingsOpen;
            _panelSig = null;
            RenderPanel(false);
            if (_settingsOpen && _settingsEl != null)
                Dispatcher.BeginInvoke((Action)(() => _settingsEl.BringIntoView()), DispatcherPriority.Loaded);
        }

        bool SettingsArmed() { return DateTime.Now - _expandedAt > ARM_DELAY && DateTime.Now - _layoutAt > ARM_DELAY; }

        // Un changement de reglage : on redessine le panneau pour refleter le nouvel etat.
        void Redraw() { _panelSig = null; RenderPanel(false); }

        // Clic complet (appui puis relache sur l'element), survol eclairci.
        static void Clickable(Border b, Action click, Func<bool> armed)
        {
            b.Cursor = Cursors.Hand;
            bool down = false;
            var normal = b.Background;
            b.MouseEnter += (s, e) => b.Background = Hover;
            b.MouseLeave += (s, e) => { down = false; b.Background = normal; };
            b.MouseLeftButtonDown += (s, e) => { down = true; e.Handled = true; };
            b.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (!down) return;
                down = false;
                if (armed()) click();
            };
        }

        // Reglages integres a l'encoche, sous le contenu, separes par un trait.
        FrameworkElement Settings()
        {
            var p = new StackPanel { Background = Brushes.Transparent, Cursor = Cursors.Arrow };
            p.MouseLeftButtonUp += (s, e) => e.Handled = true;   // encoche vide : pas d'ouverture de Claude ici

            p.Children.Add(Toggle(DndLabel(), "1 h sans entrouverture, éclat ni clignotement", Quiet, () => ToggleDnd()));
            p.Children.Add(Toggle("Sourdine automatique pendant les appels", "Teams, Zoom, Meet… repérés à l'usage du micro", _cfg.AutoDnd, () =>
            {
                _cfg.AutoDnd = !_cfg.AutoDnd;
                _cfg.Save();
                if (!_cfg.AutoDnd && _autoDnd) { _autoDnd = false; QuietChanged(); }
            }));
            p.Children.Add(Toggle("Faire clignoter Claude", "quand une demande attend depuis 5 min", _cfg.Flash, () => { _cfg.Flash = !_cfg.Flash; _cfg.Save(); }));

            p.Children.Add(Section("Traçabilité"));
            p.Children.Add(Link("Journal d'audit", "ce que les agents ont fait, et qui l'a autorisé", "↗", () =>
            {
                System.Threading.Tasks.Task.Run(() => Report.Open(Audit.KeepDays));
            }));

            var rules = Rules.Created();
            p.Children.Add(Section("Règles créées par Bip"));
            if (rules.Count == 0)
                p.Children.Add(new TextBlock { Text = "Aucune pour l'instant. Bip en propose après 5 autorisations de la même commande.", FontSize = 12, Foreground = Muted,
                                               TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 8, 4) });
            foreach (var c in rules)
            {
                var rule = c;
                p.Children.Add(Link(rule.Rule, (rule.Scope == "partout" ? "partout" : Session.NameOf(rule.Dir)) + " · " + rule.T.ToString("dd/MM"), "✕", () =>
                {
                    try { Rules.Remove(rule); } catch (Exception ex) { Program.Log("regle : " + ex.Message); }
                    Redraw();
                }, true));
            }

            // Pied : version a gauche, actions secondaires a droite.
            var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
            foot.Children.Add(new TextBlock { Text = "Bip v" + AppVersion.Text, FontSize = 11.5, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(actions, Dock.Right);
            actions.Children.Add(Small("Dossier d'état", () => { try { System.Diagnostics.Process.Start("explorer.exe", Program.StateDir); } catch { } }));
            actions.Children.Add(Small("Purger les états", () =>
            {
                try { foreach (var f in Directory.GetFiles(Program.StateDir)) File.Delete(f); } catch { }   // etats et sous-agents
                Refresh();
            }));
            actions.Children.Add(Small("Quitter Bip", () =>
            {
                // Les demandes en attente repartent vers Claude.
                foreach (var q in _pending.ToList()) ThreadPool.QueueUserWorkItem(_ => q.Reply(""));
                Application.Current.Shutdown();
            }));
            foot.Children.Add(actions);
            p.Children.Add(foot);
            return p;
        }

        static TextBlock Section(string title)
        {
            return new TextBlock { Text = title, FontSize = 11.5, Foreground = Muted, Margin = new Thickness(8, 10, 0, 2) };
        }

        // Ligne : intitule et precision a gauche, interrupteur a droite. Toute la ligne est cliquable.
        FrameworkElement Toggle(string label, string hint, bool on, Action flip)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = label, FontSize = 12.5, Foreground = Ink });
            if (hint != null) text.Children.Add(new TextBlock { Text = hint, FontSize = 11, Foreground = Muted });
            g.Children.Add(text);
            var knob = new Ellipse { Width = 13, Height = 13, Fill = Ink, HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(2, 0, 2, 0) };
            var sw = new Border { Width = 30, Height = 17, CornerRadius = new CornerRadius(8.5), Background = on ? OnGreen : Always, Child = knob,
                                  VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(sw, 1);
            g.Children.Add(sw);
            var row = new Border { Child = g, Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
            Clickable(row, () => { flip(); Redraw(); }, SettingsArmed);
            return row;
        }

        // Ligne d'action : intitule, precision, et un signe a droite (↗ ouvrir, ✕ retirer).
        FrameworkElement Link(string label, string hint, string sign, Action click, bool mono = false)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var t = new TextBlock { Text = label, FontSize = 12.5, Foreground = mono ? Code : Ink, TextTrimming = TextTrimming.CharacterEllipsis };
            if (mono) t.FontFamily = Mono;
            text.Children.Add(t);
            if (hint != null) text.Children.Add(new TextBlock { Text = hint, FontSize = 11, Foreground = Muted });
            g.Children.Add(text);
            var mark = new TextBlock { Text = sign, FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 2, 0) };
            Grid.SetColumn(mark, 1);
            g.Children.Add(mark);
            var row = new Border { Child = g, Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
            Clickable(row, click, SettingsArmed);
            return row;
        }

        FrameworkElement Small(string label, Action click)
        {
            var b = new Border { Padding = new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Margin = new Thickness(2, 0, 0, 0),
                                 Child = new TextBlock { Text = label, FontSize = 11.5, Foreground = Muted } };
            Clickable(b, click, SettingsArmed);
            return b;
        }

        // ------------------------------------------------------------ encoche vide

        static readonly string[] Morning = { "Bonjour ! C'est Bip.", "Bonjour, Bip au rapport.", "Salut ! Bip veille sur tes sessions." };
        static readonly string[] Afternoon = { "Salut, c'est Bip !", "Bip au rapport : tout est calme.", "Coucou, Bip est là." };
        static readonly string[] Evening = { "Bonsoir ! C'est Bip.", "Bonsoir, Bip veille encore.", "Bip est là, tout est calme." };
        static readonly Random Rng = new Random();
        DispatcherTimer _typing;
        TextBlock _dndText;

        // Bip qui salue (avec le bouton "ne pas deranger"), les dernieres sessions
        // terminees, puis le bilan du jour.
        FrameworkElement EmptyPanel()
        {
            var panel = new StackPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Ouvrir Claude" };
            panel.Children.Add(Greeting());

            var st = Journal.Today();
            if (st.Finished.Count > 0)
            {
                panel.Children.Add(new TextBlock { Text = "Dernières sessions", FontSize = 12, Foreground = Muted, Margin = new Thickness(4, 2, 0, 2) });
                foreach (var f in st.Finished)
                {
                    var g = new Grid { Height = 30 };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var bip = new Bip { Width = 18, Height = 18, State = BipState.Idle, Margin = new Thickness(5, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center };
                    g.Children.Add(bip);
                    var name = new TextBlock { Text = f.Item1, FontFamily = Mono, FontSize = 12.5, Foreground = Code, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    Grid.SetColumn(name, 1);
                    g.Children.Add(name);
                    var when = new TextBlock { Text = "a fini il y a " + Fmt(DateTime.Now - f.Item2), FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) };
                    Grid.SetColumn(when, 2);
                    g.Children.Add(when);
                    panel.Children.Add(g);
                }
            }

            panel.Children.Add(new Rectangle { Height = 1, Fill = Sep, Margin = new Thickness(0, 6, 0, 6) });
            panel.Children.Add(new TextBlock { Text = StatsText(st), FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 2) });
            var audit = AuditToday();
            if (audit != null)
                panel.Children.Add(new TextBlock { Text = audit, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 2, 4, 2) });
            return panel;
        }

        static string StatsText(DayStats st)
        {
            var parts = new List<string>();
            if (st.Sessions > 0) parts.Add(st.Sessions + (st.Sessions > 1 ? " sessions" : " session") + " · " + Duration(st.Work) + " de travail");
            if (st.Answers > 0) parts.Add(st.Answers + (st.Answers > 1 ? " réponses" : " réponse"));
            if (st.Commands > 0)
                parts.Add(st.Commands + (st.Commands > 1 ? " commandes" : " commande")
                          + (st.CommandFailures > 0 ? " (" + st.CommandFailures + " en échec)" : ""));
            return parts.Count == 0 ? "Aujourd'hui : aucune session pour l'instant." : "Aujourd'hui · " + string.Join(" · ", parts);
        }

        // "37 actions · 5 autorisées par toi · 0 refus", d'apres le journal d'audit du jour.
        static string AuditToday()
        {
            try
            {
                var rows = Audit.Rows(Program.Root, 1).Where(r => r.T.Date == DateTime.Today && !r.Hidden && !r.Rule).ToList();
                if (rows.Count == 0) return null;
                int byYou = rows.Count(r => r.ByUser && r.Allowed), refused = rows.Count(r => r.Refused);
                return rows.Count + (rows.Count > 1 ? " actions" : " action") + " · "
                     + byYou + (byYou > 1 ? " autorisées" : " autorisée") + " par toi · "
                     + refused + (refused > 1 ? " refus" : " refus");
            }
            catch { return null; }
        }

        static string Duration(TimeSpan t)
        {
            if (t.TotalMinutes < 1) return "moins d'1 min";
            if (t.TotalMinutes < 60) return (int)t.TotalMinutes + " min";
            return (int)t.TotalHours + " h " + t.Minutes.ToString("00");
        }

        // Bip se reveille (endormi -> blanc casse souriant) et salue en une ligne, ecrite
        // lettre par lettre ; un clic sur lui le fait reagir. A droite : "ne pas deranger".
        FrameworkElement Greeting()
        {
            var hour = DateTime.Now.Hour;
            var pool = hour < 12 ? Morning : hour < 18 ? Afternoon : Evening;
            string line = pool[Rng.Next(pool.Length)];

            var row = new Grid { Margin = new Thickness(4, 4, 4, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var bip = new Bip { Width = 40, Height = 40, State = BipState.Idle, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = "Bip" };
            bip.Loaded += (s, e) => Dispatcher.BeginInvoke((Action)(() => bip.State = BipState.Neutral), DispatcherPriority.Background);
            bip.MouseLeftButtonDown += (s, e) => e.Handled = true;
            bip.MouseLeftButtonUp += (s, e) => { e.Handled = true; bip.Poke(); };
            row.Children.Add(bip);

            var text = new TextBlock { FontSize = 13, LineHeight = 18, FontWeight = FontWeights.SemiBold, Foreground = Ink, TextWrapping = TextWrapping.Wrap, MaxWidth = 190 };
            // Taille fixee des le depart : la bulle ne bouge pas pendant l'ecriture.
            text.Text = line;
            text.Measure(new Size(text.MaxWidth, double.PositiveInfinity));
            text.Width = Math.Ceiling(text.DesiredSize.Width);
            text.Height = text.DesiredSize.Height;
            text.Text = "";
            var bubble = new Border
            {
                Background = Card, CornerRadius = new CornerRadius(4, 14, 14, 14), Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(12, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Child = text
            };
            Grid.SetColumn(bubble, 1);
            row.Children.Add(bubble);

            var dnd = (Border)Btn(DndLabel(), Quiet ? Btn2 : Brushes.Transparent, Ink, Always, 28, ToggleDnd,
                                  () => DateTime.Now - _expandedAt > ARM_DELAY);
            dnd.VerticalAlignment = VerticalAlignment.Center;
            dnd.Margin = new Thickness(0);
            dnd.ToolTip = "Pendant 1 h : pas d'entrouverture, pas d'éclat, pas de clignotement de Claude";
            _dndText = (TextBlock)dnd.Child;
            Grid.SetColumn(dnd, 2);
            row.Children.Add(dnd);

            // Ecriture lettre par lettre (30 ms), apres l'ouverture de l'encoche.
            if (_typing != null) _typing.Stop();
            int n = 0;
            var start = DateTime.Now.AddMilliseconds(350);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            t.Tick += (s, e) =>
            {
                if (DateTime.Now < start) return;
                n = Math.Min(line.Length, n + 1);
                text.Text = line.Substring(0, n);
                if (n >= line.Length || _form != Form.Expanded) { t.Stop(); text.Text = line; }
            };
            _typing = t;
            t.Start();
            return row;
        }

        string DndLabel()
        {
            if (_cfg.Dnd) return "☾ Pause jusqu'à " + _cfg.DndUntil.ToString("HH:mm");
            if (_autoDnd && !_autoSuppressed) return "☾ Appel en cours";
            return "☾ Ne pas déranger";
        }

        void ToggleDnd()
        {
            // Manuelle : on la coupe. Automatique (appel) : on la leve jusqu'a la fin de l'appel.
            // Sinon : une heure de pause.
            if (_cfg.Dnd) { _cfg.DndUntil = DateTime.MinValue; _autoSuppressed = _autoDnd; }
            else if (_autoDnd && !_autoSuppressed) _autoSuppressed = true;
            else _cfg.DndUntil = DateTime.Now.AddHours(1);
            _cfg.Save();
            QuietChanged();
        }

        // Ligne de session : 36 px, Bip 20 px, nom en Cascadia Mono, etat + duree a droite.
        FrameworkElement Row(View v, bool separator)
        {
            // Session prete : le recap de son dernier tour, en vert sous le nom.
            string recap = !v.S.IsRun && v.State == St.Ready ? TurnRecap(v.S) : null;
            var g = new Grid { Height = recap == null ? 36 : 48, Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var bip = MakeBip(v, 20);
            bip.Margin = new Thickness(4, 0, 10, 0);
            bip.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(bip);

            var name = new TextBlock { Text = Safe(v.S.Display), FontFamily = Mono, FontSize = 12.5, Foreground = Code, VerticalAlignment = VerticalAlignment.Center,
                                       TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = v.S.Dir };
            if (v.S.IsRun)
            {
                // Commande suivie par bip run : une etiquette la distingue des sessions Claude.
                var dock = new DockPanel { LastChildFill = true };
                var chip = new Border { Background = Btn2, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(0, 0, 8, 0),
                                        VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "commande", FontSize = 10.5, Foreground = Muted } };
                DockPanel.SetDock(chip, Dock.Left);
                dock.Children.Add(chip);
                dock.Children.Add(name);
                Grid.SetColumn(dock, 1);
                g.Children.Add(dock);
            }
            else if (recap != null)
            {
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(name);
                stack.Children.Add(new TextBlock { Text = recap, FontSize = 11.5, Foreground = OnGreen, Margin = new Thickness(0, 2, 0, 0),
                                                   TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(stack, 1);
                g.Children.Add(stack);
            }
            else
            {
                Grid.SetColumn(name, 1);
                g.Children.Add(name);
            }

            var info = new TextBlock { FontSize = 12, Foreground = v.State == St.Waiting ? WaitRed : Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) };
            Grid.SetColumn(info, 2);
            g.Children.Add(info);
            _tickers.Add(() => info.Text = InfoOf(v));

            if (v.S.IsRun)
            {
                // Croix : retire la commande de l'encoche. Finie, son fichier est supprime ;
                // en cours, elle est seulement masquee (elle continue de tourner).
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var x = new TextBlock { Text = "✕", FontSize = 12, Foreground = Muted, Cursor = Cursors.Hand, ToolTip = "Retirer de l'encoche",
                                        VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 4, 6, 4), Background = Brushes.Transparent };
                x.MouseEnter += (s, e) => x.Foreground = Code;
                x.MouseLeave += (s, e) => x.Foreground = Muted;
                x.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    if (v.State == St.Busy) _dismissed.Add(v.S.Id);
                    else try { File.Delete(System.IO.Path.Combine(Program.StateDir, v.S.Id + ".state")); } catch { }
                    Refresh();
                };
                Grid.SetColumn(x, 3);
                g.Children.Add(x);
            }

            // Pas de clic pour ouvrir Claude ici : quand des sessions tournent, un clic
            // a cote d'un bouton de carte ne doit rien declencher. (Encoche vide seulement.)
            var border = new Border { Child = g, CornerRadius = new CornerRadius(8) };
            var wrap = new StackPanel();
            if (separator) wrap.Children.Add(new Rectangle { Height = 1, Fill = Sep });
            wrap.Children.Add(border);
            AddAgentLines(wrap, v.S);
            return wrap;
        }

        // ------------------------------------------------------------ sous-agents

        const int MAX_AGENT_LINES = 5;

        // En cours, ou termines depuis moins d'une minute. Garde-fou : un sous-agent sans fin
        // connue (interrompu sans signal) disparait au bout de 2 h, ou avec sa session.
        static List<SubAgent> VisibleAgents(Session s)
        {
            var now = DateTime.Now;
            return s.SubAgents.Where(a => a.End == null ? now - a.Start < TimeSpan.FromHours(2)
                                                        : now - a.End.Value < TimeSpan.FromMinutes(1)).ToList();
        }

        // Sous la ligne de la session, en retrait : "↳ description" et sa duree ; vert une minute une fois fini.
        void AddAgentLines(Panel wrap, Session s)
        {
            var list = VisibleAgents(s);
            foreach (var a in list.Take(MAX_AGENT_LINES))
            {
                var g = new Grid { Height = 24, Margin = new Thickness(34, 0, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bool done = a.End != null;
                Brush fg = done ? (a.Failed ? Muted : OnGreen) : Code;
                g.Children.Add(new TextBlock { Text = "↳ " + Safe(a.Description), FontSize = 12, Foreground = fg, VerticalAlignment = VerticalAlignment.Center,
                                               TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = a.Description });
                var right = new TextBlock { FontSize = 11.5, Foreground = done && !a.Failed ? OnGreen : Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) };
                Grid.SetColumn(right, 1);
                g.Children.Add(right);
                var agent = a;
                _tickers.Add(() => right.Text = agent.End == null ? Fmt(DateTime.Now - agent.Start) : agent.Failed ? "interrompu" : "✓ terminé");
                wrap.Children.Add(g);
            }
            if (list.Count > MAX_AGENT_LINES)
                wrap.Children.Add(new TextBlock { Text = "+" + (list.Count - MAX_AGENT_LINES) + (list.Count - MAX_AGENT_LINES > 1 ? " autres" : " autre"),
                                                  FontSize = 11.5, Foreground = Muted, Margin = new Thickness(34, 2, 0, 6) });
        }

        // "4 fichiers modifiés · 6 commandes · 2 sous-agents · 1 échec · 1 refus", d'apres le
        // journal d'audit depuis le debut du tour ; null si rien a dire ou debut inconnu.
        string TurnRecap(Session s)
        {
            DateTime from;
            if (!_turnStart.TryGetValue(s.Id, out from)) return null;
            List<AuditRow> rows;
            try { rows = Audit.Rows(Program.Root, 1).Where(r => r.Sess == s.Id && !r.Rule && r.T >= from.AddSeconds(-2)).ToList(); }
            catch { return null; }
            int files = rows.Where(r => (r.Tool == "Edit" || r.Tool == "Write" || r.Tool == "MultiEdit" || r.Tool == "NotebookEdit") && !r.Refused)
                            .Select(r => r.Obj).Distinct().Count();
            int cmds = rows.Count(r => r.Tool == "Bash" || r.Tool == "PowerShell");
            int agents = rows.Count(r => r.Tool == "Agent" || r.Tool == "Task");
            int fails = rows.Count(r => r.Result == "echec");
            int refused = rows.Count(r => r.Refused);
            var parts = new List<string>();
            if (files > 0) parts.Add(files + (files > 1 ? " fichiers modifiés" : " fichier modifié"));
            if (cmds > 0) parts.Add(cmds + (cmds > 1 ? " commandes" : " commande"));
            if (agents > 0) parts.Add(agents + (agents > 1 ? " sous-agents" : " sous-agent"));
            if (fails > 0) parts.Add(fails + (fails > 1 ? " échecs" : " échec"));
            if (refused > 0) parts.Add(refused + " refus");
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }

        static string InfoOf(View v)
        {
            if (v.S.IsRun)
                switch (v.State)
                {
                    case St.Busy:  return "en cours · " + Fmt(DateTime.Now - v.S.Order);
                    case St.Ready: return "a fini · " + RunDur(v.S.RunTime);
                    case St.Error: return v.S.Code == "interrompu" ? "interrompue" : "a échoué · code " + v.S.Code;
                }
            string d = Fmt(DateTime.Now - v.Since);
            switch (v.State)
            {
                case St.Waiting: return "en attente · " + d;
                case St.Busy:    return "en cours · " + d;
                case St.Error:   return "erreur · " + d;
                case St.Idle:    return "au repos";
                default:         return "prêt";
            }
        }

        static string Fmt(TimeSpan t)
        {
            if (t.TotalSeconds < 0) t = TimeSpan.Zero;
            if (t.TotalSeconds < 60) return (int)t.TotalSeconds + " s";
            if (t.TotalMinutes < 60) return (int)t.TotalMinutes + " min";
            int h = (int)t.TotalHours, m = t.Minutes;
            return m == 0 ? h + " h" : h + " h " + m.ToString("00");
        }

        // ------------------------------------------------------------ cartes de demande

        // Fond #161616, rayon 14, marge 14. En-tete : Bip 40 px, nom du projet en
        // Cascadia Mono 13, "attend depuis..." (card.Tag, mis a jour chaque seconde).
        FrameworkElement BuildCard(View v, Pending p)
        {
            var created = DateTime.Now;
            // Un bouton qui apparait sous le curseur ne doit pas recevoir un clic destine a autre chose.
            Func<bool> armed = () => DateTime.Now - created > ARM_DELAY && DateTime.Now - _expandedAt > ARM_DELAY
                                  && DateTime.Now - _layoutAt > ARM_DELAY;

            var body = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            head.Children.Add(new Bip { Width = 40, Height = 40, State = BipState.Waiting, WaitingSince = p.Since, VerticalAlignment = VerticalAlignment.Center });
            var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            who.Children.Add(new TextBlock { Text = Safe(v.S.Display), FontFamily = Mono, FontSize = 13, Foreground = Code, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = v.S.Dir });
            var since = new TextBlock { FontSize = 12, Foreground = Muted, Margin = new Thickness(0, 2, 0, 0) };
            who.Children.Add(since);
            head.Children.Add(who);
            body.Children.Add(head);

            bool ok = p.Kind == "ask" ? BuildAsk(body, p, armed) : BuildPermission(body, p, armed);
            if (!ok) ThreadPool.QueueUserWorkItem(_ => p.Reply(""));   // illisible : rendue a Claude

            return new Border
            {
                Background = Card, CornerRadius = new CornerRadius(14), Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10), Child = body, Tag = since
            };
        }

        void Answer(Pending p, string json)
        {
            if (!string.IsNullOrEmpty(json)) Journal.Add("reponse", p.SessionId, Session.NameOf(p.Cwd));
            _pending.Remove(p);
            _cards.Remove(p);
            ThreadPool.QueueUserWorkItem(_ => p.Reply(json));
            _panelSig = "";
            Refresh();
        }

        // Rendre la main a Claude : il affichera sa propre demande. On l'amene devant,
        // puisque c'est l'utilisateur qui le demande.
        FrameworkElement HandBack(Pending p, Func<bool> armed)
        {
            var t = new TextBlock { Text = "Répondre dans Claude", Foreground = Muted, FontSize = 12, Cursor = Cursors.Hand,
                                    TextDecorations = TextDecorations.Underline, VerticalAlignment = VerticalAlignment.Center,
                                    Margin = new Thickness(6, 0, 0, 0) };
            t.MouseLeftButtonUp += (s, e) => { if (!armed()) return; Answer(p, ""); Native.FocusClaude(); };
            return t;
        }

        // -------- permission

        static readonly HashSet<string> Shells = new HashSet<string> { "Bash", "PowerShell" };

        bool BuildPermission(StackPanel body, Pending p, Func<bool> armed)
        {
            string tool = p.ToolName;
            var input = p.ToolInput;

            body.Children.Add(new TextBlock { Text = TitleOf(tool), Foreground = Ink, FontSize = 14, FontWeight = FontWeights.SemiBold,
                                              LineHeight = 20, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

            var desc = Json.Str(input, "description");
            if (!string.IsNullOrEmpty(desc) && Shells.Contains(tool))
                body.Children.Add(new TextBlock { Text = "Selon Claude : " + Safe(desc), Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

            var code = new TextBlock { FontFamily = Mono, FontSize = 12, LineHeight = 18, Foreground = Code, TextWrapping = TextWrapping.Wrap };
            FillDetails(code, tool, input);
            int nl = code.Inlines.OfType<LineBreak>().Count() + 1;
            if (nl > 12)
                body.Children.Add(new TextBlock { Text = nl + " lignes : fais défiler pour tout lire avant d'autoriser.", Foreground = WaitRed, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            body.Children.Add(new Border
            {
                Background = Black, BorderBrush = Btn2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 12),
                Child = new ScrollViewer { Content = code, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            });

            // Ligne principale toujours au meme endroit : Autoriser, Refuser, Repondre dans Claude.
            var buttons = new WrapPanel();
            buttons.Children.Add(Btn("Autoriser", Ink, Black, null, 32, () => Answer(p, Replies.Allow()), armed));
            buttons.Children.Add(Btn("Refuser", Btn2, Ink, null, 32, () => Answer(p, Replies.Deny()), armed));
            buttons.Children.Add(HandBack(p, armed));
            body.Children.Add(buttons);

            // En dessous, un bouton "Toujours" par suggestion de Claude, avec sa portee en clair.
            if (!Shells.Contains(tool))
                foreach (var o in (Json.Arr(Json.Get(p.Payload, "permission_suggestions")) ?? new object[0]).Take(3))
                {
                    var sd = Json.Obj(o);
                    var label = sd == null ? null : Replies.AlwaysLabel(sd);
                    if (label == null) continue;
                    var b = Btn(Safe(label) + " (session)", Brushes.Transparent, Ink, Always, 28, () => Answer(p, Replies.AllowAlways(sd)), armed);
                    b.HorizontalAlignment = HorizontalAlignment.Left;
                    b.Margin = new Thickness(0, 8, 0, 0);
                    b.ToolTip = "Ne plus demander pour cette portée jusqu'à la fin de la session";
                    body.Children.Add(b);
                }

            // Commande deja autorisee 5 fois dans ce projet : proposer d'en faire une regle.
            if (Shells.Contains(tool))
            {
                Suggestion sug = null;
                try { sug = Rules.For(tool, Json.Str(input, "command"), p.Proj); }
                catch (Exception ex) { Program.Log("regles : " + ex.Message); }
                if (sug != null) body.Children.Add(SuggestionBox(sug, armed));
            }
            return true;
        }

        FrameworkElement SuggestionBox(Suggestion s, Func<bool> armed)
        {
            var box = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            box.Children.Add(new Rectangle { Height = 1, Fill = Sep, Margin = new Thickness(0, 0, 0, 10) });
            var text = new TextBlock { FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            text.Inlines.Add(new Run("Tu as autorisé "));
            text.Inlines.Add(new Run(Safe(s.Key)) { FontFamily = Mono, Foreground = Code });
            text.Inlines.Add(new Run(" " + s.Count + " fois dans " + Session.NameOf(s.Dir) + ". En faire une règle ? Elle autorisera "));
            text.Inlines.Add(new Run(Safe(s.Rule)) { FontFamily = Mono, Foreground = Code });
            text.Inlines.Add(new Run(", c'est-à-dire toute commande qui commence par « " + Safe(s.Key) + " »."));
            box.Children.Add(text);
            var buttons = new WrapPanel();
            Action<string> done = msg =>
            {
                buttons.Children.Clear();
                text.Inlines.Clear();
                text.Inlines.Add(new Run(msg));
                if (_form == Form.Expanded) Resize(EXP_W, ExpandedHeight(), 220);
            };
            buttons.Children.Add(Btn("Ce projet", Btn2, Ink, null, 28, () =>
            {
                try { Rules.Apply(s, false); done("Règle ajoutée pour ce projet : " + s.Rule); }
                catch (Exception ex) { done("Règle non ajoutée : " + ex.Message); }
            }, armed));
            buttons.Children.Add(Btn("Partout", Brushes.Transparent, Ink, Always, 28, () =>
            {
                try { Rules.Apply(s, true); done("Règle ajoutée partout : " + s.Rule); }
                catch (Exception ex) { done("Règle non ajoutée : " + ex.Message); }
            }, armed));
            buttons.Children.Add(Btn("Non, ne plus proposer", Brushes.Transparent, Muted, null, 28, () =>
            {
                Rules.Ignore(s);
                done("D'accord, je ne le proposerai plus dans ce projet.");
            }, armed));
            box.Children.Add(buttons);
            return box;
        }

        static string TitleOf(string tool)
        {
            switch (tool)
            {
                case "Bash":         return "Exécuter cette commande ?";
                case "PowerShell":   return "Exécuter cette commande PowerShell ?";
                case "Edit":
                case "MultiEdit":    return "Modifier ce fichier ?";
                case "Write":        return "Écrire ce fichier ?";
                case "NotebookEdit": return "Modifier ce notebook ?";
                case "Read":         return "Lire ce fichier ?";
                case "WebFetch":     return "Ouvrir cette page web ?";
                case "WebSearch":    return "Lancer cette recherche web ?";
            }
            if (tool.StartsWith("mcp__"))
            {
                var parts = tool.Split(new[] { "__" }, StringSplitOptions.None);
                if (parts.Length >= 3) return "Utiliser « " + parts[2] + " » (" + parts[1] + ") ?";
            }
            return "Utiliser l'outil « " + tool + " » ?";
        }

        void FillDetails(TextBlock tb, string tool, Dictionary<string, object> input)
        {
            Action<string, Brush> line = (s, b) =>
            {
                if (tb.Inlines.Count > 0) tb.Inlines.Add(new LineBreak());
                tb.Inlines.Add(new Run(Safe(s)) { Foreground = b ?? Code });
            };

            string path = Json.Str(input, "file_path") ?? Json.Str(input, "notebook_path") ?? Json.Str(input, "path");
            switch (tool)
            {
                case "Bash":
                case "PowerShell":
                    line(Json.Str(input, "command") ?? "", null);
                    return;
                case "Edit":
                    line(path ?? "", Muted);
                    Diff(line, Json.Str(input, "old_string"), Json.Str(input, "new_string"));
                    return;
                case "MultiEdit":
                    line(path ?? "", Muted);
                    var edits = Json.Arr(Json.Get(input, "edits")) ?? new object[0];
                    foreach (var e in edits.Take(3))
                        Diff(line, Json.Str(Json.Obj(e), "old_string"), Json.Str(Json.Obj(e), "new_string"));
                    if (edits.Length > 3) line("… " + (edits.Length - 3) + " autres modifications", Muted);
                    return;
                case "Write":
                    line(path ?? "", Muted);
                    Excerpt(line, Json.Str(input, "content"), "", null, 12);
                    return;
                case "WebFetch":
                    line(Json.Str(input, "url") ?? "", null);
                    return;
                case "WebSearch":
                    line(Json.Str(input, "query") ?? "", null);
                    return;
            }
            foreach (var kv in input)
            {
                var val = kv.Value as string ?? Json.Write(kv.Value);
                if (val.Length > 400) val = val.Substring(0, 400) + " …";
                line(kv.Key + " : " + val, null);
            }
        }

        static void Diff(Action<string, Brush> line, string oldS, string newS)
        {
            Excerpt(line, oldS, "- ", DiffDel, 8);
            Excerpt(line, newS, "+ ", DiffAdd, 8);
        }

        static void Excerpt(Action<string, Brush> line, string text, string prefix, Brush b, int max)
        {
            if (string.IsNullOrEmpty(text)) return;
            var lines = text.Replace("\r", "").Split('\n');
            foreach (var l in lines.Take(max)) line(prefix + l, b);
            if (lines.Length > max) line(prefix + "… (" + (lines.Length - max) + " lignes de plus)", Muted);
        }

        // -------- AskUserQuestion

        bool BuildAsk(StackPanel body, Pending p, Func<bool> armed)
        {
            if (p.ToolName != "AskUserQuestion") return false;
            var qs = Json.Arr(Json.Get(p.ToolInput, "questions"));
            if (qs == null || qs.Length == 0) return false;

            var questions = qs.Select(Json.Obj).ToList();
            if (questions.Any(q => q == null || string.IsNullOrEmpty(Json.Str(q, "question")))) return false;

            int count = questions.Count;
            var picked = Enumerable.Range(0, count).Select(_ => new List<string>()).ToList();   // libelles choisis
            var otherOn = new bool[count];
            var otherText = new TextBox[count];
            Border send = null;

            Func<bool> complete = () => Enumerable.Range(0, count).All(i =>
                picked[i].Count > 0 || (otherOn[i] && !string.IsNullOrWhiteSpace(otherText[i].Text)));
            Action refreshSend = () => send.Opacity = complete() ? 1 : 0.4;

            for (int qi = 0; qi < count; qi++)
            {
                int i = qi;
                var q = questions[i];
                bool multi = Json.Get(q, "multiSelect") is bool && (bool)Json.Get(q, "multiSelect");

                var header = Json.Str(q, "header");
                if (!string.IsNullOrEmpty(header))
                    body.Children.Add(new Border
                    {
                        Background = Btn2, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1),
                        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, i == 0 ? 0 : 12, 0, 6),
                        Child = new TextBlock { Text = Safe(header), Foreground = Muted, FontSize = 11 }
                    });
                body.Children.Add(new TextBlock
                {
                    Text = Safe(Json.Str(q, "question")) + (multi ? "  (plusieurs choix possibles)" : ""),
                    Foreground = Ink, FontSize = 14, FontWeight = FontWeights.SemiBold, LineHeight = 20,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
                });

                var options = new List<Border>();
                Border other = null;
                Action paint = () =>
                {
                    foreach (var ob in options) Select(ob, picked[i].Contains((string)ob.Tag));
                    Select(other, otherOn[i]);
                    otherText[i].Visibility = otherOn[i] ? Visibility.Visible : Visibility.Collapsed;
                    refreshSend();
                };

                foreach (var o in Json.Arr(Json.Get(q, "options")) ?? new object[0])
                {
                    var od = Json.Obj(o);
                    var label = Json.Str(od, "label");
                    if (string.IsNullOrEmpty(label)) continue;
                    var ob = Option(Safe(label), Safe(Json.Str(od, "description")));
                    ob.Tag = label;
                    ob.MouseLeftButtonUp += (s, e) =>
                    {
                        if (!armed()) return;
                        if (multi) { if (!picked[i].Remove(label)) picked[i].Add(label); }
                        else { picked[i].Clear(); picked[i].Add(label); otherOn[i] = false; }
                        paint();
                    };
                    options.Add(ob);
                    body.Children.Add(ob);
                }

                other = Option("Autre…", "Répondre avec ton propre texte");
                other.MouseLeftButtonUp += (s, e) =>
                {
                    if (!armed()) return;
                    otherOn[i] = !otherOn[i] || !multi;
                    if (!multi) picked[i].Clear();
                    paint();
                    if (otherOn[i]) FocusBox(otherText[i]);
                };
                body.Children.Add(other);

                var box = new TextBox
                {
                    FontSize = 12.5, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 2, 0, 4),
                    Background = Black, Foreground = Ink, CaretBrush = Ink, BorderBrush = Always,
                    TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, Visibility = Visibility.Collapsed
                };
                box.PreviewMouseLeftButtonDown += (s, e) => FocusBox(box);
                box.TextChanged += (s, e) => refreshSend();
                otherText[i] = box;
                body.Children.Add(box);
            }

            var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            send = (Border)Btn("Envoyer", Ink, Black, null, 32, () =>
            {
                if (!complete()) return;
                var answers = new Dictionary<string, string>();
                for (int i = 0; i < count; i++)
                {
                    var parts = new List<string>(picked[i]);
                    if (otherOn[i] && !string.IsNullOrWhiteSpace(otherText[i].Text)) parts.Add(otherText[i].Text.Trim());
                    answers[Json.Str(questions[i], "question")] = string.Join(", ", parts);
                }
                Answer(p, Replies.Answers(p, answers));
                if (_activatable) SetActivatable(false);
            }, armed);
            send.Opacity = 0.4;
            buttons.Children.Add(send);
            buttons.Children.Add(HandBack(p, armed));
            body.Children.Add(buttons);
            return true;
        }

        // Option de question : contour #333, rayon 8 ; choisie, elle passe en clair.
        Border Option(string label, string description)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(description))
                sp.Children.Add(new TextBlock { Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
            var b = new Border
            {
                BorderBrush = Always, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 6), Child = sp, Cursor = Cursors.Hand
            };
            Select(b, false);
            return b;
        }

        static void Select(Border b, bool on)
        {
            b.Background = on ? Ink : Brushes.Transparent;
            var sp = (StackPanel)b.Child;
            ((TextBlock)sp.Children[0]).Foreground = on ? Black : Ink;
            if (sp.Children.Count > 1) ((TextBlock)sp.Children[1]).Foreground = on ? B(0x444444) : Muted;
        }

        // Seul moment ou l'encoche prend le clavier.
        void FocusBox(TextBox box)
        {
            if (!_activatable) SetActivatable(true);
            Activate();
            Dispatcher.BeginInvoke((Action)(() => { box.Focus(); Keyboard.Focus(box); }), DispatcherPriority.Input);
        }

        // Bouton : hauteur 32 (ou 28), rayon 8. Le clic compte seulement s'il commence et
        // finit sur le bouton : jamais au survol.
        FrameworkElement Btn(string text, Brush bg, Brush fg, Brush outline, double height, Action click, Func<bool> armed)
        {
            var b = new Border
            {
                Background = bg, CornerRadius = new CornerRadius(8), Height = height, Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
                BorderBrush = outline ?? Brushes.Transparent, BorderThickness = new Thickness(outline != null ? 1 : 0),
                Child = new TextBlock
                {
                    Text = text, Foreground = fg, FontSize = height < 30 ? 12 : 13,
                    FontWeight = height < 30 ? FontWeights.Normal : FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
                }
            };
            bool down = false;
            b.MouseLeftButtonDown += (s, e) => { down = true; e.Handled = true; };
            b.MouseEnter += (s, e) => b.Child.Opacity = 0.75;
            b.MouseLeave += (s, e) => { down = false; b.Child.Opacity = 1; };
            b.MouseLeftButtonUp += (s, e) =>
            {
                if (!down) return;
                down = false;
                e.Handled = true;
                if (armed()) click();
            };
            return b;
        }

        // Rend visibles les caracteres invisibles ou de controle (U+202E et consorts) :
        // le texte affiche doit etre celui qui sera execute.
        static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                var cat = char.GetUnicodeCategory(c);
                if ((cat == UnicodeCategory.Format || cat == UnicodeCategory.Control) && c != '\n' && c != '\t')
                    sb.Append("[U+").Append(((int)c).ToString("X4")).Append(']');
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

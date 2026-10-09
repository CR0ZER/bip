// L'encoche : une pastille posee sur la barre des taches (ou en haut de l'ecran)
// avec un nuage par session, qui se deplie au survol pour donner le detail et
// repondre aux demandes en attente.
//
// La fenetre ne prend jamais le focus (WS_EX_NOACTIVATE), sauf le temps de taper
// une reponse libre a une question.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ClaudeNotch
{
    // Une ligne de l'encoche : la session vue par l'utilisateur, demandes comprises.
    class View
    {
        public Session S;
        public St State;          // etat effectif (une demande en attente force "en attente")
        public DateTime Since;
        public List<Pending> Pending = new List<Pending>();
    }

    class NotchWindow : Window
    {
        const int MAX_CLOUDS = 5;
        const double PANEL_W = 430;
        const double PANEL_SCALE = 0.86;   // tout le panneau d'un bloc : textes, boutons, cartes
        static readonly TimeSpan FLASH_AFTER = TimeSpan.FromMinutes(5);
        static readonly TimeSpan FLASH_EVERY = TimeSpan.FromSeconds(30);
        static readonly TimeSpan ARM_DELAY   = TimeSpan.FromMilliseconds(600);

        readonly Config _cfg = new Config(System.IO.Path.Combine(Program.Root, "config.txt"));
        readonly Dictionary<string, ImageSource> _img = new Dictionary<string, ImageSource>();

        List<Session> _sessions = new List<Session>();
        readonly List<Pending> _pending = new List<Pending>();
        List<View> _views = new List<View>();

        readonly StackPanel _stack = new StackPanel();
        readonly Border _pill = new Border();
        readonly StackPanel _clouds = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _panel = new Border();
        readonly StackPanel _content = new StackPanel();
        readonly ScrollViewer _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        readonly ScaleTransform _zoom = new ScaleTransform(1, 1);
        int _anim;   // jeton : une animation plus recente annule la fin d'une ancienne

        readonly Dictionary<Pending, FrameworkElement> _cards = new Dictionary<Pending, FrameworkElement>();
        readonly List<Tuple<UIElement, Func<DateTime>>> _pulse = new List<Tuple<UIElement, Func<DateTime>>>();
        readonly List<Action> _tickers = new List<Action>();

        IntPtr _hwnd;
        double _scale = 1;
        bool _expanded, _menuOpen, _activatable, _hiddenFullScreen;
        DateTime _expandedAt = DateTime.MinValue, _lastFlash = DateTime.MinValue, _layoutAt = DateTime.MinValue;
        string _pillSig = "", _panelSig = "";
        string _mode = "";
        DispatcherTimer _collapse;
        Native.WinEventProc _fgProc;   // garde une reference : le GC ne doit pas la collecter

        // ------------------------------------------------------------ couleurs

        static SolidColorBrush B(uint argb)
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            b.Freeze();
            return b;
        }

        static readonly Brush BgPill   = B(0xF0161618), BgPanel = B(0xF51C1C1F), Line = B(0x30FFFFFF);
        static readonly Brush Ink      = B(0xFFF2F2F4), Muted = B(0xFF9C9CA6), Faint = B(0xFF6E6E78);
        static readonly Brush CodeBg   = B(0xFF111113), CardBg = B(0xFF2A1D1E), CardLine = B(0x88EF0E0E);
        static readonly Brush Red      = B(0xFFEF0E0E);
        static readonly Brush BtnAllow = B(0xFF1F7A35), BtnDeny = B(0xFF7A1F24), BtnNeutral = B(0xFF34343A), BtnSel = B(0xFF3B4C8C);
        static readonly Brush DiffDel  = B(0xFFFF8A8A), DiffAdd = B(0xFF8AE39A), RowHover = B(0x14FFFFFF);
        static readonly FontFamily Ui = new FontFamily("Segoe UI Variable Text, Segoe UI");
        static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");

        ImageSource Clawd(St s)
        {
            return _img[s == St.Busy ? "orange" : (s == St.Waiting || s == St.Error) ? "red" : "green"];
        }

        // ------------------------------------------------------------ construction

        public NotchWindow()
        {
            foreach (var n in new[] { "green", "orange", "red" }) _img[n] = LoadPng("clawd_" + n + ".png");

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = Ui;
            Title = "ClaudeNotch";
            Left = -2000; Top = -2000;   // hors ecran tant que la position n'est pas calculee

            _pill.Background = BgPill;
            _pill.BorderBrush = Line;
            _pill.BorderThickness = new Thickness(1);
            _pill.Padding = new Thickness(6, 0, 6, 0);
            _pill.Child = _clouds;
            _pill.Cursor = Cursors.Hand;
            _pill.MouseLeftButtonUp += (s, e) => Native.FocusClaude();
            _pill.ContextMenu = BuildMenu();

            _scroll.Content = _content;
            _panel.Background = BgPanel;
            _panel.BorderBrush = Line;
            _panel.BorderThickness = new Thickness(1);
            _panel.CornerRadius = new CornerRadius(14);
            _panel.Padding = new Thickness(12, 10, 12, 10);
            _panel.Width = PANEL_W;
            _panel.Child = _scroll;
            _panel.Visibility = Visibility.Collapsed;
            _panel.LayoutTransform = new ScaleTransform(PANEL_SCALE, PANEL_SCALE);
            _panel.RenderTransform = _zoom;
            _panel.ContextMenu = BuildMenu();

            Content = _stack;

            // Barres de defilement fines et discretes : le style WPF par defaut detonne,
            // mais une commande ou une liste coupee doit rester visible comme telle.
            var bars = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar));
            bars.Setters.Add(new Setter(FrameworkElement.WidthProperty, 8.0));
            bars.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 8.0));
            bars.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = bars;

            MouseEnter += (s, e) => { _collapse.Stop(); Expand(); };
            MouseLeave += (s, e) => _collapse.Start();
            Deactivated += (s, e) => { if (!IsMouseOver) _collapse.Start(); };
            SizeChanged += (s, e) => Reposition();
            SourceInitialized += OnSourceInitialized;

            _collapse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _collapse.Tick += (s, e) =>
            {
                _collapse.Stop();
                bool typing = IsActive && Keyboard.FocusedElement is TextBox;
                if (!IsMouseOver && !_menuOpen && !typing) Collapse();
            };

            Directory.CreateDirectory(Program.StateDir);
            Sessions.Purge(Program.StateDir);

            new PipeServer(
                p => Dispatcher.BeginInvoke((Action)(() => { _pending.Add(p); Refresh(); })),
                p => Dispatcher.BeginInvoke((Action)(() => { _pending.Remove(p); Refresh(); })),
                Program.Log).Start();

            Every(300, Refresh);
            Every(60, Animate);
            Every(1000, () => { foreach (var t in _tickers) t(); KeepOnTop(); CheckFullScreen(); });
            Every(2000, Reposition);

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => Dispatcher.BeginInvoke((Action)Reposition);

            ApplyMode();
            Refresh();
        }

        void Every(int ms, Action a)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { try { a(); } catch (Exception ex) { Program.Log(ex.ToString()); } };
            t.Start();
        }

        static ImageSource LoadPng(string name)
        {
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (st == null) throw new InvalidOperationException("ressource manquante : " + name);
                var ms = new MemoryStream();
                st.CopyTo(ms);
                ms.Position = 0;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = ms;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = 96;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var src = PresentationSource.FromVisual(this);
            if (src != null && src.CompositionTarget != null) _scale = src.CompositionTarget.TransformToDevice.M11;
            SetActivatable(false);

            // La barre des taches repasse au premier plan quand on clique dessus :
            // on se replace au-dessus a chaque changement de fenetre active.
            _fgProc = (h, evt, hwnd, o, c, t, time) => KeepOnTop();
            Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                                   IntPtr.Zero, _fgProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            Reposition();
        }

        // WS_EX_NOACTIVATE en temps normal ; levee le temps de taper une reponse.
        void SetActivatable(bool on)
        {
            if (_hwnd == IntPtr.Zero) return;
            _activatable = on;
            int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW;
            ex = on ? ex & ~Native.WS_EX_NOACTIVATE : ex | Native.WS_EX_NOACTIVATE;
            Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex);
        }

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

        // ------------------------------------------------------------ position

        string EffectiveMode()
        {
            switch (_cfg.Position)
            {
                case "gauche": case "centre": case "haut": return _cfg.Position;
                default: return Native.TaskbarAppsCentered() ? "gauche" : "centre";
            }
        }

        // Ordre des blocs et forme de la pastille selon l'ancrage.
        void ApplyMode()
        {
            string m = EffectiveMode();
            if (m == _mode) return;
            _mode = m;

            _stack.Children.Clear();
            var h = m == "gauche" ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            _pill.HorizontalAlignment = h;
            _panel.HorizontalAlignment = h;
            _panel.RenderTransformOrigin = new Point(m == "gauche" ? 0 : 0.5, m == "haut" ? 0 : 1);

            if (m == "haut")
            {
                _pill.Height = 26;
                _pill.CornerRadius = new CornerRadius(0, 0, 12, 12);
                _pill.BorderThickness = new Thickness(1, 0, 1, 1);
                _panel.Margin = new Thickness(0, 8, 0, 0);
                _stack.Children.Add(_pill);
                _stack.Children.Add(_panel);
            }
            else
            {
                _pill.Height = 30;
                _pill.CornerRadius = new CornerRadius(15);
                _pill.BorderThickness = new Thickness(1);
                _panel.Margin = new Thickness(0, 0, 0, 10);
                _stack.Children.Add(_panel);
                _stack.Children.Add(_pill);
            }
            _pillSig = "";
            Reposition();
        }

        void Reposition()
        {
            ApplyMode();
            if (_hwnd == IntPtr.Zero || ActualWidth <= 0) return;

            double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight;
            double w = ActualWidth, h = ActualHeight, left, top;

            if (_mode == "haut")
            {
                left = (sw - w) / 2;
                top = 0;
            }
            else
            {
                // Barre des taches en bas (Windows 11). A defaut, le bas de l'ecran.
                Native.RECT r;
                double tbL = 0, tbR = sw, tbT = sh - 48, tbB = sh;
                if (Native.TaskbarRect(out r) && r.Bottom / _scale >= sh - 1)
                {
                    tbL = r.Left / _scale; tbR = r.Right / _scale; tbT = r.Top / _scale; tbB = r.Bottom / _scale;
                }
                double pillBottom = (tbT + tbB) / 2 + _pill.ActualHeight / 2;
                top = pillBottom - h;
                left = _mode == "gauche" ? tbL + 10 : (tbL + tbR) / 2 - w / 2;
            }

            if (Math.Abs(Left - left) > 0.5) Left = left;
            if (Math.Abs(Top - top) > 0.5) Top = top;
            KeepOnTop();
        }

        // ------------------------------------------------------------ menu

        ContextMenu BuildMenu()
        {
            var m = new ContextMenu();
            var pos = new MenuItem { Header = "Position" };
            foreach (var p in new[] { Tuple.Create("auto", "Automatique (selon la barre des tâches)"),
                                      Tuple.Create("gauche", "En bas à gauche"),
                                      Tuple.Create("centre", "Au centre de la barre des tâches"),
                                      Tuple.Create("haut", "En haut au centre de l'écran") })
            {
                var key = p.Item1;
                var it = new MenuItem { Header = p.Item2, IsCheckable = true };
                it.Click += (s, e) => { _cfg.Position = key; _cfg.Save(); ApplyMode(); Reposition(); };
                pos.Items.Add(it);
            }
            m.Items.Add(pos);

            var flash = new MenuItem { Header = "Faire clignoter la fenêtre Claude (attente > 5 min)", IsCheckable = true };
            flash.Click += (s, e) => { _cfg.Flash = flash.IsChecked; _cfg.Save(); };
            m.Items.Add(flash);

            m.Items.Add(new Separator());
            var open = new MenuItem { Header = "Ouvrir le dossier d'état" };
            open.Click += (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", Program.StateDir); } catch { } };
            m.Items.Add(open);
            var purge = new MenuItem { Header = "Purger les états" };
            purge.Click += (s, e) =>
            {
                try { foreach (var f in Directory.GetFiles(Program.StateDir, "*.state")) File.Delete(f); } catch { }
                Refresh();
            };
            m.Items.Add(purge);

            m.Items.Add(new Separator());
            var quit = new MenuItem { Header = "Quitter ClaudeNotch" };
            quit.Click += (s, e) =>
            {
                // Les demandes en attente repartent vers Claude.
                foreach (var p in _pending.ToList()) ThreadPool.QueueUserWorkItem(_ => p.Reply(""));
                Application.Current.Shutdown();
            };
            m.Items.Add(quit);

            m.Opened += (s, e) =>
            {
                _menuOpen = true;
                foreach (MenuItem it in pos.Items)
                    it.IsChecked = ((string)it.Header).StartsWith(Label(_cfg.Position));
                flash.IsChecked = _cfg.Flash;
            };
            m.Closed += (s, e) => { _menuOpen = false; if (!IsMouseOver) _collapse.Start(); };
            return m;
        }

        static string Label(string pos)
        {
            switch (pos)
            {
                case "gauche": return "En bas";
                case "centre": return "Au centre";
                case "haut":   return "En haut";
                default:       return "Automatique";
            }
        }

        // ------------------------------------------------------------ donnees

        void Refresh()
        {
            var loaded = Sessions.Load(Program.StateDir);
            if (loaded != null) _sessions = loaded;
            _pending.RemoveAll(p => p.Done);

            var views = new List<View>();
            foreach (var s in _sessions)
            {
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

            // Cartes des demandes disparues.
            foreach (var p in _cards.Keys.Where(p => !_pending.Contains(p)).ToList()) _cards.Remove(p);

            RenderPill();
            if (_expanded) RenderPanel();
        }

        // ------------------------------------------------------------ pastille

        void RenderPill()
        {
            var shown = _views.Take(MAX_CLOUDS).ToList();
            int extra = _views.Count - shown.Count;
            bool extraWaiting = _views.Skip(MAX_CLOUDS).Any(v => v.State == St.Waiting);
            string sig = _mode + "|" + string.Join(";", shown.Select(v => v.S.Id + ":" + v.State)) + "|" + extra + extraWaiting;
            if (sig == _pillSig) return;
            _pillSig = sig;

            _pulse.RemoveAll(t => _clouds.Children.Contains(t.Item1));
            _clouds.Children.Clear();

            if (shown.Count == 0)
            {
                // Au repos : un nuage discret, pour garder le survol et le clic droit.
                var idle = Cloud(St.Stale, 19);
                idle.Opacity = 0.4;
                idle.ToolTip = "ClaudeNotch — aucune session active";
                _clouds.Children.Add(idle);
                return;
            }

            foreach (var v in shown)
            {
                var c = Cloud(v.State, 21);
                c.Margin = new Thickness(2, 0, 2, 0);
                c.ToolTip = v.S.Name + " — " + States.Label(v.State);
                _clouds.Children.Add(c);
                if (v.State == St.Waiting) { var id = v.S.Id; _pulse.Add(Tuple.Create((UIElement)c, (Func<DateTime>)(() => SinceOf(id)))); }
            }

            if (extra > 0)
            {
                var more = new TextBlock
                {
                    Text = "+" + extra, Foreground = extraWaiting ? Red : Muted, FontSize = 11,
                    FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 2, 0)
                };
                _clouds.Children.Add(more);
                if (extraWaiting)
                {
                    var firstId = _views.Skip(MAX_CLOUDS).First(v => v.State == St.Waiting).S.Id;
                    _pulse.Add(Tuple.Create((UIElement)more, (Func<DateTime>)(() => SinceOf(firstId))));
                }
            }
        }

        // Delai d'attente lu a jour : une nouvelle demande peut arriver sur une session deja en attente.
        DateTime SinceOf(string id)
        {
            var v = _views.FirstOrDefault(x => x.S.Id == id);
            return v != null && v.State == St.Waiting ? v.Since : DateTime.Now;
        }

        // Nuage clawd : la pastille d'etat fait partie de l'image (quart bas-droit,
        // de 55 % a 98 % du cote). Pour une erreur, un "!" blanc se pose dessus.
        FrameworkElement Cloud(St s, double size)
        {
            var g = new Grid { Width = size + 4, Height = size };
            g.Children.Add(new Image { Source = Clawd(s), Width = size, Height = size, Stretch = Stretch.Uniform });
            if (s == St.Error)
            {
                var spot = new Grid
                {
                    Width = size * 0.43, Height = size * 0.43,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(2 + size * 0.55, size * 0.55, 0, 0)
                };
                spot.Children.Add(new TextBlock { Text = "!", FontSize = size * 0.36, FontWeight = FontWeights.Black, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                g.Children.Add(spot);
            }
            return g;
        }

        // Respiration des nuages en attente, comme ClaudeVoyant : rouge fixe 15 s,
        // puis de plus en plus vite. Au-dela de 5 min, la fenetre Claude clignote.
        void Animate()
        {
            TimeSpan longest = TimeSpan.Zero;
            long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            foreach (var t in _pulse)
            {
                var waited = DateTime.Now - t.Item2();
                if (waited > longest) longest = waited;
                int period = waited < TimeSpan.FromSeconds(15) ? 0 : waited < TimeSpan.FromMinutes(2) ? 1500 : 900;
                t.Item1.Opacity = period == 0 ? 1 : 0.3 + 0.7 * (0.5 + 0.5 * Math.Cos(2 * Math.PI * (now % period) / period));
            }

            if (_cfg.Flash && longest >= FLASH_AFTER && DateTime.Now - _lastFlash > FLASH_EVERY)
            {
                _lastFlash = DateTime.Now;
                Native.FlashClaude();   // clignotement seulement : jamais de premier plan force
            }
        }

        // ------------------------------------------------------------ panneau deplie

        void Expand()
        {
            if (_expanded) return;
            _expanded = true;
            _expandedAt = DateTime.Now;
            _panelSig = "";
            RenderPanel();
            _scroll.ScrollToTop();   // les demandes sont en tete : toujours les montrer
            AnimatePanel(true);
        }

        void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;
            AnimatePanel(false);
            // Les nuages du panneau ne comptent plus pour l'escalade (clignotement de Claude).
            _pulse.RemoveAll(t => !_clouds.Children.Contains(t.Item1));
            if (_activatable) SetActivatable(false);
        }

        // Ouverture : fondu et zoom de 94 % a 100 % depuis la pastille (180 ms).
        // Fermeture : l'inverse en 130 ms, puis le panneau sort de la mise en page
        // (la fenetre se reduit alors a la pastille).
        void AnimatePanel(bool open)
        {
            int token = ++_anim;
            bool fromHidden = open && _panel.Visibility != Visibility.Visible;
            if (open) _panel.Visibility = Visibility.Visible;

            var dur = new Duration(TimeSpan.FromMilliseconds(open ? 180 : 130));
            var ease = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn };
            Func<double, double, DoubleAnimation> anim = (from, to) =>
            {
                var a = new DoubleAnimation { To = to, Duration = dur, EasingFunction = ease };
                if (fromHidden) a.From = from;   // sinon : repart de la valeur courante
                return a;
            };

            var fade = anim(0, open ? 1 : 0);
            if (!open) fade.Completed += (s, e) => { if (token == _anim) _panel.Visibility = Visibility.Collapsed; };
            _panel.BeginAnimation(UIElement.OpacityProperty, fade);
            _zoom.BeginAnimation(ScaleTransform.ScaleXProperty, anim(0.94, open ? 1 : 0.96));
            _zoom.BeginAnimation(ScaleTransform.ScaleYProperty, anim(0.94, open ? 1 : 0.96));
        }

        void RenderPanel()
        {
            string sig = string.Join(";", _views.Select(v => v.S.Id + ":" + v.State + ":" + v.S.Name + ":" + v.Pending.Count))
                       + "|" + string.Join(",", _pending.Select(p => p.GetHashCode()));
            if (sig == _panelSig) return;
            // Pendant une saisie, on ne reconstruit rien : le focus serait perdu.
            if (IsActive && Keyboard.FocusedElement is TextBox) return;
            _layoutAt = DateTime.Now;   // la liste bouge : les boutons se re-arment

            _pulse.RemoveAll(t => !_clouds.Children.Contains(t.Item1));
            _tickers.Clear();
            _content.Children.Clear();
            _scroll.MaxHeight = SystemParameters.WorkArea.Height * 0.85;

            int n = _views.Count;
            _content.Children.Add(new TextBlock
            {
                Text = n == 0 ? "Aucune session active" : n == 1 ? "1 session active" : n + " sessions actives",
                Foreground = Muted, FontSize = 11.5, Margin = new Thickness(2, 0, 0, 8)
            });

            // Les demandes d'abord, en grand ; puis les sessions qui attendent ; puis le reste.
            foreach (var v in _views.Where(v => v.Pending.Count > 0).OrderBy(v => v.Since))
                foreach (var p in v.Pending)
                {
                    FrameworkElement card;
                    if (!_cards.TryGetValue(p, out card)) { card = BuildCard(v, p); _cards[p] = card; }
                    if (card.Parent is Panel) ((Panel)card.Parent).Children.Remove(card);
                    _content.Children.Add(card);
                    var since = (TextBlock)card.Tag;
                    _tickers.Add(() => since.Text = "attend depuis " + Fmt(DateTime.Now - p.Since));
                }

            foreach (var v in _views.Where(v => v.Pending.Count == 0).OrderBy(v => v.State == St.Waiting ? 0 : 1).ThenBy(v => v.S.Order))
                _content.Children.Add(Row(v));

            foreach (var t in _tickers) t();
            _panelSig = sig;   // retenue seulement apres un rendu complet
        }

        FrameworkElement Row(View v)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var c = Cloud(v.State, 20);
            c.Margin = new Thickness(2, 3, 8, 3);
            g.Children.Add(c);
            if (v.State == St.Waiting) { var id = v.S.Id; _pulse.Add(Tuple.Create((UIElement)c, (Func<DateTime>)(() => SinceOf(id)))); }

            var name = new TextBlock { Text = v.S.Name, Foreground = Ink, FontSize = 13, FontWeight = FontWeights.SemiBold,
                                       VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                                       ToolTip = v.S.Dir };
            Grid.SetColumn(name, 1);
            g.Children.Add(name);

            var info = new TextBlock { Foreground = v.State == St.Waiting ? Red : Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
            Grid.SetColumn(info, 2);
            g.Children.Add(info);
            _tickers.Add(() => info.Text = InfoOf(v));

            var border = new Border { Child = g, CornerRadius = new CornerRadius(6), Padding = new Thickness(2, 0, 4, 0) };
            border.MouseEnter += (s, e) => border.Background = RowHover;
            border.MouseLeave += (s, e) => border.Background = Brushes.Transparent;
            border.MouseLeftButtonUp += (s, e) => Native.FocusClaude();
            border.ToolTip = "Ouvrir Claude";
            return border;
        }

        static string InfoOf(View v)
        {
            string d = Fmt(DateTime.Now - v.Since);
            switch (v.State)
            {
                case St.Waiting: return "en attente · " + d;
                case St.Busy:    return "en cours · " + d;
                case St.Error:   return "erreur · " + d;
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

        // ------------------------------------------------------------ cartes

        // En-tete commun ; card.Tag = le TextBlock de duree, mis a jour chaque seconde.
        FrameworkElement BuildCard(View v, Pending p)
        {
            var created = DateTime.Now;
            // Un bouton qui apparait sous le curseur ne doit pas recevoir un clic destine a autre chose.
            Func<bool> armed = () => DateTime.Now - created > ARM_DELAY && DateTime.Now - _expandedAt > ARM_DELAY
                                  && DateTime.Now - _layoutAt > ARM_DELAY;

            var body = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
            var since = new TextBlock { Foreground = Red, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(since, Dock.Right);
            var icon = new Image { Source = _img["red"], Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            head.Children.Add(icon);
            head.Children.Add(since);
            head.Children.Add(new TextBlock { Text = v.S.Name, Foreground = Ink, FontSize = 13, FontWeight = FontWeights.SemiBold,
                                              VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = v.S.Dir });
            body.Children.Add(head);

            bool ok = p.Kind == "ask" ? BuildAsk(body, p, armed) : BuildPermission(body, p, armed);
            if (!ok)
            {
                // Demande illisible : on la rend a Claude sans attendre.
                ThreadPool.QueueUserWorkItem(_ => p.Reply(""));
            }

            var card = new Border
            {
                Background = CardBg, BorderBrush = CardLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10), Child = body, Tag = since
            };
            return card;
        }

        void Answer(Pending p, string json)
        {
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
                                    Margin = new Thickness(6, 0, 0, 6) };
            t.MouseLeftButtonUp += (s, e) => { if (!armed()) return; Answer(p, ""); Native.FocusClaude(); };
            return t;
        }

        // -------- permission

        static readonly HashSet<string> Shells = new HashSet<string> { "Bash", "PowerShell" };

        bool BuildPermission(StackPanel body, Pending p, Func<bool> armed)
        {
            string tool = p.ToolName;
            var input = p.ToolInput;

            body.Children.Add(new TextBlock { Text = TitleOf(tool), Foreground = Ink, FontSize = 13.5, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });

            var desc = Json.Str(input, "description");
            if (!string.IsNullOrEmpty(desc) && Shells.Contains(tool))
                body.Children.Add(new TextBlock { Text = "Selon Claude : " + Safe(desc), Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });

            var code = new TextBlock { FontFamily = Mono, FontSize = 11.5, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
            FillDetails(code, tool, input);
            int nl = code.Inlines.OfType<LineBreak>().Count() + 1;
            if (nl > 12)
                body.Children.Add(new TextBlock { Text = nl + " lignes : fais défiler pour tout lire avant d'autoriser.", Foreground = Red, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            body.Children.Add(new Border
            {
                Background = CodeBg, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 10),
                Child = new ScrollViewer { Content = code, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            });

            // Ligne principale toujours au meme endroit : Autoriser, Refuser, Repondre dans Claude.
            var buttons = new WrapPanel();
            buttons.Children.Add(Btn("Autoriser", BtnAllow, () => Answer(p, Replies.Allow()), armed));
            buttons.Children.Add(Btn("Refuser", BtnDeny, () => Answer(p, Replies.Deny()), armed));
            buttons.Children.Add(HandBack(p, armed));
            body.Children.Add(buttons);

            // En dessous, un bouton "Toujours" par suggestion de Claude, avec sa portee en clair.
            if (!Shells.Contains(tool))
                foreach (var o in (Json.Arr(Json.Get(p.Payload, "permission_suggestions")) ?? new object[0]).Take(3))
                {
                    var sd = Json.Obj(o);
                    var label = sd == null ? null : Replies.AlwaysLabel(sd);
                    if (label == null) continue;
                    var b = Btn(Safe(label) + " (session)", BtnNeutral, () => Answer(p, Replies.AllowAlways(sd)), armed);
                    b.HorizontalAlignment = HorizontalAlignment.Left;
                    b.ToolTip = "Ne plus demander pour cette portée jusqu'à la fin de la session";
                    body.Children.Add(b);
                }
            return true;
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
                tb.Inlines.Add(new Run(Safe(s)) { Foreground = b ?? Ink });
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
                    if (edits.Length > 3) line("… " + (edits.Length - 3) + " autres modifications", Faint);
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
            if (lines.Length > max) line(prefix + "… (" + (lines.Length - max) + " lignes de plus)", Faint);
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
                        Background = BtnNeutral, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1),
                        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, i == 0 ? 0 : 10, 0, 4),
                        Child = new TextBlock { Text = Safe(header), Foreground = Muted, FontSize = 11 }
                    });
                body.Children.Add(new TextBlock
                {
                    Text = Safe(Json.Str(q, "question")) + (multi ? "  (plusieurs choix possibles)" : ""),
                    Foreground = Ink, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6)
                });

                var optionBorders = new List<Border>();
                Border otherBorder = null;
                Action paint = () =>
                {
                    foreach (var ob in optionBorders) ob.Background = picked[i].Contains((string)ob.Tag) ? BtnSel : BtnNeutral;
                    otherBorder.Background = otherOn[i] ? BtnSel : BtnNeutral;
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
                        if (multi)
                        {
                            if (!picked[i].Remove(label)) picked[i].Add(label);
                        }
                        else { picked[i].Clear(); picked[i].Add(label); otherOn[i] = false; }
                        paint();
                    };
                    optionBorders.Add(ob);
                    body.Children.Add(ob);
                }

                otherBorder = Option("Autre…", "Répondre avec ton propre texte");
                otherBorder.MouseLeftButtonUp += (s, e) =>
                {
                    if (!armed()) return;
                    otherOn[i] = !otherOn[i] || !multi;
                    if (!multi) picked[i].Clear();
                    paint();
                    if (otherOn[i]) FocusBox(otherText[i]);
                };
                body.Children.Add(otherBorder);

                var box = new TextBox
                {
                    FontSize = 12.5, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 2, 0, 4),
                    Background = CodeBg, Foreground = Ink, CaretBrush = Ink, BorderBrush = Line,
                    TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, Visibility = Visibility.Collapsed
                };
                box.PreviewMouseLeftButtonDown += (s, e) => FocusBox(box);
                box.TextChanged += (s, e) => refreshSend();
                otherText[i] = box;
                body.Children.Add(box);
            }

            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            send = (Border)Btn("Envoyer", BtnAllow, () =>
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

        Border Option(string label, string description)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, Foreground = Ink, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(description))
                sp.Children.Add(new TextBlock { Text = description, Foreground = Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
            return new Border
            {
                Background = BtnNeutral, CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 4), Child = sp, Cursor = Cursors.Hand
            };
        }

        // Seul moment ou l'encoche prend le clavier.
        void FocusBox(TextBox box)
        {
            if (!_activatable) SetActivatable(true);
            Activate();
            Dispatcher.BeginInvoke((Action)(() => { box.Focus(); Keyboard.Focus(box); }), DispatcherPriority.Input);
        }

        // Bouton maison (le style WPF par defaut detonne sur fond sombre). Le clic
        // compte seulement s'il commence et finit sur le bouton : jamais au survol.
        FrameworkElement Btn(string text, Brush bg, Action click, Func<bool> armed)
        {
            var b = new Border
            {
                Background = bg, CornerRadius = new CornerRadius(7), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = text, Foreground = Ink, FontSize = 12.5, FontWeight = FontWeights.SemiBold }
            };
            bool down = false;
            b.MouseLeftButtonDown += (s, e) => { down = true; e.Handled = true; };
            b.MouseLeave += (s, e) => { down = false; b.BorderBrush = Brushes.Transparent; };
            b.MouseEnter += (s, e) => b.BorderBrush = Ink;
            b.MouseLeftButtonUp += (s, e) =>
            {
                if (!down) return;
                down = false;
                e.Handled = true;
                if (armed()) click();
            };
            return b;
        }
    }
}

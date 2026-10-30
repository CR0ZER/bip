// Bip, la mascotte de ClaudeNotch (kit design/bip-mascotte, tokens.json).
// Portage en code du BipControl livre en XAML : le projet se compile sans XAML,
// avec le csc du .NET Framework (C# 5). Geometrie, couleurs, mouvements et durees
// sont ceux du kit, a l'identique.
//
// Grille 32 x 32 mise a l'echelle par le Viewbox : poser le controle a 16 px
// (encoche repliee), 20 px (lignes) ou 40 px (cartes, forme entrouverte).
// A 16 px, 2 unites = 1 px.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ClaudeNotch
{
    // Neutral : hors kit, Bip seul dans l'encoche vide, qui parle a l'utilisateur.
    // DoNotDisturb : Bip endormi, le mode "ne pas deranger" (pas un etat de session).
    enum BipState { Waiting, Running, Ready, Error, Idle, Neutral, DoNotDisturb }

    class Bip : Viewbox
    {
        static readonly Dictionary<BipState, Color> StateColors = new Dictionary<BipState, Color>
        {
            { BipState.Waiting, Hex(0xFF5D52) },
            { BipState.Running, Hex(0xF5A524) },
            { BipState.Ready,   Hex(0x3FD07F) },
            { BipState.Error,   Hex(0x8E96A3) },
            { BipState.Idle,    Hex(0x4A4A4E) },
            { BipState.Neutral, Hex(0xDAD6CF) },   // blanc casse
            { BipState.DoNotDisturb, Hex(0x6C9EFF) },
        };
        static readonly Brush Face = Frozen(Hex(0x141414)), FaceIdle = Frozen(Hex(0x161616)), BadgeFill = Frozen(Hex(0xFFC93D)), Zzz = Frozen(Hex(0x9DBBFF));
        static readonly Random Rng = new Random();

        readonly SolidColorBrush _brush = new SolidColorBrush(StateColors[BipState.Idle]);
        readonly ScaleTransform _scale = new ScaleTransform();
        readonly RotateTransform _tilt = new RotateTransform();
        readonly TranslateTransform _shift = new TranslateTransform();
        readonly ScaleTransform _ballScale = new ScaleTransform();
        readonly ScaleTransform _badgeScale = new ScaleTransform(0, 0, 26, 7.5);
        readonly RotateTransform _armRotate = new RotateTransform(0, 24, 16);
        readonly Canvas _antennaStraight, _antennaBent, _faceRunning, _faceWaiting, _faceReady, _faceError, _faceIdle, _badge, _antennaMoon, _faceDnd, _z1, _z2;
        readonly TranslateTransform _z1Shift = new TranslateTransform(), _z2Shift = new TranslateTransform();
        readonly Path _arm;
        readonly DispatcherTimer _waitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        BipState _state = BipState.Idle;
        DateTime _waitStart, _lastTremble;

        // Debut de l'attente si la session attendait deja avant la creation du controle :
        // le rythme d'impatience repart alors du bon niveau.
        public DateTime? WaitingSince;

        // Images par seconde des animations en boucle. L'encoche repliee (Bip de 16 px) passe a 8.
        public int Fps = 15;

        // Pose fixe : couleur et visage de l'etat, sans aucun mouvement (sessions listees sous le
        // bandeau "ne pas deranger").
        public bool Still;

        public BipState State
        {
            get { return _state; }
            set
            {
                if (value == _state) return;
                var from = _state;
                _state = value;
                if (IsLoaded && IsVisible) Apply(from, value, true);
            }
        }

        public Bip()
        {
            Stretch = Stretch.Uniform;
            UseLayoutRounding = true;

            var root = new Canvas { Width = 32, Height = 32, RenderTransformOrigin = new Point(0.5, 0.875) };
            var tg = new TransformGroup();
            tg.Children.Add(_scale);
            tg.Children.Add(_tilt);
            tg.Children.Add(_shift);
            root.RenderTransform = tg;
            Child = root;

            // Corps et pieds
            root.Children.Add(Rect(7, 11, 18, 14, 4, _brush));
            root.Children.Add(Rect(10, 24, 4, 4, 1.2, _brush));
            root.Children.Add(Rect(18, 24, 4, 4, 1.2, _brush));

            // Antenne droite (tous les etats sauf inactif), boule gonflee en attente
            _antennaStraight = new Canvas();
            _antennaStraight.Children.Add(Line("M16,11 V6.5", 1.8, _brush));
            var ball = Ell(13.8, 2.8, 4.4, 4.4, _brush);
            ball.RenderTransformOrigin = new Point(0.5, 0.5);
            ball.RenderTransform = _ballScale;
            _antennaStraight.Children.Add(ball);
            root.Children.Add(_antennaStraight);

            // Antenne pliee (inactif)
            _antennaBent = new Canvas { Opacity = 0, Visibility = Visibility.Collapsed };
            _antennaBent.Children.Add(Line("M16,11 Q16,7.5 19.5,7.8", 1.8, _brush));
            _antennaBent.Children.Add(Ell(18.6, 6, 4, 4, _brush));
            root.Children.Add(_antennaBent);

            // Antenne en croissant de lune (ne pas deranger)
            _antennaMoon = new Canvas { Opacity = 0, Visibility = Visibility.Collapsed };
            _antennaMoon.Children.Add(Line("M16,11 V6.9", 1.8, _brush));
            _antennaMoon.Children.Add(new Path { Data = Geometry.Parse("M17.2,2.92 A2.4,2.4 0 1 0 17.2,7.08 A2.1,2.1 0 0 1 17.2,2.92 Z"), Fill = _brush });
            root.Children.Add(_antennaMoon);

            // Bras leve (attente)
            _arm = Line("M24,16 L28.5,10.5", 3, _brush);
            _arm.Opacity = 0;
            _arm.Visibility = Visibility.Collapsed;
            _arm.RenderTransform = _armRotate;
            root.Children.Add(_arm);

            // Visages : un seul visible, fondu croise de 150 ms
            _faceRunning = Group(Rect(11, 16.9, 4, 2.2, 1.1, Face), Rect(17, 16.9, 4, 2.2, 1.1, Face));
            _faceWaiting = Group(Ell(11, 15, 4, 4, Face), Ell(17, 15, 4, 4, Face), Ell(14.6, 19.9, 2.8, 3.2, Face));
            _faceReady = Group(Line("M11,18.8 Q13,15.8 15,18.8", 2.2, Face), Line("M17,18.8 Q19,15.8 21,18.8", 2.2, Face),
                               Line("M14.5,21.2 Q16,22.8 17.5,21.2", 1.6, Face));
            _faceError = Group(Rect(11.5, 17, 3, 3, 1.5, Face), Rect(17.5, 17, 3, 3, 1.5, Face));
            _faceIdle = Group(Line("M11,18 Q13,20 15,18", 1.8, FaceIdle), Line("M17,18 Q19,20 21,18", 1.8, FaceIdle));
            _faceDnd = Group(Line("M11,18 Q13,20 15,18", 1.8, Face), Line("M17,18 Q19,20 21,18", 1.8, Face));
            foreach (var f in new[] { _faceRunning, _faceWaiting, _faceReady, _faceError, _faceIdle, _faceDnd }) root.Children.Add(f);

            // Deux Z qui montent (ne pas deranger)
            _z1 = Group(Line("M20.5,5.5 H24.5 L20.5,9.5 H24.5", 1.5, Zzz));
            _z2 = Group(Line("M25.5,1.5 H28 L25.5,4 H28", 1.2, Zzz));
            _z1.RenderTransform = _z1Shift;
            _z2.RenderTransform = _z2Shift;
            root.Children.Add(_z1);
            root.Children.Add(_z2);

            // Pastille "!" (erreur)
            _badge = Group(Ell(21.8, 3.3, 8.4, 8.4, BadgeFill), Rect(25.3, 4.8, 1.4, 3.6, 0.7, Face), Ell(25.15, 9.25, 1.7, 1.7, Face));
            _badge.RenderTransform = _badgeScale;
            root.Children.Add(_badge);

            _waitTimer.Tick += OnWaitTick;
            Loaded += (s, e) => { if (IsVisible) Apply(_state, _state, false); };
            Unloaded += (s, e) => StopLoops();
            // Cache (lui ou un parent) : plus aucune animation. Une boucle d'un Bip masque mais charge
            // (etiquette "ne pas deranger", encoche repliee) forcait le rendu de toute la fenetre.
            IsVisibleChanged += (s, e) => { if (!IsLoaded) return; if (IsVisible) Apply(_state, _state, false); else StopLoops(); };
        }

        void Apply(BipState from, BipState to, bool animate)
        {
            StopLoops();

            _brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(StateColors[to], Ms(animate ? 250 : 0)));

            int face = animate ? 150 : 0;
            Fade(_faceWaiting, to == BipState.Waiting ? 1 : 0, face);
            Fade(_faceRunning, to == BipState.Running ? 1 : 0, face);
            Fade(_faceReady, to == BipState.Ready || to == BipState.Neutral ? 1 : 0, face);
            Fade(_faceError, to == BipState.Error ? 1 : 0, face);
            Fade(_faceIdle, to == BipState.Idle ? 1 : 0, face);
            Fade(_faceDnd, to == BipState.DoNotDisturb ? 1 : 0, animate ? 400 : 0);

            Fade(_antennaStraight, to == BipState.Idle || to == BipState.DoNotDisturb ? 0 : 1, animate ? 400 : 0);
            Fade(_antennaMoon, to == BipState.DoNotDisturb ? 1 : 0, animate ? 400 : 0);
            Fade(_antennaBent, to == BipState.Idle ? 1 : 0, animate ? 400 : 0);
            var ballAnim = new DoubleAnimation(to == BipState.Waiting ? 2.8 / 2.2 : 1.0, Ms(animate ? 200 : 0))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
            _ballScale.BeginAnimation(ScaleTransform.ScaleXProperty, ballAnim);
            _ballScale.BeginAnimation(ScaleTransform.ScaleYProperty, ballAnim);

            Fade(_arm, to == BipState.Waiting ? 1 : 0, animate ? (to == BipState.Waiting ? 200 : 150) : 0);

            Fade(_badge, to == BipState.Error ? 1 : 0, animate ? 150 : 0);
            var badgeAnim = new DoubleAnimation(to == BipState.Error ? 1 : 0, Ms(animate ? 200 : 0))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
            _badgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, badgeAnim);
            _badgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, badgeAnim);

            if (Still)
            {
                // Pose fixe : les Z restent visibles, rien ne bouge.
                if (to == BipState.DoNotDisturb) foreach (var z in new[] { _z1, _z2 }) { z.Opacity = 1; z.Visibility = Visibility.Visible; }
                return;
            }
            switch (to)
            {
                case BipState.DoNotDisturb:
                    Sleep();
                    break;
                case BipState.Waiting:
                    _waitStart = WaitingSince ?? DateTime.Now;
                    _lastTremble = DateTime.Now;
                    BreatheCycle();
                    _waitTimer.Start();
                    break;
                case BipState.Running:
                case BipState.Neutral:
                    Float();
                    break;
                case BipState.Ready:
                    if (animate && from != BipState.Ready) Hop();
                    break;
                case BipState.Error:
                    // Affaissement X 1,04 / Y 0,94 en 250 ms, puis fige
                    var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                    _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.04, Ms(animate ? 250 : 0)) { EasingFunction = ease });
                    _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, Ms(animate ? 250 : 0)) { EasingFunction = ease });
                    break;
            }
        }

        void StopLoops()
        {
            _waitTimer.Stop();
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _tilt.BeginAnimation(RotateTransform.AngleProperty, null);
            _shift.BeginAnimation(TranslateTransform.YProperty, null);
            _armRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            foreach (var z in new[] { _z1, _z2 }) { z.BeginAnimation(OpacityProperty, null); z.Opacity = 0; z.Visibility = Visibility.Collapsed; }
            _z1Shift.BeginAnimation(TranslateTransform.YProperty, null);
            _z2Shift.BeginAnimation(TranslateTransform.YProperty, null);
        }

        // ---- Ne pas deranger : sommeil lent (X 1 -> 1,01, Y 1 -> 1,03, cycle 4 s) et deux Z qui
        // montent en 3 s chacun (opacite 0 -> 1 -> 1 -> 0, Y +1 -> -1,5 u), le second decale de 1,5 s.
        void Sleep()
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, Loop(new DoubleAnimation(1, 1.01, Ms(2000))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease }, Fps));
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, Loop(new DoubleAnimation(1, 1.03, Ms(2000))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease }, Fps));
            for (int n = 0; n < 2; n++)
            {
                var begin = TimeSpan.FromMilliseconds(n * 1500);
                var fade = Steps(new[] { 0, 900, 2100, 3000 }, new[] { 0.0, 1, 1, 0 });
                fade.RepeatBehavior = RepeatBehavior.Forever;
                fade.BeginTime = begin;
                Timeline.SetDesiredFrameRate(fade, Fps);
                (n == 0 ? _z1 : _z2).Visibility = Visibility.Visible;
                (n == 0 ? _z1 : _z2).BeginAnimation(OpacityProperty, fade);
                (n == 0 ? _z1Shift : _z2Shift).BeginAnimation(TranslateTransform.YProperty,
                    Loop(new DoubleAnimation(1, -1.5, Ms(3000)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin }, Fps));
            }
        }

        // ---- En attente : respiration qui accelere, bras qui s'agite, tremblement

        // 1,6 s, puis 1,1 s apres 1 min, 0,8 s apres 3 min.
        double BreathePeriodMs()
        {
            double waited = (DateTime.Now - _waitStart).TotalSeconds;
            return waited < 60 ? 1600 : waited < 180 ? 1100 : 800;
        }

        // Un cycle a la fois : le rythme ne change qu'en fin de cycle, sans a-coup.
        void BreatheCycle()
        {
            if (_state != BipState.Waiting || !IsLoaded) return;
            var half = Ms(BreathePeriodMs() / 2);
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var sy = Loop(new DoubleAnimation(1, 1.08, half) { AutoReverse = true, EasingFunction = ease }, Fps);
            var sx = Loop(new DoubleAnimation(1, 1.04, half) { AutoReverse = true, EasingFunction = ease }, Fps);
            var arm = Loop(new DoubleAnimation(-10, 12, half) { AutoReverse = true, EasingFunction = ease }, Fps);
            sy.Completed += (s, e) => BreatheCycle();
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
            _armRotate.BeginAnimation(RotateTransform.AngleProperty, arm);
        }

        void OnWaitTick(object sender, EventArgs e)
        {
            double waited = (DateTime.Now - _waitStart).TotalSeconds;
            if ((DateTime.Now - _lastTremble).TotalSeconds < (waited >= 180 ? 4 : 6)) return;
            _lastTremble = DateTime.Now;
            // -4 / +4 / -3 / 0 degres en 300 ms
            var k = new DoubleAnimationUsingKeyFrames { Duration = Ms(300) };
            k.KeyFrames.Add(new LinearDoubleKeyFrame(-4, Kt(60)));
            k.KeyFrames.Add(new LinearDoubleKeyFrame(4, Kt(150)));
            k.KeyFrames.Add(new LinearDoubleKeyFrame(-3, Kt(225)));
            k.KeyFrames.Add(new LinearDoubleKeyFrame(0, Kt(300)));
            _tilt.BeginAnimation(RotateTransform.AngleProperty, k);
        }

        // ---- En cours : flottement Y 0 -> -2 u et rotation +-2 deg, cycle 3,2 s,
        // depart decale de 0 a 1 s pour que plusieurs Bip ne flottent pas en phase.
        void Float()
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var delay = TimeSpan.FromMilliseconds(Rng.Next(0, 1000));
            _shift.BeginAnimation(TranslateTransform.YProperty, Loop(new DoubleAnimation(0, -2, Ms(1600))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease, BeginTime = delay }, Fps));
            _tilt.BeginAnimation(RotateTransform.AngleProperty, Loop(new DoubleAnimation(-2, 2, Ms(1600))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease, BeginTime = delay }, Fps));
        }

        // ---- Pret : un saut, une seule fois (tassement, montee -6 u, reception, repos)
        void Hop()
        {
            var o = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var i = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(new[] { 0, 90, 270, 390, 510 }, new[] { 1, 1.06, 0.96, 1.08, 1 }, o, i));
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(new[] { 0, 90, 270, 390, 510 }, new[] { 1, 0.90, 1.05, 0.90, 1 }, o, i));
            _shift.BeginAnimation(TranslateTransform.YProperty, Keys(new[] { 0, 90, 270, 390, 510 }, new[] { 0, 0, -6.0, 0, 0 }, o, i));
        }

        // ---- Reaction au clic : un saut, un clignement des yeux ou un petit signe.
        public void Poke()
        {
            if (_state == BipState.DoNotDisturb) return;   // il dort
            switch (Rng.Next(3))
            {
                case 0:
                    Hop();
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) };
                    t.Tick += (s, e) => { t.Stop(); if (_state == BipState.Running || _state == BipState.Neutral) Float(); };
                    t.Start();
                    break;
                case 1: Blink(); break;
                default: Wave(); break;
            }
        }

        // Yeux fermes 120 ms (le visage endormi), puis rouverts.
        void Blink()
        {
            if (_state == BipState.Idle) return;
            UIElement face = _state == BipState.Waiting ? _faceWaiting : _state == BipState.Running ? _faceRunning
                           : _state == BipState.Error ? _faceError : _faceReady;
            face.BeginAnimation(OpacityProperty, Steps(new[] { 0, 60, 180, 240 }, new[] { 1.0, 0, 0, 1 }));
            _faceIdle.Visibility = Visibility.Visible;
            _faceIdle.BeginAnimation(OpacityProperty, Steps(new[] { 0, 60, 180, 240 }, new[] { 0.0, 1, 1, 0 }));
        }

        // Le bras se leve et s'agite deux fois.
        void Wave()
        {
            if (_state == BipState.Waiting) return;   // il s'agite deja
            _arm.Visibility = Visibility.Visible;
            _arm.BeginAnimation(OpacityProperty, Steps(new[] { 0, 120, 900, 1050 }, new[] { 0.0, 1, 1, 0 }));
            _armRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-10, 12, Ms(180))
                { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), BeginTime = TimeSpan.FromMilliseconds(100) });
        }

        static DoubleAnimationUsingKeyFrames Steps(int[] ms, double[] v)
        {
            var a = new DoubleAnimationUsingKeyFrames();
            for (int n = 0; n < ms.Length; n++) a.KeyFrames.Add(new LinearDoubleKeyFrame(v[n], Kt(ms[n])));
            return a;
        }

        // Trames du saut : la 3e image monte (ease out), la 4e redescend (ease in).
        static DoubleAnimationUsingKeyFrames Keys(int[] ms, double[] v, IEasingFunction up, IEasingFunction down)
        {
            var a = new DoubleAnimationUsingKeyFrames();
            for (int n = 0; n < ms.Length; n++)
                a.KeyFrames.Add(new EasingDoubleKeyFrame(v[n], Kt(ms[n]), n == 2 ? up : n == 3 ? down : null));
            return a;
        }

        // ---- Utilitaires de dessin

        static Rectangle Rect(double x, double y, double w, double h, double r, Brush fill)
        {
            var e = new Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r, Fill = fill };
            Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
            return e;
        }

        static Ellipse Ell(double x, double y, double w, double h, Brush fill)
        {
            var e = new Ellipse { Width = w, Height = h, Fill = fill };
            Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
            return e;
        }

        static Path Line(string data, double thickness, Brush stroke)
        {
            return new Path
            {
                Data = Geometry.Parse(data), Stroke = stroke, StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            };
        }

        static Canvas Group(params UIElement[] children)
        {
            var c = new Canvas { Opacity = 0, Visibility = Visibility.Collapsed };
            foreach (var ch in children) c.Children.Add(ch);
            return c;
        }

        // Un element transparent est aussi retire de l'affichage : en rendu logiciel, il serait
        // redessine a chaque image d'un Bip qui bouge (mesure : ~1 point d'un coeur par Bip anime).
        static void Fade(UIElement el, double to, int ms)
        {
            if (to > 0) el.Visibility = Visibility.Visible;
            if (ms <= 0)
            {
                el.BeginAnimation(OpacityProperty, null);
                el.Opacity = to;
                if (to == 0) el.Visibility = Visibility.Collapsed;
                return;
            }
            var a = new DoubleAnimation(to, Ms(ms));
            if (to == 0) a.Completed += (s, e) => { if (el.Opacity == 0) el.Visibility = Visibility.Collapsed; };
            el.BeginAnimation(OpacityProperty, a);
        }

        // Animations en boucle, lentes et de quelques pixels : pas besoin de 60 images/s.
        // Bip : 15, et 8 dans l'encoche repliee (son flottement y fait 1 px) ; l'eclat de l'encoche : 30, sinon
        // son balayage saccade. Les transitions courtes restent a 60.
        public static DoubleAnimation Loop(DoubleAnimation a, int fps = 15) { Timeline.SetDesiredFrameRate(a, fps); return a; }

        static Duration Ms(double ms) { return new Duration(TimeSpan.FromMilliseconds(ms)); }
        static KeyTime Kt(int ms) { return KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)); }
        static Color Hex(int rgb) { return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); }
        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }
}

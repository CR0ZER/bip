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
    enum BipState { Waiting, Running, Ready, Error, Idle, Neutral }

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
        };
        static readonly Brush Face = Frozen(Hex(0x141414)), FaceIdle = Frozen(Hex(0x161616)), BadgeFill = Frozen(Hex(0xFFC93D));
        static readonly Random Rng = new Random();

        readonly SolidColorBrush _brush = new SolidColorBrush(StateColors[BipState.Idle]);
        readonly ScaleTransform _scale = new ScaleTransform();
        readonly RotateTransform _tilt = new RotateTransform();
        readonly TranslateTransform _shift = new TranslateTransform();
        readonly ScaleTransform _ballScale = new ScaleTransform();
        readonly ScaleTransform _badgeScale = new ScaleTransform(0, 0, 26, 7.5);
        readonly RotateTransform _armRotate = new RotateTransform(0, 24, 16);
        readonly Canvas _antennaStraight, _antennaBent, _faceRunning, _faceWaiting, _faceReady, _faceError, _faceIdle, _badge;
        readonly Path _arm;
        readonly DispatcherTimer _waitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        BipState _state = BipState.Idle;
        DateTime _waitStart, _lastTremble;

        // Debut de l'attente si la session attendait deja avant la creation du controle :
        // le rythme d'impatience repart alors du bon niveau.
        public DateTime? WaitingSince;

        public BipState State
        {
            get { return _state; }
            set
            {
                if (value == _state) return;
                var from = _state;
                _state = value;
                if (IsLoaded) Apply(from, value, true);
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
            _antennaBent = new Canvas { Opacity = 0 };
            _antennaBent.Children.Add(Line("M16,11 Q16,7.5 19.5,7.8", 1.8, _brush));
            _antennaBent.Children.Add(Ell(18.6, 6, 4, 4, _brush));
            root.Children.Add(_antennaBent);

            // Bras leve (attente)
            _arm = Line("M24,16 L28.5,10.5", 3, _brush);
            _arm.Opacity = 0;
            _arm.RenderTransform = _armRotate;
            root.Children.Add(_arm);

            // Visages : un seul visible, fondu croise de 150 ms
            _faceRunning = Group(Rect(11, 16.9, 4, 2.2, 1.1, Face), Rect(17, 16.9, 4, 2.2, 1.1, Face));
            _faceWaiting = Group(Ell(11, 15, 4, 4, Face), Ell(17, 15, 4, 4, Face), Ell(14.6, 19.9, 2.8, 3.2, Face));
            _faceReady = Group(Line("M11,18.8 Q13,15.8 15,18.8", 2.2, Face), Line("M17,18.8 Q19,15.8 21,18.8", 2.2, Face),
                               Line("M14.5,21.2 Q16,22.8 17.5,21.2", 1.6, Face));
            _faceError = Group(Rect(11.5, 17, 3, 3, 1.5, Face), Rect(17.5, 17, 3, 3, 1.5, Face));
            _faceIdle = Group(Line("M11,18 Q13,20 15,18", 1.8, FaceIdle), Line("M17,18 Q19,20 21,18", 1.8, FaceIdle));
            foreach (var f in new[] { _faceRunning, _faceWaiting, _faceReady, _faceError, _faceIdle }) root.Children.Add(f);

            // Pastille "!" (erreur)
            _badge = Group(Ell(21.8, 3.3, 8.4, 8.4, BadgeFill), Rect(25.3, 4.8, 1.4, 3.6, 0.7, Face), Ell(25.15, 9.25, 1.7, 1.7, Face));
            _badge.RenderTransform = _badgeScale;
            root.Children.Add(_badge);

            _waitTimer.Tick += OnWaitTick;
            Loaded += (s, e) => Apply(_state, _state, false);
            Unloaded += (s, e) => StopLoops();
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

            Fade(_antennaStraight, to == BipState.Idle ? 0 : 1, animate ? 400 : 0);
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

            switch (to)
            {
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
            var sy = Loop(new DoubleAnimation(1, 1.08, half) { AutoReverse = true, EasingFunction = ease });
            var sx = Loop(new DoubleAnimation(1, 1.04, half) { AutoReverse = true, EasingFunction = ease });
            var arm = Loop(new DoubleAnimation(-10, 12, half) { AutoReverse = true, EasingFunction = ease });
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
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease, BeginTime = delay }));
            _tilt.BeginAnimation(RotateTransform.AngleProperty, Loop(new DoubleAnimation(-2, 2, Ms(1600))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease, BeginTime = delay }));
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
            var c = new Canvas { Opacity = 0 };
            foreach (var ch in children) c.Children.Add(ch);
            return c;
        }

        static void Fade(UIElement el, double to, int ms)
        {
            if (ms <= 0) { el.BeginAnimation(OpacityProperty, null); el.Opacity = to; return; }
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(to, Ms(ms)));
        }

        // Animations en boucle, lentes et de quelques pixels : pas besoin de 60 images/s.
        // Bip : 15 (son flottement fait 1 px a 16 px) ; l'eclat de l'encoche : 30, sinon
        // son balayage saccade. Les transitions courtes restent a 60.
        public static DoubleAnimation Loop(DoubleAnimation a, int fps = 15) { Timeline.SetDesiredFrameRate(a, fps); return a; }

        static Duration Ms(double ms) { return new Duration(TimeSpan.FromMilliseconds(ms)); }
        static KeyTime Kt(int ms) { return KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)); }
        static Color Hex(int rgb) { return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); }
        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }
}

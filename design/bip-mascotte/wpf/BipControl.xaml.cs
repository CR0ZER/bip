using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ClaudeNotch.Controls
{
    /// <summary>Les cinq états d'une session Claude Code, tels que Bip les montre.</summary>
    public enum BipState
    {
        /// <summary>Claude attend une réponse (permission, question) ou est à l'arrêt depuis 1 min.</summary>
        Waiting,
        /// <summary>Claude travaille (réfléchit, lit, exécute).</summary>
        Running,
        /// <summary>Claude a fini et rend la main.</summary>
        Ready,
        /// <summary>Le tour s'est arrêté sur une erreur de l'API.</summary>
        Error,
        /// <summary>Session ouverte mais au repos.</summary>
        Idle
    }

    /// <summary>
    /// Bip, la mascotte de ClaudeNotch. Toutes les amplitudes sont exprimées dans la grille 32 x 32 :
    /// 2 unités = 1 px quand le contrôle est affiché à 16 px.
    /// </summary>
    public partial class BipControl : UserControl
    {
        public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
            nameof(State), typeof(BipState), typeof(BipControl),
            new PropertyMetadata(BipState.Idle, OnStateChanged));

        /// <summary>
        /// Début de l'attente, si la session attendait déjà avant la création du contrôle.
        /// Laisser à null pour compter à partir du passage en Waiting.
        /// </summary>
        public static readonly DependencyProperty WaitingSinceProperty = DependencyProperty.Register(
            nameof(WaitingSince), typeof(DateTime?), typeof(BipControl), new PropertyMetadata(null));

        public BipState State
        {
            get => (BipState)GetValue(StateProperty);
            set => SetValue(StateProperty, value);
        }

        public DateTime? WaitingSince
        {
            get => (DateTime?)GetValue(WaitingSinceProperty);
            set => SetValue(WaitingSinceProperty, value);
        }

        // Palette (identique à tokens.json et BipResources.xaml)
        private static readonly Dictionary<BipState, Color> StateColors = new Dictionary<BipState, Color>
        {
            [BipState.Waiting] = Hex("#FF5D52"),
            [BipState.Running] = Hex("#F5A524"),
            [BipState.Ready] = Hex("#3FD07F"),
            [BipState.Error] = Hex("#8E96A3"),
            [BipState.Idle] = Hex("#4A4A4E"),
        };

        private static readonly Random Rng = new Random();

        private readonly SolidColorBrush _stateBrush = new SolidColorBrush(StateColors[BipState.Idle]);
        private readonly DispatcherTimer _waitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private DateTime _waitStart;
        private DateTime _lastTremble;

        public BipControl()
        {
            InitializeComponent();

            foreach (var shape in new Shape[] { BodyRect, FootL, FootR, AntennaBall, AntennaBallBent })
                shape.Fill = _stateBrush;
            foreach (var shape in new Shape[] { AntennaStem, AntennaBent, Arm })
                shape.Stroke = _stateBrush;

            _waitTimer.Tick += OnWaitTick;
            Loaded += (_, __) => ApplyState(State, State, animate: false);
            Unloaded += (_, __) => StopLoops();
        }

        private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var bip = (BipControl)d;
            if (bip.IsLoaded)
                bip.ApplyState((BipState)e.OldValue, (BipState)e.NewValue, animate: true);
        }

        private void ApplyState(BipState from, BipState to, bool animate)
        {
            StopLoops();

            // Couleur : interpolée en 250 ms
            _stateBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(StateColors[to], Ms(animate ? 250 : 0)));

            // Visage : fondu croisé 150 ms
            int face = animate ? 150 : 0;
            Fade(FaceWaiting, to == BipState.Waiting ? 1 : 0, face);
            Fade(FaceRunning, to == BipState.Running ? 1 : 0, face);
            Fade(FaceReady, to == BipState.Ready ? 1 : 0, face);
            Fade(FaceError, to == BipState.Error ? 1 : 0, face);
            Fade(FaceIdle, to == BipState.Idle ? 1 : 0, face);

            // Antenne : droite, gonflée en attente, pliée au repos (400 ms)
            Fade(AntennaStraight, to == BipState.Idle ? 0 : 1, animate ? 400 : 0);
            Fade(AntennaBentGroup, to == BipState.Idle ? 1 : 0, animate ? 400 : 0);
            double ball = to == BipState.Waiting ? 2.8 / 2.2 : 1.0;
            var ballAnim = new DoubleAnimation(ball, Ms(animate ? 200 : 0)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
            AntennaBallScale.BeginAnimation(ScaleTransform.ScaleXProperty, ballAnim);
            AntennaBallScale.BeginAnimation(ScaleTransform.ScaleYProperty, ballAnim);

            // Bras : sort en 200 ms, rentre en 150 ms
            Fade(Arm, to == BipState.Waiting ? 1 : 0, animate ? (to == BipState.Waiting ? 200 : 150) : 0);

            // Pastille « ! » : 200 ms, BackEase Out
            Fade(Badge, to == BipState.Error ? 1 : 0, animate ? 150 : 0);
            double badge = to == BipState.Error ? 1 : 0;
            var badgeAnim = new DoubleAnimation(badge, Ms(animate ? 200 : 0)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
            BadgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, badgeAnim);
            BadgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, badgeAnim);

            switch (to)
            {
                case BipState.Waiting:
                    _waitStart = WaitingSince ?? DateTime.Now;
                    _lastTremble = DateTime.Now;
                    StartBreatheCycle();
                    _waitTimer.Start();
                    break;
                case BipState.Running:
                    StartFloat();
                    break;
                case BipState.Ready:
                    if (animate && from != BipState.Ready) PlayHop();
                    break;
                case BipState.Error:
                    // Affaissement : X 1,04 / Y 0,94 en 250 ms, puis figé
                    var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                    RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.04, Ms(animate ? 250 : 0)) { EasingFunction = ease });
                    RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, Ms(animate ? 250 : 0)) { EasingFunction = ease });
                    break;
                case BipState.Idle:
                    break;
            }
        }

        /// <summary>Arrête toutes les boucles et ramène le corps au repos.</summary>
        private void StopLoops()
        {
            _waitTimer.Stop();
            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            RootTilt.BeginAnimation(RotateTransform.AngleProperty, null);
            RootShift.BeginAnimation(TranslateTransform.YProperty, null);
            ArmRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        // ---- En attente : respiration qui accélère, bras qui s'agite, tremblement ----

        /// <summary>Durée d'un cycle de respiration : 1,6 s, puis 1,1 s après 1 min, 0,8 s après 3 min.</summary>
        private double BreathePeriodMs()
        {
            double waited = (DateTime.Now - _waitStart).TotalSeconds;
            return waited < 60 ? 1600 : waited < 180 ? 1100 : 800;
        }

        /// <summary>Un cycle à la fois : le rythme ne change qu'en fin de cycle, sans à-coup.</summary>
        private void StartBreatheCycle()
        {
            if (State != BipState.Waiting) return;

            var half = Ms(BreathePeriodMs() / 2);
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var sy = new DoubleAnimation(1, 1.08, half) { AutoReverse = true, EasingFunction = ease };
            var sx = new DoubleAnimation(1, 1.04, half) { AutoReverse = true, EasingFunction = ease };
            var arm = new DoubleAnimation(-10, 12, half) { AutoReverse = true, EasingFunction = ease };
            sy.Completed += (_, __) => StartBreatheCycle();

            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
            ArmRotate.BeginAnimation(RotateTransform.AngleProperty, arm);
        }

        private void OnWaitTick(object sender, EventArgs e)
        {
            double waited = (DateTime.Now - _waitStart).TotalSeconds;
            double every = waited >= 180 ? 4 : 6;
            if ((DateTime.Now - _lastTremble).TotalSeconds >= every)
            {
                _lastTremble = DateTime.Now;
                PlayTremble();
            }
        }

        /// <summary>Tremblement : -4° / +4° / -3° / 0 en 300 ms.</summary>
        private void PlayTremble()
        {
            var k = new DoubleAnimationUsingKeyFrames { Duration = Ms(300) };
            k.KeyFrames.Add(new LinearDoubleKeyFrame(-4, Kt(60)));
            k.KeyFrames.Add(new LinearDoubleKeyFrame(4, Kt(150)));
            k.KeyFrames.Add(new LinearDoubleKeyFrame(-3, Kt(225)));
            k.KeyFrames.Add(new LinearDoubleKeyFrame(0, Kt(300)));
            RootTilt.BeginAnimation(RotateTransform.AngleProperty, k);
        }

        // ---- En cours : flottement ----

        /// <summary>Y 0 → -2 u (−1 px à 16 px) et rotation ±2°, cycle 3,2 s, départ décalé 0–1 s.</summary>
        private void StartFloat()
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var delay = TimeSpan.FromMilliseconds(Rng.Next(0, 1000));
            var y = new DoubleAnimation(0, -2, Ms(1600))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease, BeginTime = delay };
            var r = new DoubleAnimation(-2, 2, Ms(1600))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease, BeginTime = delay };
            RootShift.BeginAnimation(TranslateTransform.YProperty, y);
            RootTilt.BeginAnimation(RotateTransform.AngleProperty, r);
        }

        // ---- Prêt : petit saut, une seule fois ----

        /// <summary>Tassement 90 ms, montée 180 ms (−6 u = −3 px à 16 px), réception 120 ms, retour 120 ms.</summary>
        private void PlayHop()
        {
            var outE = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var inE = new QuadraticEase { EasingMode = EasingMode.EaseIn };

            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                Keys((0, 1, null), (90, 1.06, null), (270, 0.96, outE), (390, 1.08, inE), (510, 1, null)));
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                Keys((0, 1, null), (90, 0.90, null), (270, 1.05, outE), (390, 0.90, inE), (510, 1, null)));
            RootShift.BeginAnimation(TranslateTransform.YProperty,
                Keys((0, 0, null), (90, 0, null), (270, -6, outE), (390, 0, inE), (510, 0, null)));
        }

        // ---- Utilitaires ----

        private static DoubleAnimationUsingKeyFrames Keys(params (int ms, double value, IEasingFunction ease)[] frames)
        {
            var a = new DoubleAnimationUsingKeyFrames();
            foreach (var f in frames)
                a.KeyFrames.Add(new EasingDoubleKeyFrame(f.value, Kt(f.ms), f.ease));
            return a;
        }

        private static void Fade(UIElement element, double to, int ms)
        {
            if (ms <= 0)
            {
                element.BeginAnimation(OpacityProperty, null);
                element.Opacity = to;
                return;
            }
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, Ms(ms)));
        }

        private static Duration Ms(double ms) => new Duration(TimeSpan.FromMilliseconds(ms));
        private static KeyTime Kt(int ms) => KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms));
        private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    }
}

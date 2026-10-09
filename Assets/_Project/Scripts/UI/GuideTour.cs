using System;
using UnityEngine;
using UnityEngine.UIElements;
using BuildAR.Managers;

namespace BuildAR.UI
{
    /// <summary>
    /// Ryan, the friendly PC builder who walks a new learner around Home on their first visit. Each step dims the
    /// screen, cuts a spotlight around one part of it and has Ryan explain it in a speech bubble, typed out as
    /// if spoken. Started by HomeScreenController while SaveData.guideTourPending is set (the first launch only),
    /// and from the profile panel on request.
    ///
    /// Ryan is drawn from plain elements (see the "guided tour" section of BuildAR.uss) so he can blink, talk and
    /// wave. A PNG at Resources/Guide/guide_avatar replaces the drawing — transparent background, the character
    /// alone, no bubble.
    /// </summary>
    public static class GuideTour
    {
        public const string GuideName = "Ryan";
        public const string ArtResource = "Guide/guide_avatar";

        struct Step
        {
            public Func<VisualElement, VisualElement> target; // null: no spotlight, Ryan just talks
            public bool round;                                // spotlight is a circle (the scan button)
            public string text;                               // ", {name}" is dropped when there's no name
            public string next;                               // Next button text, if not "Next"
        }

        static readonly Step[] Steps =
        {
            new Step { next = "Show me around",
                text = "Hi there, {name}! I'm Ryan, your build buddy. It's your first visit, so let me show you around. It only takes a minute!" },
            new Step { target = r => r.Q(className: "header-row"),
                text = "Up here I'll say hello each time you drop by. The ring fills up as you learn, so you can always see how far you've come." },
            new Step { target = r => r.Q(className: "stat-strip"),
                text = "These are your rewards: your daily streak, hearts for quizzes, coins to spend and your level. Tap any of them to find out more." },
            new Step { target = r => r.Q(className: "hero-card"),
                text = "This card shows your next step in building a PC. Tap Start when you're ready, and we'll put one together piece by piece." },
            new Step { target = r => r.Q("qa-scan"),
                text = "Got a real computer part nearby? Tap here and point your camera at it. I'll tell you what it is and show it to you in 3D!" },
            new Step { target = r => r.Q("qa-lesson"),
                text = "Lessons are short and full of pictures. This button always picks up right where you left off." },
            new Step { target = r => r.Q("qa-practice"),
                text = "Here you can build a whole PC on screen by dragging each part into place. Don't worry, nothing can break in here!" },
            new Step { target = r => r.Q("qa-quiz"),
                text = "Memorize is a quick quiz that helps new things stick. A few minutes a day is all it takes." },
            new Step { target = r => r.Q("bottom-nav"),
                text = "Use this bar to get around. Home brings you back here, Learn has every lesson, Practice opens the build, and My Progress shows your badges." },
            new Step { target = r => r.Q("nav-fab"), round = true,
                text = "And this big blue button opens the scanner from anywhere in the app. Handy!" },
            new Step { target = r => r.Q(className: "app-bar-right"),
                text = "Tap your picture to change your name, avatar or music. The moon turns on dark mode, which is easier on your eyes at night." },
            new Step { next = "Let's go!",
                text = "That's the tour! My tip: start with a lesson, then try it out in Practice. Two quiz questions a day keep your streak going. You've got this, {name}!" },
        };

        /// <summary>Starts the tour over the screen in <paramref name="root"/> (Home). Does nothing if it's already running.</summary>
        public static void Show(VisualElement root, Action onDone = null)
        {
            var host = root.Q(className: "screen") ?? root;
            if (host.Q(className: "tour") != null) return;
            new Runner(root, host, onDone).Start();
        }

        static string Named(string text)
        {
            string name = ProgressManager.Instance != null ? ProgressManager.Instance.DisplayName : "";
            return text.Replace(", {name}", name.Length > 0 ? ", " + name : "");
        }

        // ------------------------------------------------------------------ one running tour

        class Runner
        {
            const float Pad = 8f;        // spotlight margin around the highlighted element
            const float Gap = 12f;       // between the spotlight and Ryan's panel
            const float Hole = 1200f;    // border width of the spotlight: the dim is a border around a rounded hole
            const float Corner = 18f;
            const float CharMs = 24f;    // typing speed

            readonly VisualElement _root, _host, _overlay, _spot, _ring, _panel;
            readonly Label _text, _count, _nextLabel;
            readonly Button _back;
            readonly Character _guide;
            readonly Action _onDone;

            int _index = -1;
            string _full = "";
            int _typed;
            float _typeStart;
            IVisualElementScheduledItem _typing, _scroll, _pulse;

            public Runner(VisualElement root, VisualElement host, Action onDone)
            {
                _root = root;
                _host = host;
                _onDone = onDone;

                // Blocks taps on the screen underneath: the tour is driven by its own buttons.
                _overlay = UIUtil.Box("tour", "tour--hidden");
                _spot = UIUtil.Box("tour-spot");
                _ring = UIUtil.Box("tour-ring");
                _ring.pickingMode = PickingMode.Ignore;
                _overlay.Add(_spot);
                _overlay.Add(_ring);

                _panel = UIUtil.Box("tour-panel", "tour-panel--out");
                _guide = new Character();
                _panel.Add(_guide.Root);

                var bubble = UIUtil.Box("tour-bubble");
                var head = UIUtil.Box("tour-head");
                head.Add(UIUtil.Text(GuideName, "tour-name"));
                _count = UIUtil.Text("", "tour-count");
                head.Add(_count);
                head.Add(UIUtil.Box("tour-spacer"));
                head.Add(UIUtil.MakeButton("Skip tour", () => Close(finished: false), "link-btn", "tour-skip"));
                bubble.Add(head);

                _text = UIUtil.Text("", "tour-text");
                _text.enableRichText = true;
                _text.RegisterCallback<ClickEvent>(_ => FinishTyping()); // tap the words to see them all at once
                bubble.Add(_text);

                var foot = UIUtil.Box("tour-foot");
                _back = UIUtil.MakeButton("Back", () => GoTo(_index - 1), "link-btn", "tour-back");
                foot.Add(_back);
                var next = UIUtil.MakeButton("", Next, "btn", "btn-primary", "btn-sm", "tour-next");
                _nextLabel = new Label();
                next.Add(_nextLabel);
                next.Add(new LineIcon("arrow-right"));
                foot.Add(next);
                bubble.Add(foot);

                bubble.Add(UIUtil.Box("tour-tail")); // last, so it paints over the bubble's outline where they meet
                _panel.Add(bubble);
                _overlay.Add(_panel);
            }

            public void Start()
            {
                _host.Add(_overlay);
                _pulse = _ring.schedule.Execute(() => _ring.ToggleInClassList("tour-ring--dim")).Every(900);
                _overlay.schedule.Execute(() =>
                {
                    _overlay.RemoveFromClassList("tour--hidden");
                    GoTo(0);
                }).StartingIn(40);
            }

            void Next()
            {
                if (_index >= Steps.Length - 1) Close(finished: true);
                else GoTo(_index + 1);
            }

            void GoTo(int index)
            {
                if (index < 0 || index >= Steps.Length) return;
                _index = index;
                var step = Steps[index];

                var target = step.target?.Invoke(_root);
                if (target != null && (target.resolvedStyle.display == DisplayStyle.None || target.worldBound.width <= 0f)) target = null;

                StopTyping();
                _panel.AddToClassList("tour-panel--out");
                ScrollTo(target, () => _overlay.schedule.Execute(() =>
                {
                    if (_index != index || _overlay.panel == null) return; // moved on, or closed, while scrolling
                    Rect? spot = target != null ? _overlay.WorldToLocal(target.worldBound) : (Rect?)null;
                    Spotlight(spot, step.round);
                    Place(spot);

                    _count.text = $"{index + 1} of {Steps.Length}";
                    _back.style.visibility = index == 0 ? Visibility.Hidden : Visibility.Visible;
                    _nextLabel.text = step.next ?? "Next";
                    Say(Named(step.text));
                    _panel.RemoveFromClassList("tour-panel--out");
                    if (spot == null) _guide.Wave();
                }).StartingIn(170));
            }

            // ---------- spotlight + placement

            /// <summary>
            /// The dim is one element: a huge border around a rounded hole the size of <paramref name="spot"/>. Moving
            /// it slides the hole to the next target (left/top/width/height are transitioned in the USS).
            /// </summary>
            void Spotlight(Rect? spot, bool round)
            {
                float w = _overlay.layout.width, h = _overlay.layout.height;
                Rect s = spot.HasValue
                    ? new Rect(spot.Value.x - Pad, spot.Value.y - Pad, spot.Value.width + Pad * 2f, spot.Value.height + Pad * 2f)
                    : new Rect(w * 0.5f, h * 0.45f, 0f, 0f);
                float radius = round ? Mathf.Min(s.width, s.height) * 0.5f : Corner;

                _spot.style.left = s.x - Hole;
                _spot.style.top = s.y - Hole;
                _spot.style.width = s.width + Hole * 2f;
                _spot.style.height = s.height + Hole * 2f;
                SetRadius(_spot, radius + Hole);

                _ring.style.left = s.x;
                _ring.style.top = s.y;
                _ring.style.width = s.width;
                _ring.style.height = s.height;
                SetRadius(_ring, radius);
                _ring.EnableInClassList("tour-ring--off", !spot.HasValue);
            }

            static void SetRadius(VisualElement e, float r)
            {
                e.style.borderTopLeftRadius = r;
                e.style.borderTopRightRadius = r;
                e.style.borderBottomLeftRadius = r;
                e.style.borderBottomRightRadius = r;
            }

            /// <summary>Puts Ryan below a target in the top half of the screen, above one in the bottom half.</summary>
            void Place(Rect? spot)
            {
                float h = _overlay.layout.height;
                var auto = new StyleLength(StyleKeyword.Auto);
                if (!spot.HasValue)
                {
                    _panel.style.top = h * 0.3f;
                    _panel.style.bottom = auto;
                }
                else if (spot.Value.center.y < h * 0.5f)
                {
                    _panel.style.top = spot.Value.yMax + Pad + Gap;
                    _panel.style.bottom = auto;
                }
                else
                {
                    _panel.style.top = auto;
                    _panel.style.bottom = h - (spot.Value.yMin - Pad - Gap);
                }
            }

            /// <summary>
            /// Glides Home's scroll view so the target sits in the upper part of the screen, leaving room for Ryan
            /// below it. With no target it goes back to the top.
            /// </summary>
            void ScrollTo(VisualElement target, Action done)
            {
                _scroll?.Pause();
                var view = _root.Q<ScrollView>("scroll");
                if (view == null || (target != null && !view.contentContainer.Contains(target))) { done(); return; }

                float viewport = view.contentViewport.layout.height;
                float max = Mathf.Max(0f, view.contentContainer.layout.height - viewport);
                float want = 0f;
                if (target != null)
                {
                    float y = target.worldBound.yMin - view.contentContainer.worldBound.yMin;
                    want = Mathf.Clamp(y - viewport * 0.2f, 0f, max);
                }

                float from = view.scrollOffset.y;
                if (float.IsNaN(want) || Mathf.Abs(want - from) < 1f) { done(); return; }

                float start = Time.realtimeSinceStartup;
                _scroll = view.schedule.Execute(() =>
                {
                    float t = Mathf.Clamp01((Time.realtimeSinceStartup - start) / 0.28f);
                    float k = 1f - Mathf.Pow(1f - t, 3f);
                    view.scrollOffset = new Vector2(view.scrollOffset.x, Mathf.Lerp(from, want, k));
                    if (t < 1f) return;
                    _scroll.Pause();
                    done();
                }).Every(16);
            }

            // ---------- talking

            /// <summary>
            /// Types the line out. The untyped rest is already there, just transparent, so the bubble has its final
            /// size from the first letter and nothing jumps as the words appear.
            /// </summary>
            void Say(string text)
            {
                _full = text;
                _typed = 0;
                _typeStart = Time.realtimeSinceStartup;
                RenderText();
                _guide.Talk(true);
                _typing = _text.schedule.Execute(() =>
                {
                    _typed = Mathf.Min(_full.Length, Mathf.FloorToInt((Time.realtimeSinceStartup - _typeStart) * 1000f / CharMs));
                    if (_typed >= _full.Length) FinishTyping();
                    else RenderText();
                }).Every(16);
            }

            void RenderText() =>
                _text.text = _typed >= _full.Length ? _full : _full.Substring(0, _typed) + "<alpha=#00>" + _full.Substring(_typed);

            void FinishTyping()
            {
                StopTyping();
                _typed = _full.Length;
                RenderText();
            }

            void StopTyping()
            {
                _typing?.Pause();
                _typing = null;
                _guide.Talk(false);
            }

            // ---------- end

            void Close(bool finished)
            {
                ProgressManager.Instance?.CompleteGuideTour();
                StopTyping();
                _scroll?.Pause();
                _pulse?.Pause();
                _guide.Stop();
                _index = -1;
                _overlay.AddToClassList("tour--hidden");
                _overlay.schedule.Execute(() =>
                {
                    _overlay.RemoveFromHierarchy();
                    if (finished) Confetti.Burst(_root);
                    else UIUtil.Toast(_root, "No problem!", "You can take the tour again any time from your profile.", "info");
                    _onDone?.Invoke();
                }).StartingIn(300);
            }
        }

        // ------------------------------------------------------------------ Ryan

        /// <summary>The builder: bobs gently, blinks, moves its mouth while talking and waves hello and goodbye.</summary>
        class Character
        {
            public readonly VisualElement Root;
            readonly VisualElement _figure;
            readonly IVisualElementScheduledItem _bob, _blink;
            IVisualElementScheduledItem _talk, _wave;

            public Character()
            {
                Root = UIUtil.Box("guide");
                Root.pickingMode = PickingMode.Ignore;
                _figure = UIUtil.Box("guide__figure");
                Root.Add(_figure);

                var art = Resources.Load<Texture2D>(ArtResource);
                if (art != null)
                {
                    var image = UIUtil.Box("guide__art");
                    image.style.backgroundImage = new StyleBackground(art);
                    _figure.Add(image);
                }
                else Draw(_figure);
                _figure.Query().ForEach(e => e.pickingMode = PickingMode.Ignore);

                _bob = Root.schedule.Execute(() => Root.ToggleInClassList("guide--up")).Every(900);
                _blink = Root.schedule.Execute(() =>
                {
                    Root.AddToClassList("guide--blink");
                    Root.schedule.Execute(() => Root.RemoveFromClassList("guide--blink")).StartingIn(130);
                }).Every(3100);
            }

            /// <summary>
            /// Ryan, back to front: legs and boots, the arm at his side, jacket, waving arm, neck, ears, face, then
            /// the hard hat over the top of his head. Every piece is positioned in the "guided tour" USS section.
            /// </summary>
            static void Draw(VisualElement f)
            {
                VisualElement Part(VisualElement parent, params string[] classes)
                {
                    var e = UIUtil.Box(classes);
                    parent.Add(e);
                    return e;
                }

                Part(f, "guide__leg", "guide__leg--l");
                Part(f, "guide__leg", "guide__leg--r");
                Part(f, "guide__boot", "guide__boot--l");
                Part(f, "guide__boot", "guide__boot--r");

                var armL = Part(f, "guide__arm", "guide__arm--l");
                Part(armL, "guide__hand", "guide__hand--l");

                // Work jacket in the app's blue, with the brand-cyan reflective stripe and a name badge.
                var jacket = Part(f, "guide__jacket");
                Part(jacket, "guide__stripe");
                Part(jacket, "guide__zip");
                Part(jacket, "guide__collar", "guide__collar--l");
                Part(jacket, "guide__collar", "guide__collar--r");
                var badge = Part(jacket, "guide__badge");
                Part(badge, "guide__badge-line");

                var armR = Part(f, "guide__arm", "guide__arm--r"); // the waving one
                Part(armR, "guide__hand", "guide__hand--r");

                Part(f, "guide__neck");
                Part(f, "guide__ear", "guide__ear--l");
                Part(f, "guide__ear", "guide__ear--r");

                var face = Part(f, "guide__face");
                Part(face, "guide__hair", "guide__hair--l");
                Part(face, "guide__hair", "guide__hair--r");
                Part(face, "guide__brow", "guide__brow--l");
                Part(face, "guide__brow", "guide__brow--r");
                Part(face, "guide__eye", "guide__eye--l");
                Part(face, "guide__eye", "guide__eye--r");
                Part(face, "guide__cheek", "guide__cheek--l");
                Part(face, "guide__cheek", "guide__cheek--r");
                var mouth = Part(face, "guide__mouth");
                Part(mouth, "guide__teeth");
                Part(mouth, "guide__tongue");

                // Hard hat with the app's badge on the front.
                var hat = Part(f, "guide__hat");
                Part(hat, "guide__hat-ridge");
                var logo = Part(hat, "guide__hat-logo");
                Part(logo, "guide__hat-logo-mark");
                Part(f, "guide__brim");
            }

            public void Talk(bool on)
            {
                _talk?.Pause();
                _talk = null;
                Root.RemoveFromClassList("guide--talk");
                if (on) _talk = Root.schedule.Execute(() => Root.ToggleInClassList("guide--talk")).Every(120);
            }

            /// <summary>A few quick waves of the right hand.</summary>
            public void Wave()
            {
                _wave?.Pause();
                int flips = 0;
                _wave = Root.schedule.Execute(() =>
                {
                    Root.ToggleInClassList("guide--wave");
                    if (++flips < 6) return;
                    Root.RemoveFromClassList("guide--wave");
                    _wave.Pause();
                }).Every(180);
            }

            public void Stop()
            {
                _bob.Pause();
                _blink.Pause();
                _wave?.Pause();
                Talk(false);
            }
        }
    }
}

using System;
using UnityEngine.UIElements;
using BuildAR.Managers;

namespace BuildAR.UI
{
    /// <summary>Full-screen "set up your profile" panel: name, gender and avatar. Shown on first launch and from Home.</summary>
    public static class ProfileSetup
    {
        static readonly string[] Genders = { "Male", "Female", "Prefer not to say" };

        public static void Show(VisualElement root, bool firstTime, Action onDone = null)
        {
            var progress = ProgressManager.Instance;
            if (progress == null) { onDone?.Invoke(); return; }
            var host = root.Q(className: "screen") ?? root;
            if (host.Q(className: "profile-overlay") != null) return;

            var data = progress.Data;
            string gender = data.profileGender;
            string avatar = string.IsNullOrEmpty(data.profileAvatar) ? Avatars.DefaultId : data.profileAvatar;

            var overlay = UIUtil.Box("profile-overlay", "profile-overlay--hidden");
            UIUtil.ApplySafeArea(overlay);

            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
            };
            scroll.AddToClassList("scroll");
            scroll.AddToClassList("profile-scroll");
            overlay.Add(scroll);

            scroll.Add(UIUtil.Text(firstTime ? "WELCOME TO PCBUILDAR" : "YOUR PROFILE", "kicker"));
            scroll.Add(UIUtil.Text(firstTime ? "Set up your profile" : "Edit profile", "h1"));
            scroll.Add(UIUtil.Text("Add your name and pick an avatar. You can change them any time by tapping your avatar on Home.", "body", "profile-intro"));

            var preview = UIUtil.Box("profile-preview");
            var previewAvatar = UIUtil.Box("avatar-img", "profile-preview-avatar");
            var previewName = UIUtil.Text("", "profile-preview-name");
            preview.Add(previewAvatar);
            preview.Add(previewName);
            scroll.Add(preview);

            scroll.Add(UIUtil.Text("Your name", "field-label"));
            var nameField = new TextField { maxLength = ProgressManager.ProfileNameMaxLength, value = data.profileName ?? "" };
            nameField.AddToClassList("answer-input");
            nameField.textEdition.placeholder = "e.g. Alex";
            scroll.Add(nameField);

            scroll.Add(UIUtil.Text("Gender", "field-label"));
            var genderRow = UIUtil.Box("select-row");
            scroll.Add(genderRow);

            scroll.Add(UIUtil.Text("Choose an avatar", "field-label"));
            var grid = UIUtil.Box("avatar-grid");
            scroll.Add(grid);

            scroll.Add(UIUtil.Text("Appearance", "field-label"));
            scroll.Add(DarkModeRow());
            scroll.Add(UIUtil.Text("Sound", "field-label"));
            scroll.Add(MusicRow());
            Button tourRow = null;
            if (!firstTime)
            {
                scroll.Add(UIUtil.Text("Help", "field-label"));
                tourRow = ActionRow("play", "Take the tour again", $"{GuideTour.GuideName} shows you around Home.");
                scroll.Add(tourRow);
                var resetRow = ActionRow("refresh", "Reset app", "Start over from the beginning, as if the app were new.");
                resetRow.AddToClassList("setting-row--danger");
                resetRow.clicked += () => ConfirmReset(host, () =>
                {
                    onDone = null; // the screen behind is about to be replaced
                    Close(false);
                    ResetApp();
                });
                scroll.Add(resetRow);
            }

            var actions = UIUtil.Box("profile-actions");
            var save = UIUtil.MakeButton("", null, "btn", "btn-primary", "btn-lg");
            save.Add(new Label(firstTime ? "Continue" : "Save"));
            var secondary = UIUtil.MakeButton(firstTime ? "Skip for now" : "Cancel", null, "link-btn", "profile-secondary");
            actions.Add(save);
            actions.Add(secondary);
            overlay.Add(actions);

            void UpdateName()
            {
                string name = nameField.value.Trim();
                previewName.text = name.Length == 0 ? "Your name" : name;
                previewName.EnableInClassList("profile-preview-name--empty", name.Length == 0);
                save.SetEnabled(name.Length > 0);
            }

            void Rebuild()
            {
                Avatars.Apply(previewAvatar, avatar);

                genderRow.Clear();
                foreach (var g in Genders)
                {
                    var option = g;
                    var chip = UIUtil.MakeButton(option, () => { gender = option; Rebuild(); }, "select-chip");
                    chip.EnableInClassList("selected", gender == option);
                    genderRow.Add(chip);
                }

                grid.Clear();
                foreach (var id in Avatars.Ids)
                {
                    var option = id;
                    var cell = UIUtil.MakeButton("", () => { avatar = option; Rebuild(); }, "avatar-option");
                    cell.EnableInClassList("selected", avatar == option);
                    var img = UIUtil.Box("avatar-img");
                    Avatars.Apply(img, option);
                    cell.Add(img);
                    grid.Add(cell);
                }
                UpdateName();
            }

            void Close(bool store)
            {
                if (store) progress.SaveProfile(nameField.value, gender, avatar);
                overlay.AddToClassList("profile-overlay--hidden");
                overlay.schedule.Execute(() =>
                {
                    overlay.RemoveFromHierarchy();
                    onDone?.Invoke();
                }).StartingIn(300);
            }

            nameField.RegisterValueChangedCallback(_ => UpdateName());
            save.clicked += () => { if (nameField.value.Trim().Length > 0) Close(true); };
            // Skipping on first launch still saves, so the setup isn't shown again.
            secondary.clicked += () => Close(firstTime);
            if (tourRow != null)
                tourRow.clicked += () =>
                {
                    onDone += () => GuideTour.Show(root);
                    Close(false);
                };

            Rebuild();
            ScrollFx.Attach(scroll);
            host.Add(overlay);
            overlay.schedule.Execute(() => overlay.RemoveFromClassList("profile-overlay--hidden")).StartingIn(30);
        }

        static VisualElement DarkModeRow() => SwitchRow("Dark mode",
            () => Theme.IsDark, Theme.Set,
            on => on ? "sun" : "moon",
            on => on ? "Dark background, easier on the eyes at night."
                     : "Light background, easier to read in daylight.");

        static VisualElement MusicRow() => SwitchRow("Background music",
            () => AudioManager.Instance == null || AudioManager.Instance.MusicOn,
            on => AudioManager.Instance?.SetMusic(on),
            on => on ? "music" : "music-off",
            on => on ? "A quiet loop while you learn." : "Off. Button and quiz sounds still play.");

        /// <summary>Bottom sheet that asks before wiping everything. <paramref name="onConfirm"/> runs on "Reset everything".</summary>
        static void ConfirmReset(VisualElement host, Action onConfirm)
        {
            var overlay = UIUtil.Box("overlay");
            var backdrop = UIUtil.Box("overlay-backdrop");
            overlay.Add(backdrop);
            var sheet = UIUtil.Box("sheet", "reset-sheet", "sheet--hidden");
            overlay.Add(sheet);

            sheet.Add(UIUtil.Box("sheet-handle"));
            var head = UIUtil.Box("sheet-head");
            var icon = UIUtil.Box("sheet-icon", "reset-icon");
            icon.Add(new LineIcon("warning"));
            head.Add(icon);
            var titles = UIUtil.Box("sheet-titles");
            titles.Add(UIUtil.Text("Reset the app?", "sheet-title"));
            head.Add(titles);
            sheet.Add(head);
            sheet.Add(UIUtil.Text(
                "This clears your progress, lessons, quiz cards, coins, hearts, streak, badges and profile, and puts " +
                "every setting back the way it started. Photos you saved stay on your phone. This can't be undone.",
                "sheet-desc"));

            void Close(Action then)
            {
                sheet.AddToClassList("sheet--hidden");
                overlay.schedule.Execute(() =>
                {
                    overlay.RemoveFromHierarchy();
                    then?.Invoke();
                }).StartingIn(320);
            }

            var confirm = UIUtil.MakeButton("", () => Close(onConfirm), "btn", "btn-lg", "btn-danger", "reset-confirm");
            confirm.Add(new Label("Reset everything"));
            sheet.Add(confirm);
            var cancel = UIUtil.MakeButton("", () => Close(null), "btn", "btn-secondary", "reset-cancel");
            cancel.Add(new Label("Cancel"));
            sheet.Add(cancel);
            backdrop.AddManipulator(new Clickable(() => Close(null)));

            host.Add(overlay);
            sheet.schedule.Execute(() => sheet.RemoveFromClassList("sheet--hidden")).StartingIn(30);
        }

        /// <summary>Wipes the save, puts the look and sound back to their defaults and reopens the Welcome slides.</summary>
        static void ResetApp()
        {
            var progress = ProgressManager.Instance;
            if (progress == null) return;
            Theme.Set(false);                   // repaints open screens while the old save still says dark
            progress.ResetAll();
            AudioManager.Instance?.RefreshMusic();
            GameManager.Instance?.RestartFromWelcome();
        }

        /// <summary>Settings row that does something when tapped (a chevron where a switch row has its switch).</summary>
        static Button ActionRow(string icon, string title, string description)
        {
            var row = UIUtil.MakeButton("", null, "setting-row");
            var iconBox = UIUtil.Box("setting-icon");
            iconBox.Add(new LineIcon(icon));
            row.Add(iconBox);
            var text = UIUtil.Box("setting-text");
            text.Add(UIUtil.Text(title, "setting-title"));
            text.Add(UIUtil.Text(description, "setting-desc"));
            row.Add(text);
            row.Add(new LineIcon("chevron-right", "setting-chevron"));
            return row;
        }

        /// <summary>Settings row with a switch. It applies (and saves) straight away, so Cancel doesn't undo it.</summary>
        static VisualElement SwitchRow(string title, Func<bool> get, Action<bool> set, Func<bool, string> icon, Func<bool, string> describe)
        {
            var row = UIUtil.MakeButton("", null, "setting-row");
            var iconBox = UIUtil.Box("setting-icon");
            var glyph = new LineIcon(icon(get()));
            iconBox.Add(glyph);
            row.Add(iconBox);

            var text = UIUtil.Box("setting-text");
            text.Add(UIUtil.Text(title, "setting-title"));
            var desc = UIUtil.Text(describe(get()), "setting-desc");
            text.Add(desc);
            row.Add(text);

            var sw = UIUtil.Box("switch");
            sw.Add(UIUtil.Box("switch-knob"));
            sw.EnableInClassList("on", get());
            row.Add(sw);

            row.clicked += () =>
            {
                set(!get());
                bool on = get();
                sw.EnableInClassList("on", on);
                glyph.icon = icon(on);
                desc.text = describe(on);
            };
            return row;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using BuildAR.Data;
using BuildAR.Managers;
using BuildAR.Quiz;
using BuildAR.UI;

/// <summary>
/// Two modes on one screen:
///   Memorize (practice, Gizmo-style): spaced-repetition rounds with mixed question types, hearts, XP, coins,
///     hints, lightning questions, speed runs and a round summary. Plays GameManager.PendingDeck.
///   Lesson quiz (the test that completes a lesson): GameManager.PendingLessonQuiz's 20 questions, each asked
///     once and scored, no hearts, hints, reveal or settings, then a results page (pass at 75%).
/// The question cards, answer feedback and question types are shared.
/// Attach next to a UIDocument (QuizScreen.uxml).
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class QuizScreenController : MonoBehaviour
{
    static readonly (QuizStyle style, string title, string desc, string icon)[] Styles =
    {
        (QuizStyle.Mixed, "Mixed", "Every question type: choices, typing, true or false, matching and ordering.", "layers"),
        (QuizStyle.MultipleChoice, "Multiple choice only", "Pick the right answer from the options.", "quiz"),
        (QuizStyle.Typing, "Typing preferred", "Type the answer whenever a card allows it.", "keyboard"),
        (QuizStyle.Flashcards, "Flashcards only", "Flip cards and mark yourself. No hearts, XP or streak.", "cards"),
    };

    VisualElement _root, _body, _questionCard, _answerArea, _assistRow, _feedback, _settings, _settingsSheet, _modal, _summary;
    VisualElement _heartsChip, _fill, _lightningTag, _lightningTrack, _lightningFill;
    Label _prompt, _question, _heartsLabel, _xpLabel, _xpMult, _xpPop, _hintCount, _lightningLabel, _speedRunLabel, _modalTimer;
    Button _hintButton, _revealButton;

    QuizDeck _deck;
    QuizSession _session;
    LessonQuizSession _test;    // set in lesson-quiz mode, instead of _session
    VisualElement _testSummary;
    Action _testPrimary, _testSecondary;
    Question _q;
    bool _resolved, _hintUsed, _optionsHidden, _summaryShown, _summaryPending, _heartsModal;
    float _questionStart = -1f, _lightningEnd, _speedRunEnd, _lightningAtAnswer;
    QuizStyle _styleBefore;
    bool _hideBefore;
    Action _modalPrimary, _modalSecondary, _summaryPrimary, _summarySecondary, _revealOptions, _resumeAfterHearts;
    IVisualElementScheduledItem _tick;

    readonly List<Button> _choices = new List<Button>();
    readonly List<Button> _left = new List<Button>();
    readonly List<Button> _right = new List<Button>();
    readonly List<int> _order = new List<int>();
    TextField _input;
    Button _checkButton;
    Label _hintLabel;
    VisualElement _orderPicked, _orderPool;
    int _matchLeft = -1, _matchRight = -1, _matched, _mistakes;
    bool _matchBusy;

    static ProgressManager Progress => ProgressManager.Instance;

    // ================================================================== lifecycle

    void OnEnable()
    {
        _root = GetComponent<UIDocument>().rootVisualElement;
        UIUtil.PrepareScreen(_root);

        _body = _root.Q("quiz-body");
        _questionCard = _root.Q("question-card");
        _answerArea = _root.Q("answer-area");
        _assistRow = _root.Q("assist-row");
        _feedback = _root.Q("feedback");
        _settings = _root.Q("settings");
        _settingsSheet = _root.Q("settings-sheet");
        _modal = _root.Q("modal");
        _summary = _root.Q("summary");
        _heartsChip = _root.Q("hearts-chip");
        _fill = _root.Q("quiz-fill");
        _lightningTag = _root.Q("tag-lightning");
        _lightningTrack = _root.Q("lightning-track");
        _lightningFill = _root.Q("lightning-fill");
        _prompt = _root.Q<Label>("quiz-prompt");
        _question = _root.Q<Label>("quiz-question-text");
        _heartsLabel = _root.Q<Label>("hearts-label");
        _xpLabel = _root.Q<Label>("xp-label");
        _xpMult = _root.Q<Label>("xp-multiplier");
        _xpPop = _root.Q<Label>("xp-pop");
        _hintCount = _root.Q<Label>("hint-count");
        _lightningLabel = _root.Q<Label>("tag-lightning-label");
        _speedRunLabel = _root.Q<Label>("tag-speedrun-label");
        _modalTimer = _root.Q<Label>("modal-timer");
        _hintButton = _root.Q<Button>("btn-hint");
        _revealButton = _root.Q<Button>("btn-reveal");

        UIUtil.OnClick(_root, "btn-quiz-close", OnClose);
        UIUtil.OnClick(_root, "btn-quiz-settings", OpenSettings);
        UIUtil.OnClick(_root, "btn-settings-done", CloseSettings);
        _root.Q("settings-backdrop").AddManipulator(new Clickable(CloseSettings));
        UIUtil.OnClick(_root, "btn-continue", Continue);
        UIUtil.OnClick(_root, "btn-hint", UseHint);
        UIUtil.OnClick(_root, "btn-reveal", Reveal);
        UIUtil.OnClick(_root, "btn-modal-primary", () => _modalPrimary?.Invoke());
        UIUtil.OnClick(_root, "btn-modal-secondary", () => _modalSecondary?.Invoke());
        UIUtil.OnClick(_root, "btn-see-more", ToggleSeeMore);
        UIUtil.OnClick(_root, "btn-sum-primary", () => _summaryPrimary?.Invoke());
        UIUtil.OnClick(_root, "btn-sum-secondary", () => _summarySecondary?.Invoke());
        UIUtil.OnClick(_root, "btn-sum-done", () => GameManager.Instance?.Back());
        UIUtil.OnClick(_root, "btn-test-primary", () => _testPrimary?.Invoke());
        UIUtil.OnClick(_root, "btn-test-secondary", () => _testSecondary?.Invoke());
        _heartsChip.AddManipulator(UIUtil.Tap(ShowHeartsToast));
        _testSummary = _root.Q("test-summary");

        _tick = _root.schedule.Execute(Tick).Every(100);

        var lesson = GameManager.Instance != null ? GameManager.Instance.PendingLessonQuiz : null;
        if (lesson != null)
        {
            if (Progress == null) { Debug.LogWarning("BuildAR: Lesson quiz opened without managers."); return; }
            StartTest(lesson);
            return;
        }

        _deck = GameManager.Instance != null ? GameManager.Instance.PendingDeck : null;
        if (_deck == null && ComponentDatabase.Instance != null) _deck = QuizDeck.DailyReview(ComponentDatabase.Instance);
        if (_deck == null || Progress == null) { Debug.LogWarning("BuildAR: Quiz opened without managers or a deck."); return; }
        StartRound(false);
    }

    void OnDisable()
    {
        _tick?.Pause();
        if (_summaryPending) { _summaryPending = false; LoadingOverlay.Instance?.Hide(); }
        if (_session != null && !_session.IsFinished && _session.Answered > 0) _session.Finish();
        _session = null;
        _test = null;   // a lesson quiz left part-way isn't scored
    }

    // ================================================================== round flow

    void StartRound(bool speedRun)
    {
        ResetScreen();
        _test = null;
        ApplyMode();

        bool usesHearts = speedRun || Progress.QuizSettings.style != QuizStyle.Flashcards;
        if (usesHearts && !Progress.CanQuiz)
        {
            _session = null;
            ShowOutOfHearts(() => StartRound(speedRun));
            return;
        }

        _session = new QuizSession(_deck, speedRun, Progress);
        _speedRunEnd = Time.realtimeSinceStartup + QuizRewards.SpeedRunSeconds;
        UIUtil.SetVisible(_root.Q("tag-speedrun"), speedRun);
        _fill.EnableInClassList("progress-fill--orange", speedRun);
        _fill.EnableInClassList("progress-fill--green", !speedRun);

        if (_session.RoundCards == 0)
        {
            ShowModal("info", "blue", "No cards yet", "This deck has no quiz cards. Add some to the component's Quiz Questions list.",
                      "Back", () => GameManager.Instance?.Back(), null, null);
            return;
        }
        NextQuestion();
    }

    /// <summary>A lesson quiz: a fresh shuffle of the lesson's questions, from the first one.</summary>
    void StartTest(LessonSO lesson)
    {
        ResetScreen();
        _session = null;
        _test = new LessonQuizSession(lesson, Progress);
        ApplyMode();
        if (_test.Total == 0)
        {
            ShowModal("info", "blue", "No quiz yet", "This lesson has no quiz questions yet.",
                      "Back", () => GameManager.Instance?.Back(), null, null);
            return;
        }
        NextQuestion();
    }

    void ResetScreen()
    {
        _summaryShown = false;
        _summaryPending = false;
        _q = null;
        _resolved = false;
        UIUtil.SetVisible(_summary, false);
        UIUtil.SetVisible(_testSummary, false);
        UIUtil.SetVisible(_settings, false);
        HideModal();
        HideFeedback();
        _answerArea.Clear();
    }

    /// <summary>
    /// A lesson quiz is a test, so the practice extras go: no hearts to lose, no settings to switch to flashcards,
    /// no hints or reveal (hidden per question in ShowQuestion). It shows which question of the 20 this is instead.
    /// </summary>
    void ApplyMode()
    {
        bool test = _test != null;
        UIUtil.SetVisible(_heartsChip, !test);
        UIUtil.SetVisible(_root.Q("btn-quiz-settings"), !test);
        UIUtil.SetVisible(_root.Q("tag-count"), test);
        if (!test) return;
        UIUtil.SetVisible(_root.Q("tag-speedrun"), false);
        _fill.EnableInClassList("progress-fill--orange", false);
        _fill.EnableInClassList("progress-fill--green", true);
    }

    bool UsesHearts => _session != null && (_session.IsSpeedRun || Progress.QuizSettings.style != QuizStyle.Flashcards);

    void NextQuestion()
    {
        HideFeedback();
        if (_test != null)
        {
            if (_test.Next()) ShowQuestion();
            else ShowSummary();
            return;
        }
        if (_session.IsFinished || !_session.Next()) { ShowSummary(); return; }
        if (UsesHearts && !Progress.CanQuiz)
        {
            _q = _session.Current;
            _resolved = false;
            ShowOutOfHearts(ShowQuestion);
            return;
        }
        ShowQuestion();
    }

    void Continue()
    {
        if (!_resolved || (_session == null && _test == null)) return;
        if (_session != null && _session.IsSpeedRun && Time.realtimeSinceStartup >= _speedRunEnd) _session.EndSpeedRun();
        NextQuestion();
    }

    void ShowQuestion()
    {
        _q = _test != null ? _test.Current : _session.Current;
        _resolved = false;
        _hintUsed = false;
        _optionsHidden = false;
        _revealOptions = null;
        _questionStart = -1f;
        _mistakes = 0;
        _matched = 0;
        _matchLeft = _matchRight = -1;
        _matchBusy = false;
        _choices.Clear(); _left.Clear(); _right.Clear(); _order.Clear();
        _input = null; _checkButton = null; _hintLabel = null;
        HideFeedback();
        _answerArea.Clear();

        var info = FormatInfo(_q.format);
        _prompt.text = info.prompt;
        _question.text = _q.card.questionText;
        _root.Q<Label>("tag-format-label").text = info.label;
        _root.Q<LineIcon>("tag-format-icon").icon = info.icon;
        UIUtil.SetVisible(_root.Q("tag-forgotten"), _q.forgotten);
        UIUtil.SetVisible(_lightningTag, _q.lightning);
        UIUtil.SetVisible(_lightningTrack, _q.lightning);
        _lightningTag.RemoveFromClassList("pill--gray");
        _lightningLabel.text = $"{QuizRewards.LightningSeconds:0}s";
        _lightningFill.style.width = Length.Percent(100);
        _questionCard.EnableInClassList("question-card--lightning", _q.lightning);

        switch (_q.format)
        {
            case AskFormat.MultipleChoice: BuildChoice(); break;
            case AskFormat.TrueFalse: BuildTrueFalse(); break;
            case AskFormat.Typing: BuildTyping(); break;
            case AskFormat.Matching: BuildMatching(); break;
            case AskFormat.Ordering: BuildOrdering(); break;
            default: BuildFlashcard(); break;
        }

        UIUtil.SetVisible(_assistRow, _q.format != AskFormat.Flashcard && _test == null);
        if (!_optionsHidden) StartTimer();
        RefreshHud();
        ((ScrollView)_body).scrollOffset = Vector2.zero;
    }

    void StartTimer()
    {
        _questionStart = Time.realtimeSinceStartup;
        _lightningEnd = _questionStart + QuizRewards.LightningSeconds;
    }

    float LightningLeft => _q != null && _q.lightning && _questionStart >= 0f ? Mathf.Max(0f, _lightningEnd - Time.realtimeSinceStartup) : 0f;

    static (string label, string icon, string prompt) FormatInfo(AskFormat f)
    {
        switch (f)
        {
            case AskFormat.TrueFalse: return ("True or false", "check-circle", "TRUE OR FALSE?");
            case AskFormat.Typing: return ("Type the answer", "keyboard", "TYPE THE ANSWER");
            case AskFormat.Matching: return ("Match the pairs", "swap", "MATCH THE PAIRS");
            case AskFormat.Ordering: return ("Put in order", "sort", "PUT THESE IN ORDER");
            case AskFormat.Flashcard: return ("Flashcard", "cards", "FLASHCARD");
            default: return ("Multiple choice", "quiz", "CHOOSE THE RIGHT ANSWER");
        }
    }

    // ================================================================== question types

    void BuildChoice()
    {
        var list = UIUtil.Box("option-list");
        for (int i = 0; i < _q.options.Count; i++)
        {
            int index = i;
            var b = UIUtil.MakeButton("", () => PickOption(index), "option");
            b.Add(UIUtil.Text(((char)('A' + i)).ToString(), "option-letter"));
            b.Add(UIUtil.Text(_q.options[i], "option-text"));
            _choices.Add(b);
            list.Add(b);
        }
        _answerArea.Add(list);
        if (_test != null || !Progress.QuizSettings.hideOptions) return;   // a test always shows its options

        _optionsHidden = true;
        UIUtil.SetVisible(list, false);
        var cover = UIUtil.Box("options-cover");
        cover.Add(UIUtil.Text("Try to recall the answer before you look.", "answer-tip"));
        var show = UIUtil.MakeButton("", null, "btn", "btn-secondary", "icon-first");
        show.Add(new LineIcon("eye"));
        show.Add(new Label("Show options"));
        cover.Add(show);
        _answerArea.Add(cover);

        _revealOptions = () =>
        {
            if (!_optionsHidden) return;
            cover.RemoveFromHierarchy();
            UIUtil.SetVisible(list, true);
            _optionsHidden = false;
            StartTimer();
        };
        show.clicked += () => _revealOptions();
    }

    void BuildTrueFalse()
    {
        var row = UIUtil.Box("tf-row");
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            var b = UIUtil.MakeButton("", () => PickOption(index), "option", "tf-option", i == 0 ? "tf-option--true" : "tf-option--false");
            b.Add(new LineIcon(i == 0 ? "check" : "close"));
            b.Add(UIUtil.Text(_q.options[i], "option-text"));
            _choices.Add(b);
            row.Add(b);
        }
        _answerArea.Add(row);
    }

    void PickOption(int index)
    {
        if (_resolved || _optionsHidden || _choices[index].ClassListContains("eliminated")) return;
        bool correct = index == _q.correctIndex;
        _choices[index].AddToClassList(correct ? "correct" : "incorrect");
        MarkCorrectChoice();
        Grade(correct);
    }

    void MarkCorrectChoice()
    {
        for (int i = 0; i < _choices.Count; i++)
        {
            _choices[i].AddToClassList("locked");
            if (i == _q.correctIndex) _choices[i].AddToClassList("correct");
        }
    }

    void BuildTyping()
    {
        _input = new TextField { maxLength = 80 };
        _input.AddToClassList("answer-input");
        _input.textEdition.placeholder = "Type your answer";
        _input.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
            CheckTyped();
            e.StopPropagation();
        }, TrickleDown.TrickleDown);
        _input.RegisterValueChangedCallback(e => _checkButton?.SetEnabled(!string.IsNullOrWhiteSpace(e.newValue)));
        _answerArea.Add(_input);

        _hintLabel = UIUtil.Text("", "answer-tip", "answer-hint");
        UIUtil.SetVisible(_hintLabel, false);
        _answerArea.Add(_hintLabel);

        _checkButton = UIUtil.MakeButton("", CheckTyped, "btn", "btn-primary", "check-btn");
        _checkButton.Add(new Label("Check"));
        _checkButton.SetEnabled(false);
        _answerArea.Add(_checkButton);

        var field = _input;
        field.schedule.Execute(() => field.Focus()).StartingIn(60);
    }

    void CheckTyped()
    {
        if (_resolved || _input == null || string.IsNullOrWhiteSpace(_input.value)) return;
        var result = AnswerMatcher.Check(_input.value, _q.accepted);
        bool correct = result != AnswerMatcher.Result.Wrong;
        _input.AddToClassList(correct ? "correct" : "incorrect");
        _input.isReadOnly = true;
        _input.Blur();
        UIUtil.SetVisible(_checkButton, false);
        Grade(correct, result == AnswerMatcher.Result.Close);
    }

    void BuildMatching()
    {
        var pairs = _q.card.ValidPairs.ToList();
        var grid = UIUtil.Box("match-grid");
        var left = UIUtil.Box("match-col", "gap-right");
        var right = UIUtil.Box("match-col");
        for (int i = 0; i < pairs.Count; i++)
        {
            int index = i;
            var b = UIUtil.MakeButton("", () => SelectMatch(index, true), "match-item", "match-item--left");
            b.Add(UIUtil.Text(pairs[i].left, "match-text"));
            _left.Add(b);
            left.Add(b);
        }
        for (int i = 0; i < _q.options.Count; i++)
        {
            int index = i;
            var b = UIUtil.MakeButton("", () => SelectMatch(index, false), "match-item");
            b.Add(UIUtil.Text(_q.options[i], "match-text"));
            _right.Add(b);
            right.Add(b);
        }
        grid.Add(left);
        grid.Add(right);
        _answerArea.Add(grid);
        _answerArea.Add(UIUtil.Text("Tap an item on the left, then its match on the right.", "answer-tip"));
    }

    void SelectMatch(int index, bool leftSide)
    {
        var buttons = leftSide ? _left : _right;
        if (_resolved || _matchBusy || buttons[index].ClassListContains("matched")) return;
        if (leftSide) _matchLeft = _matchLeft == index ? -1 : index;
        else _matchRight = _matchRight == index ? -1 : index;
        for (int i = 0; i < _left.Count; i++) _left[i].EnableInClassList("selected", i == _matchLeft);
        for (int i = 0; i < _right.Count; i++) _right[i].EnableInClassList("selected", i == _matchRight);
        if (_matchLeft < 0 || _matchRight < 0) return;

        var l = _left[_matchLeft];
        var r = _right[_matchRight];
        bool ok = _q.card.ValidPairs.ElementAt(_matchLeft).right == _q.options[_matchRight];
        l.RemoveFromClassList("selected");
        r.RemoveFromClassList("selected");
        _matchLeft = _matchRight = -1;

        if (ok)
        {
            SetMatched(l, r);
            return;
        }
        _mistakes++;
        l.AddToClassList("wrong");
        r.AddToClassList("wrong");
        _matchBusy = true;
        _root.schedule.Execute(() =>
        {
            l.RemoveFromClassList("wrong");
            r.RemoveFromClassList("wrong");
            _matchBusy = false;
        }).StartingIn(450);
    }

    void SetMatched(Button l, Button r)
    {
        l.AddToClassList("matched");
        r.AddToClassList("matched");
        _matched++;
        if (_matched == _left.Count) Grade(_mistakes == 0);
    }

    void BuildOrdering()
    {
        _orderPicked = UIUtil.Box("order-picked");
        _orderPool = UIUtil.Box("order-pool");
        _checkButton = UIUtil.MakeButton("", CheckOrder, "btn", "btn-primary", "check-btn");
        _checkButton.Add(new Label("Check"));
        _answerArea.Add(_orderPicked);
        _answerArea.Add(UIUtil.Text("Tap the items in order. Tap a numbered row to put it back.", "answer-tip"));
        _answerArea.Add(_orderPool);
        _answerArea.Add(_checkButton);
        RenderOrder();
    }

    void RenderOrder()
    {
        _orderPicked.Clear();
        _orderPool.Clear();
        if (_order.Count == 0) _orderPicked.Add(UIUtil.Text("Your order appears here", "order-empty"));
        for (int k = 0; k < _order.Count; k++)
        {
            int item = _order[k];
            var row = UIUtil.MakeButton("", () => { if (_resolved) return; _order.Remove(item); RenderOrder(); }, "order-row");
            row.Add(UIUtil.Text((k + 1).ToString(), "order-num"));
            row.Add(UIUtil.Text(_q.options[item], "order-text"));
            _orderPicked.Add(row);
        }
        for (int i = 0; i < _q.options.Count; i++)
        {
            if (_order.Contains(i)) continue;
            int item = i;
            var chip = UIUtil.MakeButton("", () => { if (_resolved) return; _order.Add(item); RenderOrder(); }, "order-chip");
            chip.Add(UIUtil.Text(_q.options[i], "order-text"));
            _orderPool.Add(chip);
        }
        UIUtil.SetVisible(_orderPool, _order.Count < _q.options.Count);
        _checkButton.SetEnabled(_order.Count == _q.options.Count);
    }

    void CheckOrder()
    {
        if (_resolved || _order.Count != _q.options.Count) return;
        var rows = _orderPicked.Query<Button>(className: "order-row").ToList();
        bool correct = true;
        for (int k = 0; k < rows.Count; k++)
        {
            bool right = _q.options[_order[k]] == _q.card.options[k];
            rows[k].AddToClassList(right ? "correct" : "incorrect");
            correct &= right;
        }
        UIUtil.SetVisible(_checkButton, false);
        Grade(correct);
    }

    void BuildFlashcard()
    {
        var card = UIUtil.MakeButton("", null, "flashcard");
        var front = UIUtil.Box("flash-face");
        front.Add(new LineIcon("cards", "flash-icon"));
        front.Add(UIUtil.Text("Think of the answer, then tap to flip", "flash-hint"));
        card.Add(front);

        var marks = UIUtil.Box("flash-marks");
        var no = UIUtil.MakeButton("", () => SelfMark(false), "btn", "btn-lg", "flash-no", "grow", "gap-right", "icon-first");
        no.Add(new LineIcon("close"));
        no.Add(new Label("Didn't know"));
        var yes = UIUtil.MakeButton("", () => SelfMark(true), "btn", "btn-lg", "flash-yes", "grow", "icon-first");
        yes.Add(new LineIcon("check"));
        yes.Add(new Label("Knew it"));
        marks.Add(no);
        marks.Add(yes);
        UIUtil.SetVisible(marks, false);

        var question = _q;
        card.clicked += () =>
        {
            if (card.ClassListContains("flipped") || card.ClassListContains("flipping")) return;
            card.AddToClassList("flipping");
            card.schedule.Execute(() =>
            {
                card.Clear();
                var back = UIUtil.Box("flash-face");
                back.Add(UIUtil.Text("ANSWER", "kicker", "kicker--cyan"));
                back.Add(UIUtil.Text(question.answerText, "flash-answer"));
                if (!string.IsNullOrEmpty(question.card.explanation)) back.Add(UIUtil.Text(question.card.explanation, "flash-explain"));
                card.Add(back);
                card.RemoveFromClassList("flipping");
                card.AddToClassList("flipped");
                UIUtil.SetVisible(marks, true);
            }).StartingIn(160);
        };

        _answerArea.Add(card);
        _answerArea.Add(marks);
        _answerArea.Add(UIUtil.Text("Flashcards are self-marked, so they don't use hearts or earn XP.", "answer-tip"));
    }

    /// <summary>
    /// Flashcards mark themselves ("Knew it"), so they are Memorize practice only. They never appear in a lesson
    /// quiz, and nothing they record counts towards completing a lesson.
    /// </summary>
    void SelfMark(bool knewIt)
    {
        if (_resolved || _test != null) return;
        _resolved = true;
        _session.SelfMark(knewIt);
        NextQuestion();
    }

    // ================================================================== answering

    void Grade(bool correct, bool close = false)
    {
        if (_resolved) return;
        _resolved = true;
        _lightningAtAnswer = LightningLeft;
        float seconds = _questionStart >= 0f ? Time.realtimeSinceStartup - _questionStart : 10f;
        var r = _test != null ? _test.Submit(correct, close) : _session.Submit(correct, seconds, _lightningAtAnswer, close);

        if (correct) AudioManager.Instance?.PlayCorrect();
        else AudioManager.Instance?.PlayIncorrect();
        ShowFeedback(r);
        RefreshHud();
        if (r.xp > 0) PopXp(r.xp);
        if (r.heartLost) Pulse(_heartsChip, "hud-chip--hit");
    }

    void Reveal()
    {
        if (_test != null || _resolved || _q == null || _q.format == AskFormat.Flashcard) return;
        _revealOptions?.Invoke();
        _resolved = true;
        if (_q.format == AskFormat.MultipleChoice || _q.format == AskFormat.TrueFalse) MarkCorrectChoice();
        if (_checkButton != null) UIUtil.SetVisible(_checkButton, false);
        if (_input != null) _input.isReadOnly = true;
        var r = _session.Reveal();
        ShowFeedback(r);
        RefreshHud();
        if (r.heartLost) Pulse(_heartsChip, "hud-chip--hit");
    }

    bool HintWorks => _q != null && (_q.format == AskFormat.MultipleChoice || _q.format == AskFormat.Typing
                                     || _q.format == AskFormat.Matching || _q.format == AskFormat.Ordering);

    void UseHint()
    {
        if (_test != null || _resolved || _hintUsed || !HintWorks) return;
        if (Progress.Data.hints <= 0) { RewardsUI.ShowShop(_root, RefreshHud); return; }
        if (!Progress.TryUseHint()) return;
        _hintUsed = true;

        switch (_q.format)
        {
            case AskFormat.MultipleChoice:
                _revealOptions?.Invoke();
                var wrong = Enumerable.Range(0, _choices.Count).Where(i => i != _q.correctIndex && !_choices[i].ClassListContains("eliminated")).ToList();
                if (wrong.Count > 1) _choices[wrong[UnityEngine.Random.Range(0, wrong.Count)]].AddToClassList("eliminated");
                break;

            case AskFormat.Typing:
                string answer = _q.answerText.Trim();
                _hintLabel.text = answer.Length > 0 ? $"Starts with \"{char.ToUpperInvariant(answer[0])}\" · {answer.Length} characters" : "";
                UIUtil.SetVisible(_hintLabel, true);
                break;

            case AskFormat.Matching:
                var pairs = _q.card.ValidPairs.ToList();
                for (int i = 0; i < _left.Count; i++)
                {
                    if (_left[i].ClassListContains("matched")) continue;
                    int j = Enumerable.Range(0, _right.Count).FirstOrDefault(k => !_right[k].ClassListContains("matched") && _q.options[k] == pairs[i].right);
                    _matchLeft = _matchRight = -1;
                    foreach (var b in _left.Concat(_right)) b.RemoveFromClassList("selected");
                    SetMatched(_left[i], _right[j]);
                    break;
                }
                break;

            case AskFormat.Ordering:
                int prefix = 0;
                while (prefix < _order.Count && _q.options[_order[prefix]] == _q.card.options[prefix]) prefix++;
                _order.RemoveRange(prefix, _order.Count - prefix);
                if (prefix < _q.card.options.Count)
                {
                    string next = _q.card.options[prefix];
                    _order.Add(Enumerable.Range(0, _q.options.Count).First(i => !_order.Contains(i) && _q.options[i] == next));
                }
                RenderOrder();
                break;
        }
        RefreshHud();
    }

    // ================================================================== feedback + HUD

    void ShowFeedback(AnswerResult r)
    {
        string state = r.revealed ? "revealed" : r.correct ? "correct" : "incorrect";
        foreach (var s in new[] { "correct", "incorrect", "revealed" }) _feedback.EnableInClassList("feedback-sheet--" + s, s == state);
        _root.Q<LineIcon>("fb-icon").icon = r.revealed ? "eye" : r.correct ? "check" : "close";
        _root.Q<Label>("fb-title").text = r.revealed ? "Here's the answer" : r.correct ? (r.close ? "Correct, but check the spelling" : Praise(r.combo)) : "Not quite";

        string sub;
        if (_test != null) sub = $"{_test.Correct} of {_test.Answered} right so far";
        else if (r.revealed) sub = (r.heartLost ? "−1 heart. " : "") + "This card will come back soon.";
        else if (r.correct)
            sub = r.multiplier > 1 ? $"{r.combo} in a row · {r.multiplier}x XP"
                : _q.lightning && _lightningAtAnswer > 0f ? "Lightning bonus!"
                : r.combo >= 2 ? $"{r.combo} in a row" : "";
        else
            sub = (_session.IsSpeedRun ? "" : "You'll see this card again this round. ") + (r.heartLost ? "−1 heart" : "");
        var subLabel = _root.Q<Label>("fb-sub");
        subLabel.text = sub.Trim();
        UIUtil.SetVisible(subLabel, subLabel.text.Length > 0);

        _root.Q<Label>("fb-xp").text = $"+{r.xp} XP";
        UIUtil.SetVisible(_root.Q("fb-xp"), r.xp > 0);
        UIUtil.SetVisible(_root.Q("fb-coin"), r.coins > 0);

        var answer = _root.Q<Label>("fb-answer");
        bool multiLine = _q.format == AskFormat.Matching || _q.format == AskFormat.Ordering;
        answer.text = (multiLine ? "Correct answer:\n" : "Answer: ") + r.answerText;
        UIUtil.SetVisible(answer, !r.correct || r.close);

        var explanation = _root.Q<Label>("fb-explanation");
        explanation.text = _q.card.explanation;
        UIUtil.SetVisible(explanation, !string.IsNullOrEmpty(_q.card.explanation));

        _root.Q<Label>("btn-continue-label").text =
            _test != null ? (_test.IsLast ? "See my score" : "Next question")
            : r.outOfHearts && UsesHearts ? "Continue (out of hearts)" : "Continue";
        _feedback.RemoveFromClassList("sheet--hidden");
        _body.AddToClassList("quiz-body--feedback");
    }

    void HideFeedback()
    {
        _feedback.AddToClassList("sheet--hidden");
        _body.RemoveFromClassList("quiz-body--feedback");
    }

    static string Praise(int combo) =>
        combo >= 8 ? "Unstoppable!" : combo >= 4 ? "On fire!" : new[] { "Nice!", "Correct!", "Great job!" }[combo % 3];

    void RefreshHud()
    {
        if (_test != null && Progress != null)
        {
            _xpLabel.text = $"{_test.Xp} XP";
            UIUtil.SetVisible(_xpMult, false);
            _fill.style.width = Length.Percent(100f * _test.Answered / Mathf.Max(1, _test.Total));
            _root.Q<Label>("tag-count-label").text = $"{_test.Number} of {_test.Total}";
            return;
        }
        if (_session == null || Progress == null) return;
        int hearts = Progress.Hearts;
        bool super = hearts == 0 && Progress.SuperHearts > 0;
        _heartsLabel.text = (super ? Progress.SuperHearts : hearts).ToString();
        _heartsChip.EnableInClassList("hud-chip--super", super);
        _heartsChip.EnableInClassList("hud-chip--off", !UsesHearts);

        _xpLabel.text = $"{_session.Xp} XP";
        int multiplier = QuizRewards.Multiplier(_session.Combo);
        _xpMult.text = $"{multiplier}x";
        UIUtil.SetVisible(_xpMult, multiplier > 1);
        if (!_session.IsSpeedRun) _fill.style.width = Length.Percent(100f * _session.Progress01);

        _hintCount.text = Progress.Data.hints.ToString();
        _hintButton.SetEnabled(!_resolved && !_hintUsed && HintWorks);
        _revealButton.SetEnabled(!_resolved && _q != null);
    }

    void PopXp(int xp)
    {
        _xpPop.text = $"+{xp} XP";
        _xpPop.RemoveFromClassList("xp-pop--show");
        _xpPop.schedule.Execute(() => _xpPop.AddToClassList("xp-pop--show")).StartingIn(20);
        _xpPop.schedule.Execute(() => _xpPop.RemoveFromClassList("xp-pop--show")).StartingIn(800);
        Pulse(_root.Q("xp-counter"), "xp-counter--hit");
    }

    static void Pulse(VisualElement el, string cls)
    {
        el.AddToClassList(cls);
        el.schedule.Execute(() => el.RemoveFromClassList(cls)).StartingIn(260);
    }

    void ShowHeartsToast()
    {
        if (Progress == null) return;
        int hearts = Progress.Hearts;
        string body = hearts >= QuizRewards.MaxHearts ? "Full hearts. You lose one for each wrong answer or reveal."
            : $"{hearts} left · full again in {RewardsUI.Countdown(Progress.HeartsRefillIn)}"
              + (Progress.SuperHearts > 0 ? $" · {Progress.SuperHearts} Super Hearts" : "");
        UIUtil.Toast(_root, "Hearts", body, "heart");
    }

    void Tick()
    {
        if (_heartsModal) UpdateHeartsModal();
        if (_session == null || _summaryShown) return;

        if (_q != null && _q.lightning && !_resolved && _questionStart >= 0f)
        {
            float left = LightningLeft;
            _lightningLabel.text = left > 0f ? $"{Mathf.CeilToInt(left)}s" : "No bonus";
            _lightningTag.EnableInClassList("pill--gray", left <= 0f);
            _lightningFill.style.width = Length.Percent(100f * left / QuizRewards.LightningSeconds);
        }

        if (_session.IsSpeedRun && !_session.IsFinished)
        {
            float left = Mathf.Max(0f, _speedRunEnd - Time.realtimeSinceStartup);
            _speedRunLabel.text = RewardsUI.Countdown(TimeSpan.FromSeconds(Mathf.Ceil(left)));
            _fill.style.width = Length.Percent(100f * left / QuizRewards.SpeedRunSeconds);
            if (left <= 0f && !_resolved && _modal.resolvedStyle.display == DisplayStyle.None)
            {
                _session.EndSpeedRun();
                ShowSummary();
            }
        }
    }

    // ================================================================== settings

    void OpenSettings()
    {
        if (Progress == null) return;
        _styleBefore = Progress.QuizSettings.style;
        _hideBefore = Progress.QuizSettings.hideOptions;
        BuildSettings();
        UIUtil.SetVisible(_settings, true);
        _settingsSheet.schedule.Execute(() => _settingsSheet.RemoveFromClassList("sheet--hidden")).StartingIn(20);
    }

    void BuildSettings()
    {
        var settings = Progress.QuizSettings;
        var list = _root.Q("style-list");
        list.Clear();
        foreach (var s in Styles)
        {
            var style = s.style;
            var row = UIUtil.MakeButton("", () => { settings.style = style; BuildSettings(); }, "setting-row");
            row.EnableInClassList("selected", settings.style == style);
            var icon = UIUtil.Box("setting-icon");
            icon.Add(new LineIcon(s.icon));
            row.Add(icon);
            var text = UIUtil.Box("setting-text");
            text.Add(UIUtil.Text(s.title, "setting-title"));
            text.Add(UIUtil.Text(s.desc, "setting-desc"));
            row.Add(text);
            row.Add(UIUtil.Box("radio"));
            list.Add(row);
        }

        var toggles = _root.Q("toggle-list");
        toggles.Clear();
        toggles.Add(ToggleRow("Hide options initially", "Recall the answer before the choices appear.", settings.hideOptions, v => settings.hideOptions = v));
        toggles.Add(ToggleRow("Lightning rounds", "Now and then a question gets a 10-second timer for bonus XP.", settings.lightningRounds, v => settings.lightningRounds = v));
    }

    static VisualElement ToggleRow(string title, string desc, bool on, Action<bool> set)
    {
        var row = UIUtil.MakeButton("", null, "setting-row");
        var text = UIUtil.Box("setting-text");
        text.Add(UIUtil.Text(title, "setting-title"));
        text.Add(UIUtil.Text(desc, "setting-desc"));
        row.Add(text);
        var sw = UIUtil.Box("switch");
        sw.Add(UIUtil.Box("switch-knob"));
        sw.EnableInClassList("on", on);
        row.Add(sw);
        row.clicked += () =>
        {
            bool value = !sw.ClassListContains("on");
            sw.EnableInClassList("on", value);
            set(value);
        };
        return row;
    }

    void CloseSettings()
    {
        if (Progress == null) return;
        Progress.SaveQuizSettings();
        _settingsSheet.AddToClassList("sheet--hidden");
        _settings.schedule.Execute(() => UIUtil.SetVisible(_settings, false)).StartingIn(300);

        var s = Progress.QuizSettings;
        bool changed = s.style != _styleBefore || s.hideOptions != _hideBefore;
        if (changed && _session != null && _q != null && !_resolved && !_summaryShown)
        {
            _session.Rechoose();
            ShowQuestion();
        }
        else RefreshHud();
    }

    // ================================================================== modals

    void ShowModal(string icon, string tone, string title, string body, string primary, Action onPrimary, string secondary, Action onSecondary)
    {
        _root.Q<LineIcon>("modal-icon").icon = icon;
        var box = _root.Q("modal-icon-box");
        foreach (var t in new[] { "red", "blue", "orange" }) box.EnableInClassList("modal-icon--" + t, t == tone);
        _root.Q<Label>("modal-title").text = title;
        _root.Q<Label>("modal-body").text = body;
        _modalTimer.text = "";
        UIUtil.SetVisible(_modalTimer, false);
        _root.Q<Label>("btn-modal-primary-label").text = primary;
        _modalPrimary = onPrimary;
        UIUtil.SetVisible(_root.Q("btn-modal-secondary"), secondary != null);
        if (secondary != null) _root.Q<Label>("btn-modal-secondary-label").text = secondary;
        _modalSecondary = onSecondary;
        UIUtil.SetVisible(_modal, true);
    }

    void HideModal()
    {
        UIUtil.SetVisible(_modal, false);
        _heartsModal = false;
    }

    void ShowOutOfHearts(Action resume)
    {
        _resumeAfterHearts = resume;
        bool canSwitch = _session == null || !_session.IsSpeedRun;
        ShowModal("heart", "red", "You're out of hearts",
                  "Hearts refill 10 minutes after you lose your last one. Super Hearts you already own kick in automatically. Flashcards don't use hearts.",
                  _session != null && _session.Answered > 0 ? "End round" : "Back",
                  () => { HideModal(); if (_session != null && _session.Answered > 0) ShowSummary(); else GameManager.Instance?.Back(); },
                  canSwitch ? "Study with flashcards" : null, SwitchToFlashcards);
        UIUtil.SetVisible(_modalTimer, true);
        _heartsModal = true;
        UpdateHeartsModal();
    }

    void UpdateHeartsModal()
    {
        if (!Progress.CanQuiz)
        {
            _modalTimer.text = "Full hearts in " + RewardsUI.Countdown(Progress.HeartsRefillIn);
            return;
        }
        _heartsModal = false;
        _modalTimer.text = "Hearts refilled!";
        _root.Q<Label>("btn-modal-primary-label").text = "Keep going";
        var resume = _resumeAfterHearts;
        _modalPrimary = () => { HideModal(); resume?.Invoke(); };
    }

    void SwitchToFlashcards()
    {
        if (_test != null) return;   // a lesson quiz is a test: no switching to self-marked cards
        Progress.QuizSettings.style = QuizStyle.Flashcards;
        Progress.SaveQuizSettings();
        HideModal();
        if (_session == null) { StartRound(false); return; }
        _session.Rechoose();
        ShowQuestion();
    }

    void OnClose()
    {
        if (_test != null)
        {
            if (_summaryShown || _test.Answered == 0) { GameManager.Instance?.Back(); return; }
            ShowModal("warning", "orange", "Leave the quiz?",
                      $"Only a finished quiz gets a score. If you leave now, you'll start again from question 1 next time.",
                      "Keep going", HideModal, "Leave", () => { HideModal(); GameManager.Instance?.Back(); });
            return;
        }
        if (_session == null || _summaryShown || _session.Answered == 0) { GameManager.Instance?.Back(); return; }
        ShowModal("warning", "orange", "Quit this round?", $"You've earned {_session.Xp} XP so far, and it's already saved.",
                  "Keep going", HideModal, "Quit", () => { HideModal(); GameManager.Instance?.Back(); });
    }

    // ================================================================== summary

    /// <summary>Ends the round: a short loading animation, then the summary with confetti.</summary>
    void ShowSummary()
    {
        if (_test != null) { ShowTestResults(); return; }
        if (_session == null || _summaryPending || _summaryShown) return;
        _summaryPending = true;
        HideFeedback();
        HideModal();
        var report = _session.Finish();
        LoadingOverlay.Instance?.Show(report.speedRun ? "Counting your speed run…" : "Counting up your round…");

        _root.schedule.Execute(() =>
        {
            _summaryPending = false;
            DisplaySummary(report);
            LoadingOverlay.Instance?.Hide();
            if (report.answered == 0 && report.cardsLevelledUp == 0) return;
            _root.schedule.Execute(() => Confetti.Burst(_root)).StartingIn(150);
        }).StartingIn(900);
    }

    void DisplaySummary(RoundReport report)
    {
        _summaryShown = true;
        UIUtil.SetVisible(_summary, true);
        _root.Q<ScrollView>(className: "summary-scroll").scrollOffset = Vector2.zero;

        bool extended = report.streakAfter > report.streakBefore;
        _root.Q<LineIcon>("summary-icon").icon = report.speedRun ? "timer" : extended ? "flame" : "trophy";
        _root.Q<Label>("summary-title").text = report.speedRun ? "Time's up!" : "Round complete!";
        _root.Q<Label>("summary-sub").text = report.speedRun ? $"{_deck.Title} · speed run" : _deck.Title;

        _root.Q<Label>("sum-xp-label").text = $"+{report.xp} XP";
        _root.Q<Label>("sum-progress-label").text = report.speedRun
            ? $"{report.correct}/{report.answered} correct"
            : $"+{report.cardsLevelledUp} card{(report.cardsLevelledUp == 1 ? "" : "s")} leveled up";

        int mastery = Mathf.RoundToInt(report.masteryAfter01 * 100f);
        int delta = mastery - Mathf.RoundToInt(report.masteryBefore01 * 100f);
        _root.Q<Label>("sum-mastery").text = $"{mastery}%";
        _root.Q("sum-mastery-fill").style.width = Length.Percent(mastery);
        _root.Q<Label>("sum-mastery-note").text =
            mastery >= ProgressManager.MasteryScore && _deck.ComponentId != null ? $"Mastered! {(delta > 0 ? $"+{delta}% this round." : "")}"
            : delta > 0 ? $"+{delta}% this round. Come back when cards are due to grow it further."
            : report.speedRun ? "Speed runs are practice, so they don't change mastery."
            : "Practicing early doesn't raise mastery. Come back when cards are due.";

        _root.Q<Label>("sum-streak").text = $"{report.streakAfter} day streak";
        UIUtil.SetVisible(_root.Q("sum-streak-tag"), extended);
        var weekHost = _root.Q("sum-week-host");
        weekHost.Clear();
        weekHost.Add(RewardsUI.WeekStrip(Progress, true));
        _root.Q<Label>("sum-streak-note").text = StreakNote(Progress.QuestionsToday);

        BuildSummaryDetails(report);

        if (report.speedRun)
        {
            SetSummaryButtons("Speed run again", () => StartRound(true), "Memorize", "layers", () => StartRound(false));
        }
        else
        {
            SetSummaryButtons("Keep going", () => StartRound(false), "Speed run", "timer", () => StartRound(true));
        }
        var heartsNote = _root.Q<Label>("sum-hearts-note");
        heartsNote.text = Progress.CanQuiz ? "" : $"Out of hearts · full in {RewardsUI.Countdown(Progress.HeartsRefillIn)}";
        UIUtil.SetVisible(heartsNote, heartsNote.text.Length > 0);
    }

    static string StreakNote(int questionsToday)
    {
        int tier = QuizRewards.TierIndex(questionsToday);
        if (tier < 0)
        {
            int more = QuizRewards.StreakDailyMinimum - questionsToday;
            return $"Answer {more} more question{(more == 1 ? "" : "s")} today to keep your streak.";
        }
        var reached = QuizRewards.StreakTiers[tier];
        if (tier == QuizRewards.StreakTiers.Length - 1) return $"{reached.name} day. That's the top tier!";
        var next = QuizRewards.StreakTiers[tier + 1];
        return $"{reached.name} day · {next.questions - questionsToday} more questions for {next.name}";
    }

    void SetSummaryButtons(string primary, Action onPrimary, string secondary, string secondaryIcon, Action onSecondary)
    {
        _root.Q<Label>("btn-sum-primary-label").text = primary;
        _summaryPrimary = onPrimary;
        _root.Q<Label>("btn-sum-secondary-label").text = secondary;
        _root.Q<LineIcon>("btn-sum-secondary-icon").icon = secondaryIcon;
        _summarySecondary = onSecondary;
    }

    void BuildSummaryDetails(RoundReport report)
    {
        UIUtil.SetVisible(_root.Q("sum-more"), false);
        _root.Q<Label>("btn-see-more-label").text = "See more";

        var stats = _root.Q("sum-stats");
        stats.Clear();
        int accuracy = report.firstTryAnswered == 0 ? 0 : Mathf.RoundToInt(100f * report.firstTryCorrect / report.firstTryAnswered);
        void Stat(string label, string value)
        {
            var row = UIUtil.Box("summary-stat");
            row.Add(UIUtil.Text(label, "summary-stat-label"));
            row.Add(UIUtil.Text(value, "summary-stat-value"));
            stats.Add(row);
        }
        Stat("Questions answered", report.answered.ToString());
        Stat("Right first time", report.firstTryAnswered == 0 ? "—" : $"{accuracy}%");
        Stat("Best combo", report.bestCombo.ToString());
        Stat("Coins earned", report.coins.ToString());
        Stat("Hearts lost", report.heartsLost.ToString());

        var review = _root.Q("sum-review");
        review.Clear();
        foreach (var card in report.forgotten.Take(5))
        {
            var row = UIUtil.Box("summary-review-row");
            row.Add(new LineIcon("refresh"));
            row.Add(UIUtil.Text(card.questionText, "summary-review-text"));
            review.Add(row);
        }
        UIUtil.SetVisible(_root.Q("sum-review-title"), report.forgotten.Count > 0);
    }

    // ================================================================== lesson quiz results

    void ShowTestResults()
    {
        if (_summaryPending || _summaryShown) return;
        _summaryPending = true;
        HideFeedback();
        HideModal();
        var report = _test.Finish();
        LoadingOverlay.Instance?.Show("Checking your answers…");

        _root.schedule.Execute(() =>
        {
            _summaryPending = false;
            DisplayTestResults(report);
            LoadingOverlay.Instance?.Hide();
            if (report.passed) _root.schedule.Execute(() => Confetti.Burst(_root)).StartingIn(150);
        }).StartingIn(700);
    }

    /// <summary>
    /// Score against the pass mark, then every missed question with its answer and explanation, so a retake is a
    /// second chance to learn rather than a guess. Pass: back to the lesson to complete it. Not yet: try again.
    /// </summary>
    void DisplayTestResults(LessonQuizReport report)
    {
        _summaryShown = true;
        var lesson = _test.Lesson;
        UIUtil.SetVisible(_testSummary, true);
        _testSummary.Q<ScrollView>().scrollOffset = Vector2.zero;

        _root.Q<LineIcon>("test-icon").icon = report.passed ? "trophy" : "refresh";
        _root.Q<Label>("test-title").text = report.passed ? (report.firstPass ? "You passed!" : "Passed again!") : "Not quite yet";
        _root.Q<Label>("test-sub").text = lesson.title;
        _root.Q<Label>("test-score").text = $"{report.correct} / {report.total}";
        _root.Q<Label>("test-percent").text = $"{report.percent}%";
        _root.Q<Label>("test-xp").text = $"+{report.xp} XP";
        _root.Q("test-fill").style.width = Length.Percent(report.percent);
        _root.Q("test-pass-mark").style.left = Length.Percent(LessonQuizSession.PassPercent);

        bool lessonDone = Progress.Data.completedLessonIds.Contains(lesson.lessonId);
        string mark = $"{report.passMark} of {report.total} ({LessonQuizSession.PassPercent}%)";
        _root.Q<Label>("test-note").text = report.passed
            ? $"The pass mark is {mark}. " + (lessonDone ? "Retakes only ever keep your best score." : "Go back to the lesson to complete it.")
            : $"You need {mark} to pass. Read through the answers below, then try again."
              + (report.bestBefore > report.percent ? $" Your best is still {report.bestBefore}%." : "");

        var missed = _root.Q("test-missed");
        missed.Clear();
        foreach (var a in report.missed)
        {
            bool multiLine = a.card.questionType == QuizQuestionSO.QuestionType.Matching
                             || a.card.questionType == QuizQuestionSO.QuestionType.Ordering;
            var row = UIUtil.Box("test-missed-row");
            row.Add(UIUtil.Text(a.card.questionText, "test-missed-q"));
            row.Add(UIUtil.Text((multiLine ? "Answer:\n" : "Answer: ") + a.answerText, "test-missed-a"));
            if (!string.IsNullOrEmpty(a.card.explanation)) row.Add(UIUtil.Text(a.card.explanation, "test-missed-why"));
            missed.Add(row);
        }
        UIUtil.SetVisible(_root.Q("test-missed-card"), report.missed.Count > 0);
        _root.Q<Label>("test-missed-title").text = report.missed.Count == 1 ? "1 question to review" : $"{report.missed.Count} questions to review";

        if (report.passed)
            SetTestButtons("Back to lesson", () => GameManager.Instance?.Back(), "Retake quiz", () => StartTest(lesson));
        else
            SetTestButtons("Try again", () => StartTest(lesson), "Back to lesson", () => GameManager.Instance?.Back());
    }

    void SetTestButtons(string primary, Action onPrimary, string secondary, Action onSecondary)
    {
        _root.Q<Label>("btn-test-primary-label").text = primary;
        _testPrimary = onPrimary;
        _root.Q<Label>("btn-test-secondary-label").text = secondary;
        _testSecondary = onSecondary;
    }

    void ToggleSeeMore()
    {
        var more = _root.Q("sum-more");
        bool show = more.resolvedStyle.display == DisplayStyle.None;
        UIUtil.SetVisible(more, show);
        _root.Q<Label>("btn-see-more-label").text = show ? "See less" : "See more";
    }
}

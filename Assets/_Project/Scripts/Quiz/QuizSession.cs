using System;
using System.Collections.Generic;
using System.Linq;
using BuildAR.Data;
using BuildAR.Managers;

namespace BuildAR.Quiz
{
    /// <summary>One way a card is asked, with its shuffled options.</summary>
    public sealed class Question
    {
        public QuizQuestionSO card;
        public AskFormat format;
        /// <summary>MultipleChoice/TrueFalse: options. Matching: shuffled right column. Ordering: shuffled items.</summary>
        public List<string> options = new List<string>();
        public int correctIndex;
        public List<string> accepted = new List<string>();
        public string answerText;
        public bool forgotten;
        public bool lightning;
        /// <summary>0 the first time the card is asked this round.</summary>
        public int attempt;
    }

    public struct AnswerResult
    {
        public bool correct, close, revealed, heartLost, outOfHearts;
        public int xp, multiplier, coins, combo;
        public string answerText;
    }

    public struct RoundReport
    {
        public bool speedRun, answeredAny;
        public int xp, coins, answered, correct, firstTryCorrect, firstTryAnswered, bestCombo, heartsLost, cardsLevelledUp;
        public float masteryBefore01, masteryAfter01;
        public int streakBefore, streakAfter;
        public List<QuizQuestionSO> forgotten;
    }

    /// <summary>
    /// A Memorize round: due cards first, then new ones, up to 10. A missed card comes back two questions
    /// later ("Forgotten") until it's answered correctly. A speed run cycles the deck until the timer ends.
    /// </summary>
    public sealed class QuizSession
    {
        public const int RoundLength = 10;
        const double LightningChance = 0.2;

        readonly ProgressManager _progress;
        readonly Random _rng;
        readonly List<QuizQuestionSO> _cards;
        readonly List<Question> _queue = new List<Question>();
        readonly HashSet<QuizQuestionSO> _done = new HashSet<QuizQuestionSO>();
        readonly Dictionary<QuizQuestionSO, int> _asked = new Dictionary<QuizQuestionSO, int>();
        readonly Dictionary<QuizQuestionSO, int> _levelBefore = new Dictionary<QuizQuestionSO, int>();
        readonly List<QuizQuestionSO> _forgotten = new List<QuizQuestionSO>();
        readonly float _masteryBefore;
        readonly int _streakBefore;
        int _sinceLightning;

        public QuizDeck Deck { get; }
        public bool IsSpeedRun { get; }
        public Question Current { get; private set; }
        public bool IsFinished { get; private set; }
        public int RoundCards { get; }
        public float Progress01 => RoundCards == 0 ? 1f : (float)_done.Count / RoundCards;

        public int Xp { get; private set; }
        public int Coins { get; private set; }
        public int Answered { get; private set; }
        public int CorrectAnswers { get; private set; }
        public int FirstTryAnswered { get; private set; }
        public int FirstTryCorrect { get; private set; }
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        public int HeartsLost { get; private set; }

        public QuizStyle Style => _progress.QuizSettings.style;

        public QuizSession(QuizDeck deck, bool speedRun, ProgressManager progress, Random rng = null)
        {
            Deck = deck;
            IsSpeedRun = speedRun;
            _progress = progress;
            _rng = rng ?? new Random();
            _cards = deck.Cards.ToList();
            foreach (var c in _cards) _levelBefore[c] = progress.CardLevel(c);
            _masteryBefore = progress.DeckMastery01(_cards);
            _streakBefore = progress.CurrentStreak;

            if (speedRun)
            {
                RoundCards = _cards.Count;
                RefillSpeedRun();
            }
            else
            {
                var pick = PickRound();
                RoundCards = pick.Count;
                foreach (var c in pick) _queue.Add(new Question { card = c });
            }
        }

        List<QuizQuestionSO> PickRound()
        {
            var now = DateTime.UtcNow;
            var states = _cards.Select(c => (card: c, state: _progress.FindCard(c.Key))).ToList();
            var due = states.Where(x => !SpacedRepetition.IsNew(x.state) && SpacedRepetition.IsDue(x.state, now)).OrderBy(x => x.state.dueTicks).Select(x => x.card);
            var fresh = states.Where(x => SpacedRepetition.IsNew(x.state)).Select(x => x.card);
            var pick = due.Concat(fresh).Take(RoundLength).ToList();
            if (pick.Count < RoundLength)
                pick.AddRange(states.Where(x => !pick.Contains(x.card)).OrderBy(x => x.state.dueTicks).Select(x => x.card).Take(RoundLength - pick.Count));
            Shuffle(pick);
            return pick;
        }

        void RefillSpeedRun()
        {
            var batch = _cards.Where(c => c.questionType != QuizQuestionSO.QuestionType.Matching && c.questionType != QuizQuestionSO.QuestionType.Ordering).ToList();
            if (batch.Count == 0) batch = _cards.ToList();
            Shuffle(batch);
            if (Current != null && batch.Count > 1 && batch[0] == Current.card) { batch.RemoveAt(0); batch.Add(Current.card); }
            foreach (var c in batch) _queue.Add(new Question { card = c });
        }

        /// <summary>Moves to the next question. Returns false when the round is over.</summary>
        public bool Next()
        {
            if (IsFinished) return false;
            if (IsSpeedRun && _queue.Count == 0 && _cards.Count > 0) RefillSpeedRun();
            if (_queue.Count == 0) { Current = null; IsFinished = true; return false; }

            var q = _queue[0];
            _queue.RemoveAt(0);
            _asked.TryGetValue(q.card, out int asked);
            q.attempt = asked;
            _asked[q.card] = asked + 1;
            Current = q;
            Resolve(q);

            _sinceLightning++;
            q.lightning = _progress.QuizSettings.lightningRounds && q.format != AskFormat.Flashcard
                          && q.format != AskFormat.Matching && q.format != AskFormat.Ordering
                          && Answered > 0 && _sinceLightning >= 3 && _rng.NextDouble() < LightningChance;
            if (q.lightning) _sinceLightning = 0;
            return true;
        }

        /// <summary>Re-picks how the current question is asked (after the style setting changes).</summary>
        public void Rechoose()
        {
            if (Current == null) return;
            Resolve(Current);
            if (Current.format == AskFormat.Flashcard) Current.lightning = false;
        }

        // ------------------------------------------------------------------ formats

        void Resolve(Question q)
        {
            var c = q.card;
            var style = IsSpeedRun && Style == QuizStyle.Flashcards ? QuizStyle.Mixed : Style;
            int level = _progress.CardLevel(c);
            q.options = new List<string>();
            q.accepted = new List<string>();
            q.answerText = c.AnswerText;

            if (style == QuizStyle.Flashcards) { q.format = AskFormat.Flashcard; return; }

            switch (c.questionType)
            {
                case QuizQuestionSO.QuestionType.MultipleChoice:
                    bool type = CanType(c) && (style == QuizStyle.Typing || (style == QuizStyle.Mixed && level >= 2 && _rng.NextDouble() < 0.5));
                    if (type) AskTyping(q, c.options[c.correctOptionIndex], c.acceptedAnswers);
                    else AskChoice(q, c.options, c.options[c.correctOptionIndex]);
                    break;

                case QuizQuestionSO.QuestionType.Typing:
                    var wrong = Distractors(c);
                    bool choice = wrong.Count > 0 && (style == QuizStyle.MultipleChoice || (style == QuizStyle.Mixed && (level == 0 || _rng.NextDouble() < 0.3)));
                    if (choice) AskChoice(q, wrong.Append(c.answer).ToList(), c.answer);
                    else AskTyping(q, c.answer, c.acceptedAnswers);
                    break;

                case QuizQuestionSO.QuestionType.TrueFalse:
                    q.format = AskFormat.TrueFalse;
                    q.options = new List<string> { "True", "False" };
                    q.correctIndex = c.statementIsTrue ? 0 : 1;
                    break;

                case QuizQuestionSO.QuestionType.Matching:
                    q.format = AskFormat.Matching;
                    q.options = c.ValidPairs.Select(p => p.right).ToList();
                    ShuffleAwayFromOriginal(q.options);
                    break;

                default:
                    q.format = AskFormat.Ordering;
                    q.options = c.options.ToList();
                    ShuffleAwayFromOriginal(q.options);
                    break;
            }
        }

        void AskChoice(Question q, List<string> options, string correct)
        {
            q.format = AskFormat.MultipleChoice;
            q.options = options.ToList();
            Shuffle(q.options);
            q.correctIndex = q.options.IndexOf(correct);
            q.answerText = correct;
        }

        static void AskTyping(Question q, string answer, IEnumerable<string> alternatives)
        {
            q.format = AskFormat.Typing;
            q.accepted = new List<string> { answer };
            q.accepted.AddRange(alternatives.Where(a => !string.IsNullOrWhiteSpace(a)));
            q.answerText = answer;
        }

        /// <summary>Short multiple-choice answers ("SSD", "EPS 8-pin") can also be typed.</summary>
        static bool CanType(QuizQuestionSO c)
        {
            string a = c.options[c.correctOptionIndex];
            return a.Length <= 24 && a.Split(' ').Length <= 3 && a.IndexOfAny(new[] { '—', '–', ',', ';', ':', '(', '?', '!' }) < 0;
        }

        List<string> Distractors(QuizQuestionSO c)
        {
            var wrong = c.distractors.Where(d => !string.IsNullOrWhiteSpace(d) && !Same(d, c.answer)).Distinct().ToList();
            if (wrong.Count < 3)
                foreach (var other in _cards)
                    if (other != c && other.questionType == QuizQuestionSO.QuestionType.Typing && !Same(other.answer, c.answer) && !wrong.Any(w => Same(w, other.answer)))
                        wrong.Add(other.answer);
            Shuffle(wrong);
            return wrong.Take(3).ToList();
        }

        static bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------ answers

        /// <summary>A tested answer (multiple choice, true/false, typing, matching, ordering).</summary>
        public AnswerResult Submit(bool correct, float seconds, float lightningSecondsLeft, bool close = false)
        {
            var q = Current;
            var r = new AnswerResult { correct = correct, close = close, answerText = q.answerText, multiplier = 1 };
            bool first = q.attempt == 0;
            var now = DateTime.UtcNow;

            Answered++;
            if (first) FirstTryAnswered++;
            _progress.RecordQuestionAnswered();
            if (!IsSpeedRun) UpdateSchedule(q, correct, now);

            if (correct)
            {
                CorrectAnswers++;
                if (first) FirstTryCorrect++;
                Combo++;
                BestCombo = Math.Max(BestCombo, Combo);
                r.multiplier = QuizRewards.Multiplier(Combo);
                r.xp = QuizRewards.XpFor(seconds, q.lightning ? lightningSecondsLeft : 0f, first, r.multiplier);
                r.coins = first && !IsSpeedRun ? 1 : 0;
                Xp += r.xp;
                Coins += r.coins;
                _progress.AddXp(r.xp);
                _progress.AddCoins(r.coins);
                if (!IsSpeedRun) _done.Add(q.card);
                if (q.card.isSafetyQuestion) _progress.RecordSafetyTipViewed();
            }
            else
            {
                Combo = 0;
                r.heartLost = _progress.LoseHeart();
                if (r.heartLost) HeartsLost++;
                if (!IsSpeedRun) Requeue(q);
            }

            r.combo = Combo;
            r.outOfHearts = !_progress.CanQuiz;
            _progress.SaveQuiz();
            return r;
        }

        /// <summary>
        /// Shows the answer without marking. It costs a heart, the same as a wrong answer: when it was free,
        /// revealing every question was a way through the round that risked nothing, so the hearts meant
        /// nothing either. The card counts as forgotten and comes back.
        /// </summary>
        public AnswerResult Reveal()
        {
            var q = Current;
            var r = new AnswerResult { revealed = true, answerText = q.answerText, multiplier = 1 };
            Combo = 0;
            r.heartLost = _progress.LoseHeart();
            if (r.heartLost) HeartsLost++;
            if (!IsSpeedRun)
            {
                UpdateSchedule(q, false, DateTime.UtcNow);
                Requeue(q);
            }
            r.outOfHearts = !_progress.CanQuiz;
            _progress.SaveQuiz();
            return r;
        }

        /// <summary>Flashcards only: the learner marks themselves.</summary>
        public void SelfMark(bool knewIt)
        {
            var q = Current;
            if (!IsSpeedRun)
            {
                UpdateSchedule(q, knewIt, DateTime.UtcNow);
                if (knewIt) _done.Add(q.card);
                else Requeue(q);
            }
            _progress.SaveQuiz();
        }

        void UpdateSchedule(Question q, bool correct, DateTime now)
        {
            var state = _progress.CardFor(q.card.Key);
            if (q.attempt == 0)
            {
                SpacedRepetition.Review(state, correct, now);
                if (!correct) _forgotten.Add(q.card);
            }
            else if (correct) SpacedRepetition.Relearned(state, now);
        }

        void Requeue(Question q) => _queue.Insert(Math.Min(_queue.Count, 2), new Question { card = q.card, forgotten = true });

        public void EndSpeedRun() { if (IsSpeedRun) IsFinished = true; }

        /// <summary>Ends the round and saves. Also called when the learner leaves part-way.</summary>
        public RoundReport Finish()
        {
            IsFinished = true;
            _progress.CompleteRound(Deck, Answered > 0);
            return new RoundReport
            {
                speedRun = IsSpeedRun,
                answeredAny = Answered > 0,
                xp = Xp, coins = Coins, answered = Answered, correct = CorrectAnswers,
                firstTryAnswered = FirstTryAnswered, firstTryCorrect = FirstTryCorrect,
                bestCombo = BestCombo, heartsLost = HeartsLost,
                cardsLevelledUp = _cards.Count(c => _progress.CardLevel(c) > _levelBefore[c]),
                masteryBefore01 = _masteryBefore,
                masteryAfter01 = _progress.DeckMastery01(_cards),
                streakBefore = _streakBefore,
                streakAfter = _progress.CurrentStreak,
                forgotten = _forgotten.Distinct().ToList(),
            };
        }

        // ------------------------------------------------------------------ helpers

        void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }

        void ShuffleAwayFromOriginal(List<string> items)
        {
            var original = items.ToList();
            for (int tries = 0; tries < 5; tries++)
            {
                Shuffle(items);
                if (!items.SequenceEqual(original)) return;
            }
        }
    }
}

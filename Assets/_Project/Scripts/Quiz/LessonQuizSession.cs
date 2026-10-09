using System;
using System.Collections.Generic;
using System.Linq;
using BuildAR.Data;
using BuildAR.Managers;

namespace BuildAR.Quiz
{
    /// <summary>One answered question of a lesson quiz, for the results screen.</summary>
    public struct LessonQuizAnswer
    {
        public QuizQuestionSO card;
        public bool correct;
        public string answerText;
    }

    public struct LessonQuizReport
    {
        public int correct, total, passMark, percent, bestBefore, xp;
        public bool passed, firstPass;
        public List<LessonQuizAnswer> missed;
    }

    /// <summary>
    /// A lesson's test: its 20 questions in a random order, each asked once in its own format, and scored. Passing
    /// (75%) is what lets the lesson be completed; retakes are unlimited and the best score is kept.
    ///
    /// Unlike a Memorize round it is a fair test of what the learner knows right now: a wrong answer is final (it
    /// doesn't come back until it's right), there are no hearts to run out of, no hints, no reveal and no
    /// flashcards to mark yourself on. It earns XP and counts towards the daily streak, but it leaves the Memorize
    /// schedule alone.
    /// </summary>
    public sealed class LessonQuizSession
    {
        public const int QuestionCount = 20;
        public const int PassPercent = 75;

        readonly ProgressManager _progress;
        readonly Random _rng;
        readonly List<Question> _questions = new List<Question>();
        readonly List<LessonQuizAnswer> _answers = new List<LessonQuizAnswer>();
        int _index = -1;

        public LessonSO Lesson { get; }
        public Question Current => _index >= 0 && _index < _questions.Count ? _questions[_index] : null;
        public int Total => _questions.Count;
        /// <summary>1-based number of the current question.</summary>
        public int Number => _index + 1;
        public int Answered => _answers.Count;
        public int Correct { get; private set; }
        public int Xp { get; private set; }
        public bool IsFinished { get; private set; }
        public bool IsLast => _index >= _questions.Count - 1;

        /// <summary>Correct answers needed to pass: 75%, rounded up (15 of 20).</summary>
        public int PassMark => PassMarkFor(Total);

        public static int PassMarkFor(int total) => (int)Math.Ceiling(total * PassPercent / 100.0);

        public static IReadOnlyList<QuizQuestionSO> CardsFor(LessonSO lesson) =>
            lesson == null ? Array.Empty<QuizQuestionSO>()
                : (IReadOnlyList<QuizQuestionSO>)lesson.quiz.Where(c => c != null && c.IsPlayable).Distinct().ToList();

        public LessonQuizSession(LessonSO lesson, ProgressManager progress, Random rng = null)
        {
            Lesson = lesson;
            _progress = progress;
            _rng = rng ?? new Random();

            var cards = CardsFor(lesson).ToList();
            Shuffle(cards);
            foreach (var card in cards.Take(QuestionCount)) _questions.Add(Ask(card));
        }

        /// <summary>Moves to the next question. Returns false when there are none left.</summary>
        public bool Next()
        {
            if (IsFinished || _index + 1 >= _questions.Count) return false;
            _index++;
            return true;
        }

        public AnswerResult Submit(bool correct, bool close = false)
        {
            var q = Current;
            var r = new AnswerResult { correct = correct, close = close, answerText = q.answerText, multiplier = 1 };
            _answers.Add(new LessonQuizAnswer { card = q.card, correct = correct, answerText = q.answerText });
            _progress.RecordQuestionAnswered();
            if (correct)
            {
                Correct++;
                r.xp = QuizRewards.BaseXp;
                Xp += r.xp;
                _progress.AddXp(r.xp);
                if (q.card.isSafetyQuestion) _progress.RecordSafetyTipViewed();
            }
            _progress.SaveQuiz();
            return r;
        }

        /// <summary>Scores the quiz and keeps the best result. Only a quiz answered to the end counts.</summary>
        public LessonQuizReport Finish()
        {
            IsFinished = true;
            int percent = Total == 0 ? 0 : (int)Math.Round(100.0 * Correct / Total);
            int bestBefore = _progress.LessonQuizBest(Lesson.lessonId);
            bool passed = Correct >= PassMark;
            _progress.RecordLessonQuiz(Lesson.lessonId, percent);
            return new LessonQuizReport
            {
                correct = Correct, total = Total, passMark = PassMark, percent = percent, bestBefore = bestBefore, xp = Xp,
                passed = passed,
                firstPass = passed && bestBefore < PassPercent,
                missed = _answers.Where(a => !a.correct).ToList(),
            };
        }

        // ------------------------------------------------------------------ asking

        /// <summary>
        /// Each card in its own format, options shuffled. A Typing card becomes multiple choice when it can. Never a
        /// flashcard: those are self-marked, so they can't count towards completing a lesson.
        /// </summary>
        Question Ask(QuizQuestionSO c)
        {
            var q = new Question { card = c, answerText = c.AnswerText };
            switch (c.questionType)
            {
                case QuizQuestionSO.QuestionType.MultipleChoice:
                    Choice(q, c.options, c.options[c.correctOptionIndex]);
                    break;
                case QuizQuestionSO.QuestionType.TrueFalse:
                    q.format = AskFormat.TrueFalse;
                    q.options = new List<string> { "True", "False" };
                    q.correctIndex = c.statementIsTrue ? 0 : 1;
                    break;
                case QuizQuestionSO.QuestionType.Typing:
                    var wrong = c.distractors.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().ToList();
                    if (wrong.Count >= 2)
                    {
                        Shuffle(wrong);
                        Choice(q, wrong.Take(3).Append(c.answer).ToList(), c.answer);
                    }
                    else
                    {
                        q.format = AskFormat.Typing;
                        q.accepted = new List<string> { c.answer };
                        q.accepted.AddRange(c.acceptedAnswers.Where(a => !string.IsNullOrWhiteSpace(a)));
                    }
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
            return q;
        }

        void Choice(Question q, List<string> options, string correct)
        {
            q.format = AskFormat.MultipleChoice;
            q.options = options.ToList();
            Shuffle(q.options);
            q.correctIndex = q.options.IndexOf(correct);
            q.answerText = correct;
        }

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

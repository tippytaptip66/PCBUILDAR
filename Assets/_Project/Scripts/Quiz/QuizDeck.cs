using System.Collections.Generic;
using System.Linq;
using BuildAR.Data;
using BuildAR.Managers;

namespace BuildAR.Quiz
{
    /// <summary>Preferred question style, chosen with the cog during a round.</summary>
    public enum QuizStyle { Mixed, MultipleChoice, Typing, Flashcards }

    /// <summary>How one card is asked this time.</summary>
    public enum AskFormat { MultipleChoice, TrueFalse, Typing, Matching, Ordering, Flashcard }

    public sealed class QuizDeck
    {
        const string ComponentPrefix = "component:";

        public string Id { get; }
        public string Title { get; }
        public IReadOnlyList<QuizQuestionSO> Cards { get; }

        public QuizDeck(string id, string title, IEnumerable<QuizQuestionSO> cards)
        {
            Id = id;
            Title = title;
            Cards = cards.Where(c => c != null && c.IsPlayable).Distinct().ToList();
        }

        public string ComponentId => Id.StartsWith(ComponentPrefix) ? Id.Substring(ComponentPrefix.Length) : null;

        public static QuizDeck ForComponent(ComponentDefinitionSO c) =>
            new QuizDeck(ComponentPrefix + c.id, c.displayName, c.quizQuestions);

        /// <summary>
        /// Every component's cards, plus the quiz questions of each lesson already completed, so the daily review
        /// keeps what has been learned fresh. Lessons not done yet stay out: their questions would be new material.
        /// </summary>
        public static QuizDeck DailyReview(ComponentDatabase db)
        {
            var done = ProgressManager.Instance != null ? ProgressManager.Instance.Data.completedLessonIds : new List<string>();
            var lessonCards = db.Lessons.Where(l => done.Contains(l.lessonId)).SelectMany(l => l.quiz);
            return new QuizDeck("review", "Daily review", db.All.SelectMany(c => c.quizQuestions).Concat(lessonCards));
        }
    }
}

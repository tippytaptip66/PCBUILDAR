using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuildAR.Data
{
    /// <summary>
    /// One study card. It has a native type, but Memorize can ask it in other styles
    /// (a Typing card can be asked as multiple choice, any card can be a flashcard).
    /// Spaced-repetition progress is keyed by the asset name, so keep names unique.
    /// </summary>
    [CreateAssetMenu(menuName = "BuildAR/Quiz Question", fileName = "NewQuizQuestion")]
    public class QuizQuestionSO : ScriptableObject
    {
        public enum QuestionType { MultipleChoice, TrueFalse, Typing, Matching, Ordering }

        [Serializable]
        public class MatchPair
        {
            public string left;
            public string right;
        }

        public QuestionType questionType = QuestionType.MultipleChoice;

        [Tooltip("The question, or the statement for True/False.")]
        [TextArea] public string questionText;

        [Tooltip("Multiple choice: the options. Ordering: the items in the CORRECT order (they're shuffled when asked).")]
        public List<string> options = new List<string>();
        public int correctOptionIndex;

        [Tooltip("True/False: is the statement true?")]
        public bool statementIsTrue;

        [Tooltip("Typing: the answer to type. Also shown on the back of the flashcard.")]
        public string answer;
        [Tooltip("Typing: other spellings that count as correct.")]
        public List<string> acceptedAnswers = new List<string>();
        [Tooltip("Typing: wrong answers used when this card is asked as multiple choice.")]
        public List<string> distractors = new List<string>();

        [Tooltip("Matching: the pairs (the right column is shuffled when asked).")]
        public List<MatchPair> pairs = new List<MatchPair>();

        [Tooltip("Shown after answering, and on the back of the flashcard.")]
        [TextArea] public string explanation;

        [Tooltip("Answering correctly unlocks the Safety First badge.")]
        public bool isSafetyQuestion;

        public string Key => name;

        public IEnumerable<MatchPair> ValidPairs => pairs.Where(p => p != null && !string.IsNullOrEmpty(p.left) && !string.IsNullOrEmpty(p.right));

        public bool IsPlayable
        {
            get
            {
                switch (questionType)
                {
                    case QuestionType.MultipleChoice: return options.Count >= 2 && correctOptionIndex >= 0 && correctOptionIndex < options.Count;
                    case QuestionType.Typing: return !string.IsNullOrWhiteSpace(answer);
                    case QuestionType.Matching: return ValidPairs.Count() >= 2;
                    case QuestionType.Ordering: return options.Count >= 2;
                    default: return !string.IsNullOrWhiteSpace(questionText);
                }
            }
        }

        /// <summary>The correct answer as display text (multi-line for Matching / Ordering).</summary>
        public string AnswerText
        {
            get
            {
                switch (questionType)
                {
                    case QuestionType.MultipleChoice: return correctOptionIndex >= 0 && correctOptionIndex < options.Count ? options[correctOptionIndex] : "";
                    case QuestionType.TrueFalse: return statementIsTrue ? "True" : "False";
                    case QuestionType.Typing: return answer;
                    case QuestionType.Matching: return string.Join("\n", ValidPairs.Select(p => $"{p.left}  →  {p.right}"));
                    default: return string.Join("\n", options.Select((o, i) => $"{i + 1}. {o}"));
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BuildAR.Quiz
{
    /// <summary>
    /// Marks typed answers. Ignores case, punctuation, spacing and word order ("PCI-E x16" = "pcie x16"),
    /// and forgives a small typo ("motherbaord") as "close".
    /// </summary>
    public static class AnswerMatcher
    {
        public enum Result { Wrong, Exact, Close }

        static readonly HashSet<string> Fillers = new HashSet<string> { "a", "an", "the" };

        public static Result Check(string input, IEnumerable<string> accepted)
        {
            var typed = Tokens(input);
            if (typed.Count == 0) return Result.Wrong;
            string typedCompact = string.Concat(typed);
            string typedSorted = string.Join(" ", typed.OrderBy(t => t, StringComparer.Ordinal));

            var best = Result.Wrong;
            foreach (var answer in accepted)
            {
                var tokens = Tokens(answer);
                if (tokens.Count == 0) continue;
                string compact = string.Concat(tokens);
                if (compact == typedCompact || string.Join(" ", tokens.OrderBy(t => t, StringComparer.Ordinal)) == typedSorted)
                    return Result.Exact;
                int allowed = compact.Length >= 8 ? 2 : compact.Length >= 4 ? 1 : 0;
                if (allowed > 0 && Distance(compact, typedCompact) <= allowed) best = Result.Close;
            }
            return best;
        }

        static List<string> Tokens(string s)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(s)) return tokens;
            var sb = new StringBuilder();
            foreach (char ch in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) { sb.Append(ch); continue; }
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
            }
            if (sb.Length > 0) tokens.Add(sb.ToString());
            tokens.RemoveAll(t => Fillers.Contains(t));
            return tokens;
        }

        static int Distance(string a, string b)
        {
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                var t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }
    }
}

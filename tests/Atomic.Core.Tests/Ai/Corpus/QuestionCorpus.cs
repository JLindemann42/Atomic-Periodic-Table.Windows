using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Atomic.Core.Tests.Corpus
{
    /// <summary>
    /// One expectation from the training corpus.
    /// </summary>
    /// <param name="Session">
    /// Rows sharing a session replay in turn order against one <c>DialogueState</c>, which is the
    /// only way follow-up slot inheritance can be tested. Null for a standalone row.
    /// </param>
    /// <param name="ExpectIntent">An intent name, or null for "any claimed intent is acceptable".</param>
    /// <param name="ExpectFields">Field ids that must <i>all</i> appear in the plan's field ids.</param>
    /// <param name="ExpectCard">
    /// A card kind name, "none" for "must produce no card", or null for "don't care".
    /// </param>
    /// <param name="MustDefer">
    /// The engine must decline, so the personality layer handles it. Greetings, quizzes and games
    /// live here - answering them from the constants table is the bug this catches.
    /// </param>
    public sealed record CorpusCase(
        string Id,
        string? Session,
        int Turn,
        string Lang,
        string Query,
        string? ExpectIntent,
        IReadOnlyList<string> ExpectFields,
        string? ExpectCard,
        bool MustDefer,
        string Notes,
        string SourceFile,
        int Line)
    {
        /// <summary>How a failure identifies itself, so a report line is enough to find the row.</summary>
        public string Where => $"{SourceFile}:{Line.ToString(System.Globalization.CultureInfo.InvariantCulture)} [{Id}]";
    }

    /// <summary>
    /// Loads the tab-separated question corpus.
    ///
    /// <para>TSV rather than JSON because a flat table is something a non-developer can extend by
    /// the hundred rows without touching code, and because it keeps the corpus identical to the file
    /// Android drives - this is the same 3,162 rows, byte for byte.</para>
    /// </summary>
    public static class QuestionCorpus
    {
        /// <summary>Every corpus file, in the order a report should list them.</summary>
        public static readonly IReadOnlyList<string> Files =
        [
            "properties", "categories", "superlatives_filters", "comparisons", "aggregates",
            "isotopes_nuclides", "safety", "calculations", "unit_convert", "balance_equations",
            "decay", "solutions", "compound", "followups", "complex", "conversations_simple",
            "conversations_complex", "datasets", "must_defer", "capabilities", "cards",
            "localized_sv", "localized_other", "regressions", "reported", "multilingual",
            "multilingual_wide", "multilingual_operators", "conversations_multi",
            "conversations_deep", "conversations_repair",
        ];

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "corpus");

        private static readonly Dictionary<string, IReadOnlyList<CorpusCase>> Cache =
            new(StringComparer.Ordinal);

        /// <summary>True when the corpus is reachable at all. A false here makes every row vacuous.</summary>
        public static bool Available => File.Exists(Path.Combine(Root, "regressions.tsv"));

        /// <summary>Parsed cases for one file.</summary>
        public static IReadOnlyList<CorpusCase> Cases(string name)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(name, out var cached)) return cached;

                var path = Path.Combine(Root, name + ".tsv");
                var parsed = File.Exists(path)
                    ? Parse(name, File.ReadAllText(path))
                    : [];

                Cache[name] = parsed;
                return parsed;
            }
        }

        /// <summary>Every case in the corpus, across every file.</summary>
        public static IReadOnlyList<CorpusCase> AllCases() => Files.SelectMany(Cases).ToList();

        /// <summary>
        /// Cases grouped into replay order.
        ///
        /// <para>Standalone rows each become a group of one. Rows sharing a session become one group
        /// sorted by turn, so the caller can drive them through a single dialogue state.</para>
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<CorpusCase>> Sessions(string name)
        {
            var cases = Cases(name);

            var standalone = cases.Where(c => c.Session is null).Select(c => (IReadOnlyList<CorpusCase>)[c]);
            var grouped = cases
                .Where(c => c.Session is not null)
                .GroupBy(c => c.Session, StringComparer.Ordinal)
                .Select(g => (IReadOnlyList<CorpusCase>)g.OrderBy(c => c.Turn).ToList());

            return standalone.Concat(grouped).ToList();
        }

        /// <summary>
        /// The key a test uses to name one replayable unit: the session id, or the row id.
        ///
        /// <para>xUnit needs a serializable identifier per case so a failure names the row rather
        /// than an index into a list.</para>
        /// </summary>
        public static string KeyOf(IReadOnlyList<CorpusCase> session) =>
            session[0].Session ?? session[0].Id;

        /// <summary>One replayable unit by key.</summary>
        public static IReadOnlyList<CorpusCase> Session(string file, string key) =>
            Sessions(file).First(s => string.Equals(KeyOf(s), key, StringComparison.Ordinal));

        /// <summary>
        /// Parse a corpus file.
        ///
        /// <para>Blank lines and <c>#</c> comments are skipped. A malformed row is a hard error
        /// rather than a silent skip: a corpus that quietly drops rows would report a pass rate over
        /// a set nobody knows the size of.</para>
        /// </summary>
        private static IReadOnlyList<CorpusCase> Parse(string name, string text)
        {
            var cases = new List<CorpusCase>();
            string[]? header = null;
            var lines = text.Split('\n');

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].TrimEnd('\r', '\n');
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

                var columns = line.Split('\t');
                if (header is null)
                {
                    header = columns.Select(c => c.Trim()).ToArray();
                    if (!string.Equals(header[0], "id", StringComparison.Ordinal))
                        throw new InvalidDataException(
                            $"{name}.tsv:{index + 1} first column must be 'id', got '{columns[0]}'");
                    continue;
                }

                // Strict column count. A row with one tab too many silently shifts every later value
                // into the wrong column - must_defer lands in notes and a hard expectation quietly
                // becomes a comment.
                if (columns.Length != header.Length)
                    throw new InvalidDataException(
                        $"{name}.tsv:{index + 1} has {columns.Length} columns, expected {header.Length}");

                var row = header
                    .Select((h, i) => (h, v: columns[i].Trim()))
                    .ToDictionary(e => e.h, e => e.v, StringComparer.Ordinal);

                var id = Value(row, "id");
                if (id.Length == 0) throw new InvalidDataException($"{name}.tsv:{index + 1} has no id");

                var query = Value(row, "query");
                if (query.Length == 0)
                    throw new InvalidDataException($"{name}.tsv:{index + 1} [{id}] has no query");

                var expectIntent = Value(row, "expect_intent");

                cases.Add(new CorpusCase(
                    Id: id,
                    Session: Value(row, "session") is { Length: > 0 } s ? s : null,
                    Turn: int.TryParse(Value(row, "turn"), out var turn) ? turn : 1,
                    Lang: Value(row, "lang") is { Length: > 0 } l ? l : "en",
                    Query: query,
                    ExpectIntent: expectIntent.Length == 0 || expectIntent == "*" ? null : expectIntent,
                    ExpectFields: Value(row, "expect_fields")
                        .Split(',').Select(f => f.Trim()).Where(f => f.Length > 0).ToList(),
                    ExpectCard: Value(row, "expect_card") is { Length: > 0 } c ? c : null,
                    MustDefer: Affirmative.Contains(Value(row, "must_defer").ToLowerInvariant()),
                    Notes: Value(row, "notes"),
                    SourceFile: name + ".tsv",
                    Line: index + 1));
            }

            return cases;
        }

        private static string Value(IReadOnlyDictionary<string, string> row, string key) =>
            row.TryGetValue(key, out var value) ? value : string.Empty;

        private static readonly HashSet<string> Affirmative =
            new(StringComparer.Ordinal) { "y", "yes", "true", "1" };
    }
}

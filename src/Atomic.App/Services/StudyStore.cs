using System;
using System.Collections.Generic;
using System.IO;
using Atomic.Core.Study;
using Windows.Storage;

namespace Atomic.App.Services
{
    /// <summary>
    /// The study coach's deck on this PC (roadmap 2.6), kept in <c>study_cards.tsv</c> in the app's
    /// local folder. One deck per process, loaded on first use and written after every change, so
    /// the Learning Games, the chat and the progress sync all see the same cards. Mirrors Android's
    /// <c>StudyStore</c>.
    /// </summary>
    public static class StudyStore
    {
        private const string FileName = "study_cards.tsv";
        private static readonly object Lock = new();
        private static StudyDeck? _deck;

        public static StudyDeck Deck
        {
            get
            {
                lock (Lock) return _deck ??= new StudyDeck(Load());
            }
        }

        /// <summary>The deck for the chat, saving after every change.</summary>
        public static StudyAccess Access() => new(Deck, Save);

        /// <summary>
        /// Review the card an answered question feeds, if the user has PRO and the question is
        /// about one element in a scheduled skill. Safe to call for every answer.
        /// </summary>
        public static void Record(string? category, string? subjectKey, bool correct)
        {
            if (!SettingsStore.HasAnyPro) return;
            if (Deck.Record(category, subjectKey, correct, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) is not null) Save();
        }

        /// <summary>Fold in the cloud's cards after a sync, keeping each card's later review.</summary>
        public static void MergeFrom(IReadOnlyDictionary<string, CardState> cards)
        {
            if (cards.Count == 0) return;
            Deck.MergeFrom(cards);
            Save();
        }

        public static void Save()
        {
            lock (Lock)
            {
                try
                {
                    File.WriteAllText(FilePath, StudyDeck.Encode(Deck.Snapshot()));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // The deck stays right in memory for this session; the next save tries again.
                }
            }
        }

        private static IReadOnlyDictionary<string, CardState> Load()
        {
            try
            {
                return File.Exists(FilePath) ? StudyDeck.Decode(File.ReadAllText(FilePath)) : new Dictionary<string, CardState>();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return new Dictionary<string, CardState>();
            }
        }

        private static string FilePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, FileName);
    }
}

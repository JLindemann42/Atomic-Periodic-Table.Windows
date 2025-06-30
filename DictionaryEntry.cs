namespace Atomic_WinUI
{ 
    public class DictionaryEntry
    {
        public string Category { get; set; }
        public string Term { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }

        public DictionaryEntry(string category, string term, string description, string url)
        {
            Category = category;
            Term = term;
            Description = description;
            Url = url;
        }
}
}

namespace Atomic_WinUI
{
    public class ConstantsEntry
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Unit { get; set; }
        public string Info { get; set; }
        public string Category2 { get; set; }


        public ConstantsEntry(string name, string value, string unit, string info, string category2)
        {
            Name = name;
            Value = value;
            Unit = unit;
            Info = info;
            Category2 = category2;

        }
    }
}
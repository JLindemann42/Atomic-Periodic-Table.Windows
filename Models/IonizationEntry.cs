using System.Collections.Generic;

namespace Atomic_WinUI
{
    public class IonizationEntry
    {
        public string Name { get; set; }
        public string Short { get; set; }
        public string Count { get; set; }

        public string FirstIonizationEnergy { get; set; }
        public List<string> IonizationEnergies { get; set; } = new();

        public IonizationEntry(string name, string shortName, string count)
        {
            Name = name;
            Short = shortName;
            Count = count;
        }
    }
}
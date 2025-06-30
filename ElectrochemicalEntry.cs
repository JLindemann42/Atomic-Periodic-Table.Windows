
namespace Atomic_WinUI
{ 
    public class ElectrochemicalEntry
    {
        public string Name { get; set; }
        public string Short { get; set; }
        public string Voltage { get; set; }
        public string Charge { get; set; }

        public ElectrochemicalEntry(string name, string symbol, string voltage, string charge)
        {
            Name = name;
            Short = symbol;
            Voltage = voltage;
            Charge = charge;
        }
}
}
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Nlu;
foreach (var q in new[] { "12 g", "500 K", "19.3 g/cm3", "11460 साल", "2 M solution" })
{
    var s = QuantityScanner.Scan(q);
    System.Console.WriteLine($"{q,-16} -> {s.Count} : {string.Join(", ", s.Select(x => $"{x.Value}{x.Unit}[{x.Dimension}]"))}");
}
System.Console.WriteLine("KnownUnit(g)=" + (UnitConverter.KnownUnit("g") ?? "null"));
System.Console.WriteLine("KnownUnit(K)=" + (UnitConverter.KnownUnit("K") ?? "null"));
System.Console.WriteLine("target(11460 साल में)=" + (QuantityScanner.TargetUnit("11460 साल में") ?? "null"));
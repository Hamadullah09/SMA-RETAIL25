using BenchmarkDotNet.Running;

namespace Retail25.Benchmarks;

/// <summary>
/// The RFID throughput suite.
/// <para>
/// <c>dotnet run -c Release --project benchmarks/Retail25.Benchmarks</c>
/// </para>
/// <para>
/// Add <c>--filter *Sustained*</c> for the hour-long soak alone; it takes minutes rather than
/// seconds, so it is worth running on its own when the question is memory rather than speed.
/// </para>
/// <para>
/// Named <c>BenchmarkEntryPoint</c> rather than <c>Program</c>: the terminal agent this project
/// references already publishes a <c>Program</c>, and two of them in scope is a compile error.
/// </para>
/// </summary>
public static class BenchmarkEntryPoint
{
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(BenchmarkEntryPoint).Assembly).Run(args);
}

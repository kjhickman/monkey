using BenchmarkDotNet.Attributes;

namespace Kong.Benchmarks;

[MemoryDiagnoser]
public class LoopSumBenchmark
{
    private const string AssemblyName = "Kong.Benchmark.LoopSum1k";
    private CompiledKongProgram _compiledProgram = null!;

    [GlobalSetup]
    public void Setup()
    {
        var kongSource = """
                        module Bench

                        var total: int = 0
                        var i: int = 0

                        while i < 1000 {
                            total = total + i
                            i = i + 1
                        }

                        total
                        """;
        _compiledProgram = CompiledKongProgram.CompileIntProgram(kongSource, AssemblyName);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _compiledProgram.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int CSharp()
    {
        var total = 0;
        for (var i = 0; i < 1_000; i++)
        {
            total += i;
        }

        return total;
    }

    [Benchmark]
    public int Kong() => _compiledProgram.EntryPoint();
}

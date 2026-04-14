using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(RenderBenchmarks).Assembly).Run(args);


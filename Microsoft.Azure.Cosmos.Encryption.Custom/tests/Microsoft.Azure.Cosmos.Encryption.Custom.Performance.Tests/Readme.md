``` ini

BenchmarkDotNet=v0.13.3, OS=Windows 11 (10.0.22631.4169)
11th Gen Intel Core i9-11950H 2.60GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK=8.0.400
  [Host] : .NET 6.0.33 (6.0.3324.36610), X64 RyuJIT AVX2

Job=MediumRun  Toolchain=InProcessEmitToolchain  IterationCount=15  
LaunchCount=2  WarmupCount=10  

```
|  Method | DocumentSizeInKb |        Mean |      Error |     StdDev |      Median |     Gen0 |    Gen1 |    Gen2 |  Allocated |
|-------- |----------------- |------------:|-----------:|-----------:|------------:|---------:|--------:|--------:|-----------:|
| **Encrypt** |                **1** |    **18.26 μs** |   **0.326 μs** |   **0.487 μs** |    **18.03 μs** |   **1.9531** |  **1.9531** |       **-** |   **24.19 KB** |
| Decrypt |                1 |    30.49 μs |   0.516 μs |   0.773 μs |    30.76 μs |   3.1128 |  0.7935 |       - |    38.7 KB |
| **Encrypt** |               **10** |    **42.71 μs** |   **0.613 μs** |   **0.899 μs** |    **42.23 μs** |   **9.2773** |  **1.1597** |       **-** |   **114.4 KB** |
| Decrypt |               10 |   110.83 μs |   2.676 μs |   4.005 μs |   111.75 μs |  12.3291 |  1.3428 |       - |  151.97 KB |
| **Encrypt** |              **100** |   **502.39 μs** |  **63.242 μs** |  **94.657 μs** |   **514.72 μs** |  **41.5039** | **39.0625** | **38.5742** | **1052.29 KB** |
| Decrypt |              100 | 1,395.34 μs | 144.453 μs | 211.737 μs | 1,392.58 μs | 136.7188 | 99.6094 | 80.0781 | 1229.16 KB |

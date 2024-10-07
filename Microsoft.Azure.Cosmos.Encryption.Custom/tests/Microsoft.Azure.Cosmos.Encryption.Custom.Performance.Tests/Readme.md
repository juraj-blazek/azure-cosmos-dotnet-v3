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
| **Encrypt** |                **1** |    **18.01 μs** |   **0.263 μs** |   **0.377 μs** |    **18.15 μs** |   **1.8616** |  **1.8616** |       **-** |   **22.85 KB** |
| Decrypt |                1 |    32.37 μs |   0.512 μs |   0.751 μs |    32.37 μs |   3.1738 |  0.7935 |       - |   39.53 KB |
| **Encrypt** |               **10** |    **42.26 μs** |   **0.513 μs** |   **0.768 μs** |    **42.44 μs** |   **8.3618** |  **1.2207** |       **-** |  **103.36 KB** |
| Decrypt |               10 |   110.36 μs |   1.375 μs |   2.059 μs |   111.42 μs |  12.4512 |  1.5869 |       - |   152.8 KB |
| **Encrypt** |              **100** |   **441.99 μs** |  **37.656 μs** |  **56.362 μs** |   **456.53 μs** |  **41.5039** | **39.0625** | **38.5742** |  **943.11 KB** |
| Decrypt |              100 | 1,463.95 μs | 102.253 μs | 153.048 μs | 1,459.58 μs | 136.7188 | 97.6563 | 80.0781 | 1229.89 KB |

``` ini

BenchmarkDotNet=v0.13.3, OS=Windows 11 (10.0.26100.2033)
11th Gen Intel Core i9-11950H 2.60GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK=8.0.403
  [Host] : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX2

Job=MediumRun  Toolchain=InProcessEmitToolchain  IterationCount=15  
LaunchCount=2  WarmupCount=10  

```
|  Method | DocumentSizeInKb |  JsonProcessor |        Mean |     Error |    StdDev |    Gen0 |    Gen1 |    Gen2 |  Allocated |
|-------- |----------------- |--------------- |------------:|----------:|----------:|--------:|--------:|--------:|-----------:|
| **Encrypt** |                **1** |     **Newtonsoft** |    **21.24 μs** |  **0.322 μs** |  **0.441 μs** |  **0.1526** |  **0.0305** |       **-** |   **36.44 KB** |
| Decrypt |                1 |     Newtonsoft |    24.30 μs |  0.114 μs |  0.170 μs |  0.1526 |  0.0305 |       - |   39.27 KB |
| **Encrypt** |                **1** | **SystemTextJson** |    **13.87 μs** |  **0.114 μs** |  **0.170 μs** |  **0.0916** |  **0.0153** |       **-** |   **22.48 KB** |
| Decrypt |                1 | SystemTextJson |    13.90 μs |  0.161 μs |  0.241 μs |  0.0763 |  0.0153 |       - |   19.02 KB |
| **Encrypt** |               **10** |     **Newtonsoft** |    **80.46 μs** |  **0.367 μs** |  **0.527 μs** |  **0.6104** |  **0.1221** |       **-** |  **166.64 KB** |
| Decrypt |               10 |     Newtonsoft |    97.22 μs |  1.481 μs |  2.124 μs |  0.6104 |  0.1221 |       - |  152.53 KB |
| **Encrypt** |               **10** | **SystemTextJson** |    **41.46 μs** |  **0.378 μs** |  **0.554 μs** |  **0.4272** |  **0.0610** |       **-** |  **102.99 KB** |
| Decrypt |               10 | SystemTextJson |    36.12 μs |  0.614 μs |  0.900 μs |  0.3052 |  0.0610 |       - |   76.11 KB |
| **Encrypt** |              **100** |     **Newtonsoft** | **1,111.13 μs** | **15.367 μs** | **22.038 μs** | **25.3906** | **21.4844** | **21.4844** | **1638.33 KB** |
| Decrypt |              100 |     Newtonsoft | 1,101.82 μs | 18.516 μs | 27.715 μs | 17.5781 | 15.6250 | 15.6250 | 1229.51 KB |
| **Encrypt** |              **100** | **SystemTextJson** |   **858.31 μs** | **19.354 μs** | **28.369 μs** | **26.3672** | **26.3672** | **26.3672** |  **942.75 KB** |
| Decrypt |              100 | SystemTextJson |   597.23 μs | 12.084 μs | 17.330 μs | 17.5781 | 17.5781 | 17.5781 |  746.32 KB |

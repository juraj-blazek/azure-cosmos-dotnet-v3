# Compression of serialized property before encryption

## Compression effectivity on fully random strings

| Original size | Compression level | Final size | Ratio |
| -: | -: | -: | -: |
| 197 | None | 330 | 167% |
| 197 | Fastest | 338 | 171% |
| 197 | Optimal | 338 | 171% |
| 197 | SmallestSize | 338 | 171% |
| 287 | None | 450 | 156% |
| 287 | Fastest | 438 | 152% |
| 287 | Optimal | 438 | 152% |
| 287 | SmallestSize | 438 | 152% |
| 1187 | None | 1650 | 139% |
| 1187 | Fastest | 1218 | 102% |
| 1187 | Optimal | 1222 | 102% |
| 1187 | SmallestSize | 1222 | 102% |
| 10187 | None | 13650 | 133% |
| 10187 | Fastest | 9082 | 89% |
| 10187 | Optimal | 9394 | 92% |
| 10187 | SmallestSize | 9278 | 91% |
| 1000187 | None | 1333650 | 133% |
| 1000187 | Fastest | 877914 | 87% |
| 1000187 | Optimal | 918682 | 91% |
| 1000187 | SmallestSize | 902702 | 90% |


## Compression performance

``` ini

BenchmarkDotNet=v0.13.3, OS=Windows 11 (10.0.22621.1105)
11th Gen Intel Core i9-11950H 2.60GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK=7.0.100
  [Host] : .NET 6.0.13 (6.0.1322.58009), X64 RyuJIT AVX2

Job=MediumRun  Toolchain=InProcessEmitToolchain  IterationCount=15  
LaunchCount=2  WarmupCount=10  

```
|                                 Method | DocumentSizeInKb |          Mean |        Error |       StdDev |      Gen0 |      Gen1 |      Gen2 |   Allocated |
|--------------------------------------- |----------------- |--------------:|-------------:|-------------:|----------:|----------:|----------:|------------:|
|           **EncryptDecrypt_NoCompression** |                **0** |      **92.64 μs** |     **2.722 μs** |     **4.074 μs** |    **5.6152** |    **1.8311** |         **-** |    **70.17 KB** |
|      EncryptDecrypt_CompressionFastest |                0 |     117.46 μs |     2.131 μs |     3.123 μs |    5.8594 |    1.9531 |         - |    72.85 KB |
|      EncryptDecrypt_CompressionOptimal |                0 |     123.51 μs |     3.128 μs |     4.682 μs |    5.8594 |    1.9531 |         - |    72.85 KB |
| EncryptDecrypt_CompressionSmallestSize |                0 |     113.63 μs |     2.323 μs |     3.476 μs |    5.8594 |    1.9531 |         - |       73 KB |
|           **EncryptDecrypt_NoCompression** |                **1** |     **129.07 μs** |     **5.042 μs** |     **7.547 μs** |   **11.4746** |    **2.9297** |         **-** |   **141.69 KB** |
|      EncryptDecrypt_CompressionFastest |                1 |     165.80 μs |     3.159 μs |     4.631 μs |   10.4980 |    2.6855 |         - |   130.27 KB |
|      EncryptDecrypt_CompressionOptimal |                1 |     174.57 μs |     4.936 μs |     7.235 μs |   10.4980 |    2.6855 |         - |   130.31 KB |
| EncryptDecrypt_CompressionSmallestSize |                1 |     161.37 μs |     3.462 μs |     5.182 μs |   10.4980 |    2.6855 |         - |   130.31 KB |
|           **EncryptDecrypt_NoCompression** |               **10** |     **433.80 μs** |     **7.915 μs** |    **11.095 μs** |   **53.2227** |    **6.3477** |         **-** |   **654.09 KB** |
|      EncryptDecrypt_CompressionFastest |               10 |     604.18 μs |    14.868 μs |    22.253 μs |   50.7813 |   10.7422 |         - |   632.76 KB |
|      EncryptDecrypt_CompressionOptimal |               10 |     726.03 μs |    19.576 μs |    29.301 μs |   51.7578 |   10.7422 |         - |   638.43 KB |
| EncryptDecrypt_CompressionSmallestSize |               10 |     693.27 μs |    35.389 μs |    51.872 μs |   51.7578 |   11.7188 |         - |   636.39 KB |
|           **EncryptDecrypt_NoCompression** |              **100** |   **6,624.13 μs** |   **604.211 μs** |   **904.355 μs** |  **687.5000** |  **570.3125** |  **500.0000** |  **6407.04 KB** |
|      EncryptDecrypt_CompressionFastest |              100 |   8,691.90 μs |   991.124 μs | 1,483.468 μs |  593.7500 |  460.9375 |  328.1250 |  5433.77 KB |
|      EncryptDecrypt_CompressionOptimal |              100 |  10,322.65 μs | 1,066.350 μs | 1,596.062 μs |  750.0000 |  609.3750 |  484.3750 |  5491.64 KB |
| EncryptDecrypt_CompressionSmallestSize |              100 |  12,284.40 μs | 1,561.662 μs | 2,337.422 μs |  750.0000 |  609.3750 |  484.3750 |  5462.49 KB |
|           **EncryptDecrypt_NoCompression** |             **1000** |  **51,312.80 μs** | **1,506.471 μs** | **2,208.165 μs** | **3200.0000** | **2900.0000** | **2000.0000** | **63250.64 KB** |
|      EncryptDecrypt_CompressionFastest |             1000 |  71,394.82 μs | 2,545.488 μs | 3,731.144 μs | 4714.2857 | 4571.4286 | 3571.4286 | 57264.95 KB |
|      EncryptDecrypt_CompressionOptimal |             1000 |  89,724.83 μs | 2,790.241 μs | 4,176.301 μs | 4666.6667 | 4333.3333 | 3500.0000 | 57816.82 KB |
| EncryptDecrypt_CompressionSmallestSize |             1000 | 101,428.08 μs | 2,324.829 μs | 3,479.695 μs | 4000.0000 | 3400.0000 | 2800.0000 | 57520.08 KB |

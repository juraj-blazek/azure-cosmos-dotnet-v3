# Compression of serialized property before encryption

## Compression effectivity on fully random strings

| Original size | Compression algorithm | Final size | Ratio |
| -: | -: | -: | -: |
| 286 | None | 450 | 157% |
| 286 | Deflate | 446 | 155% |
| 386 | None | 582 | 150% |
| 386 | Deflate | 534 | 138% |
| 686 | None | 982 | 143% |
| 686 | Deflate | 794 | 115% |
| 1186 | None | 1650 | 139% |
| 1186 | Deflate | 1222 | 103% |
| 5186 | None | 6982 | 134% |
| 5186 | Deflate | 4714 | 90% |
| 10187 | None | 13650 | 133% |
| 10187 | Deflate | 9082 | 89% |
| 100187 | None | 133650 | 133% |
| 100187 | Deflate | 88062 | 87% |
| 1000187 | None | 1333650 | 133% |
| 1000187 | Deflate | 877926 | 87% |

## Compression performance

``` ini

BenchmarkDotNet=v0.13.3, OS=Windows 11 (10.0.22621.1105)
11th Gen Intel Core i9-11950H 2.60GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK=7.0.100
  [Host] : .NET 6.0.13 (6.0.1322.58009), X64 RyuJIT AVX2

Job=MediumRun  Toolchain=InProcessEmitToolchain  IterationCount=15  
LaunchCount=2  WarmupCount=10  

```

|                            Method | DocumentSizeInKb |         Mean |        Error |       StdDev |       Median |      Gen0 |      Gen1 |      Gen2 |   Allocated |
|---------------------------------- |----------------- |-------------:|-------------:|-------------:|-------------:|----------:|----------:|----------:|------------:|
|      **EncryptDecrypt_NoCompression** |                **0** |     **85.93 μs** |     **1.591 μs** |     **2.332 μs** |     **85.66 μs** |    **5.6152** |    **1.8311** |         **-** |    **70.29 KB** |
| EncryptDecrypt_CompressionDeflate |                0 |    112.93 μs |     4.345 μs |     6.369 μs |    109.49 μs |    5.8594 |    1.9531 |         - |    72.37 KB |
|      **EncryptDecrypt_NoCompression** |                **1** |    **122.98 μs** |     **2.304 μs** |     **3.305 μs** |    **122.32 μs** |   **11.4746** |    **2.9297** |         **-** |    **141.8 KB** |
| EncryptDecrypt_CompressionDeflate |                1 |    181.23 μs |    16.860 μs |    23.078 μs |    171.18 μs |   10.2539 |    2.6855 |         - |   127.72 KB |
|      **EncryptDecrypt_NoCompression** |               **10** |    **446.06 μs** |    **38.381 μs** |    **57.447 μs** |    **422.15 μs** |   **53.2227** |   **10.7422** |         **-** |    **654.2 KB** |
| EncryptDecrypt_CompressionDeflate |               10 |    569.15 μs |    10.295 μs |    15.091 μs |    567.30 μs |   49.8047 |   10.7422 |         - |    611.2 KB |
|      **EncryptDecrypt_NoCompression** |              **100** |  **6,444.25 μs** |   **879.043 μs** | **1,143.005 μs** |  **6,646.34 μs** |  **679.6875** |  **554.6875** |  **500.0000** |  **6407.14 KB** |
| EncryptDecrypt_CompressionDeflate |              100 |  8,736.01 μs |   750.250 μs | 1,099.707 μs |  8,721.78 μs |  750.0000 |  609.3750 |  500.0000 |  5184.99 KB |
|      **EncryptDecrypt_NoCompression** |             **1000** | **56,193.12 μs** | **3,112.437 μs** | **4,658.549 μs** | **55,817.60 μs** | **2800.0000** | **2400.0000** | **1600.0000** | **63250.29 KB** |
| EncryptDecrypt_CompressionDeflate |             1000 | 78,326.61 μs | 5,080.431 μs | 7,286.202 μs | 77,397.96 μs | 3500.0000 | 3125.0000 | 2375.0000 | 52412.85 KB |

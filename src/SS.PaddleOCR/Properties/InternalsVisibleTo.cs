using System.Runtime.CompilerServices;

// Also covers the engine (SimdOnnx source compiled into this assembly as internal types).
[assembly: InternalsVisibleTo("SS.PaddleOCR.Benchmarks")]
[assembly: InternalsVisibleTo("SS.PaddleOCR.Tests")]
[assembly: InternalsVisibleTo("SS.PaddleOCR.UnitTests")]
[assembly: InternalsVisibleTo("SS.PaddleOCR.AndroidBench")]
[assembly: InternalsVisibleTo("SS.PaddleOCR.WasmBench")]

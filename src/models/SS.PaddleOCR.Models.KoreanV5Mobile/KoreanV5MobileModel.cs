using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SS.PaddleOCR.ModelProvider;
using SS.PaddleOCR.Models.TextLineOrientation;

namespace SS.PaddleOCR.Models.KoreanV5Mobile;

/// <summary>
/// Embedded PP-OCRv5 mobile Korean DET + REC models and the Korean recognition dictionary.
/// </summary>
public static class KoreanV5MobileModel
{
    /// <summary>
    /// PP-OCRv5 mobile text detection model (language independent).
    /// </summary>
    public static IPaddleOcrModelProvider Detection { get; } = new ProviderImpl(
        "PP-OCRv5_mobile_det", 
        PaddleOcrModelKind.Detection, 
        "det.onnx", 
        "onnx");

    /// <summary>
    /// <c>korean_PP-OCRv5_mobile_rec</c> recognition model, 11945 classes + blank + space.
    /// </summary>
    public static IPaddleOcrModelProvider Recognition { get; } = new ProviderImpl(
        "korean_PP-OCRv5_mobile_rec", 
        PaddleOcrModelKind.Recognition, 
        "rec.onnx", 
        "onnx");

    /// <summary>
    /// Korean recognition dictionary in the exact class order of <see cref="Recognition"/>.
    /// </summary>
    public static IPaddleOcrModelProvider Dictionary { get; } = new ProviderImpl(
        "korean_PP-OCRv5_mobile_dict.txt", 
        PaddleOcrModelKind.Dictionary, 
        "dict.txt", 
        "utf-8");

    /// <summary>
    /// All providers of this package.
    /// </summary>
    public static IPaddleOcrModelProvider[] All { get; } = 
    [
        Detection, 
        Recognition, 
        Dictionary
    ];

    /// <summary>
    /// Opens the detection model stream.
    /// </summary>
    public static Stream OpenRead() => Detection.OpenRead();

    private sealed class ProviderImpl(string name, PaddleOcrModelKind kind, string resource, string format) : IPaddleOcrModelProvider
    {
        public string Name { get; } = name;
        public PaddleOcrModelKind Kind { get; } = kind;
        public string Format { get; } = format;
        public string? LanguageCode { get; } = "ko";
        public string? Version { get; } = "v5-mobile";
        public Stream OpenRead() => typeof(KoreanV5MobileModel).Assembly.GetManifestResourceStream("SS.PaddleOCR.Models.KoreanV5Mobile." + resource)
            ?? throw new InvalidOperationException("Embedded model resource was not found.");
        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); 
            return Task.FromResult(OpenRead()); 
        }
    }
}

/// <summary>
/// Ready to use PP-OCRv5 mobile Korean OCR model bundles.
/// </summary>
public static class KoreanV5MobileModels
{
    /// <summary>
    /// PP-OCRv5 mobile Korean DET + REC + dictionary, with the shared text line orientation classifier.
    /// </summary>
    public static PaddleOcrModelBundle Default { get; } = new(
        "korean_PP-OCRv5_mobile", 
        "ko", 
        KoreanV5MobileModel.Detection,
        KoreanV5MobileModel.Recognition, 
        KoreanV5MobileModel.Dictionary,
        TextLineOrientationModel.Provider);
        
    /// <summary>
    /// All bundles of this package.
    /// </summary>
    public static PaddleOcrModelBundle[] All { get; } = 
    [
        Default
    ];
}
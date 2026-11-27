using System.Reflection;
using System.Text;
using SS.PaddleOCR.ModelProvider;
using SS.PaddleOCR.Models.KoreanV5Mobile;
using SS.PaddleOCR.OnnxSharp;

namespace SS.PaddleOCR.UnitTests;

public class KoreanV5MobilePackageTests
{
    [Fact]
    public void ProvidersDescribeTheKoreanModelSet()
    {
        IPaddleOcrModelProvider[] all = KoreanV5MobileModel.All;
        Assert.Equal(3, all.Length);
        Assert.Collection(
            all,
            p =>
            {
                Assert.Equal("PP-OCRv5_mobile_det", p.Name);
                Assert.Equal(PaddleOcrModelKind.Detection, p.Kind);
                Assert.Equal("onnx", p.Format);
                Assert.Equal("ko", p.LanguageCode);
                Assert.Equal("v5-mobile", p.Version);
            },
            p =>
            {
                Assert.Equal("korean_PP-OCRv5_mobile_rec", p.Name);
                Assert.Equal(PaddleOcrModelKind.Recognition, p.Kind);
                Assert.Equal("onnx", p.Format);
                Assert.Equal("ko", p.LanguageCode);
                Assert.Equal("v5-mobile", p.Version);
            },
            p =>
            {
                Assert.Equal("korean_PP-OCRv5_mobile_dict.txt", p.Name);
                Assert.Equal(PaddleOcrModelKind.Dictionary, p.Kind);
                Assert.Equal("utf-8", p.Format);
                Assert.Equal("ko", p.LanguageCode);
                Assert.Equal("v5-mobile", p.Version);
            });

        using Stream openRead = KoreanV5MobileModel.OpenRead();
        Assert.Equal(4826518, openRead.Length);
    }

    [Fact]
    public void EmbeddedResourcesResolve()
    {
        Assert.Equal(4826518, KoreanV5MobileModel.Detection.OpenRead().Length);
        Assert.Equal(13418787, KoreanV5MobileModel.Recognition.OpenRead().Length);
        Assert.Equal(47514, KoreanV5MobileModel.Dictionary.OpenRead().Length);
    }

    [Fact]
    public void DictionaryHasExpectedShape()
    {
        string[] lines = ReadDictionary();
        Assert.Equal(11945, lines.Length);
        Assert.Equal("\u1100", lines[0]);
        Assert.Equal("\u1101", lines[1]);
        Assert.Equal("\u1102", lines[2]);
        Assert.Equal("\u03c9", lines[^1]);
        Assert.DoesNotContain(lines, l => l.IndexOf('\r') >= 0);
    }

    [Fact]
    public void DefaultBundleBindsClassifier()
    {
        PaddleOcrModelBundle bundle = KoreanV5MobileModels.Default;
        Assert.Equal("korean_PP-OCRv5_mobile", bundle.Name);
        Assert.Equal("ko", bundle.LanguageCode);
        Assert.Same(KoreanV5MobileModel.Detection, bundle.Detection);
        Assert.Same(KoreanV5MobileModel.Recognition, bundle.Recognition);
        Assert.Same(KoreanV5MobileModel.Dictionary, bundle.Dictionary);
        Assert.NotNull(bundle.Classification);
    }

    /// <summary>
    /// Regression guard for the <c>Concat</c> layout mismatch fixed in <c>LayoutPlanner</c>:
    /// before the fix this crop decoded to <c>삼담성태삼광담</c> (0.6170), and ONNX Runtime decodes it to
    /// <c>바탕으로</c> (0.9996) on the byte-identical preprocessed tensor.
    /// </summary>
    [Fact]
    public void RecognitionMatchesReferenceDecode()
    {
        (byte[] bgr, int width, int height, int stride) = LoadBmp24(resource: "Assets.Korean.rec_line.bmp");
        Assert.Equal(181, width);
        Assert.Equal(48, height);

        using Stream modelStream = KoreanV5MobileModel.Recognition.OpenRead();
        using Stream dictionaryStream = KoreanV5MobileModel.Dictionary.OpenRead();
        Model model = Model.Load(modelStream);
        PaddleOcrRecognizer rec = new(
            model,
            dictionaryStream,
            new PaddleOcrRecognizerOptions { TargetWidth = 192, AdaptiveWidth = false });

        Assert.Equal(11947, rec.ClassCount);
        Assert.Equal(192, rec.TargetWidth);

        PaddleOcrRecognitionResult result = rec.Recognize(bgr, width, height, stride, ImagePixelFormat.Bgr24);
        Assert.Equal("바탕으로", result.Text);
        Assert.True(result.Score > 0.99f, $"score={result.Score}");
        Assert.Equal(24, result.TimeSteps);
    }

    private static string[] ReadDictionary()
    {
        using Stream stream = KoreanV5MobileModel.Dictionary.OpenRead();
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().TrimEnd('\n').Split('\n');
    }

    private static (byte[] Bgr, int Width, int Height, int Stride) LoadBmp24(string resource)
    {
        using Stream stream = typeof(KoreanV5MobilePackageTests).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing embedded resource {resource}.");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        byte[] bmp = buffer.ToArray();

        Assert.Equal((ushort)0x4D42, BitConverter.ToUInt16(bmp, 0));
        int pixelOffset = BitConverter.ToInt32(bmp, 10);
        int width = BitConverter.ToInt32(bmp, 18);
        int rawHeight = BitConverter.ToInt32(bmp, 22);
        int bpp = BitConverter.ToUInt16(bmp, 28);
        int stride = (width * 3 + 3) & ~3;
        bool bottomUp = rawHeight > 0;
        int height = Math.Abs(rawHeight);

        byte[] bgr = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = bottomUp ? height - 1 - y : y;
            Buffer.BlockCopy(bmp, pixelOffset + sourceRow * stride, bgr, y * width * 3, width * 3);
        }

        return (bgr, width, height, width * 3);
    }
}
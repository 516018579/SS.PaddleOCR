using System.Reflection;
using System.Text;
using SS.PaddleOCR.ModelProvider;
using SS.PaddleOCR.Models.ChineseV6Small;
using SS.PaddleOCR.OnnxSharp;

namespace SS.PaddleOCR.UnitTests;

public class ChineseV6SmallPackageTests
{
    [Fact]
    public void ProvidersDescribeTheChineseSmallModelSet()
    {
        IPaddleOcrModelProvider[] all = ChineseV6SmallModel.All;
        Assert.Equal(3, all.Length);
        Assert.Collection(
            all,
            p =>
            {
                Assert.Equal("PP-OCRv6_small_det", p.Name);
                Assert.Equal(PaddleOcrModelKind.Detection, p.Kind);
                Assert.Equal("onnx", p.Format);
                Assert.Equal("zh", p.LanguageCode);
                Assert.Equal("v6-small", p.Version);
            },
            p =>
            {
                Assert.Equal("PP-OCRv6_small_rec", p.Name);
                Assert.Equal(PaddleOcrModelKind.Recognition, p.Kind);
                Assert.Equal("onnx", p.Format);
                Assert.Equal("zh", p.LanguageCode);
                Assert.Equal("v6-small", p.Version);
            },
            p =>
            {
                Assert.Equal("ppocrv6_dict.txt", p.Name);
                Assert.Equal(PaddleOcrModelKind.Dictionary, p.Kind);
                Assert.Equal("utf-8", p.Format);
                Assert.Equal("zh", p.LanguageCode);
                Assert.Equal("v6-small", p.Version);
            });

        using Stream openRead = ChineseV6SmallModel.OpenRead();
        Assert.Equal(9880512, openRead.Length);
    }

    [Fact]
    public void EmbeddedResourcesResolve()
    {
        Assert.Equal(9880512, ChineseV6SmallModel.Detection.OpenRead().Length);
        Assert.Equal(21159378, ChineseV6SmallModel.Recognition.OpenRead().Length);
        using Stream dictionary = ChineseV6SmallModel.Dictionary.OpenRead();
        Assert.True(dictionary.Length > 60000, $"dictionary length={dictionary.Length}");
    }

    [Fact]
    public void DictionaryHasExpectedShape()
    {
        string[] lines = ReadDictionary();
        Assert.Equal(18708, lines.Length);
        Assert.Equal("!", lines[0]);
        Assert.Equal("\U0001F6C5", lines[^1]);
        Assert.DoesNotContain(lines, l => l.IndexOf('\r') >= 0);
    }

    [Fact]
    public void DefaultBundleBindsClassifier()
    {
        PaddleOcrModelBundle bundle = ChineseV6SmallModels.Default;
        Assert.Equal("PP-OCRv6_small", bundle.Name);
        Assert.Equal("zh", bundle.LanguageCode);
        Assert.Same(ChineseV6SmallModel.Detection, bundle.Detection);
        Assert.Same(ChineseV6SmallModel.Recognition, bundle.Recognition);
        Assert.Same(ChineseV6SmallModel.Dictionary, bundle.Dictionary);
        Assert.NotNull(bundle.Classification);
    }

    /// <summary>
    /// Golden decode for the packaged PP-OCRv6 small recognizer.
    /// </summary>
    [Fact]
    public void RecognitionDecodesChineseLine()
    {
        (byte[] bgr, int width, int height, int stride) = LoadBmp24(resource: "Assets.ChineseSmall.rec_line.bmp");
        Assert.Equal(420, width);
        Assert.Equal(100, height);

        using Stream modelStream = ChineseV6SmallModel.Recognition.OpenRead();
        using Stream dictionaryStream = ChineseV6SmallModel.Dictionary.OpenRead();
        Model model = Model.Load(modelStream);
        PaddleOcrRecognizer rec = new(
            model,
            dictionaryStream,
            new PaddleOcrRecognizerOptions { TargetWidth = 288, AdaptiveWidth = false });

        Assert.Equal(18710, rec.ClassCount);

        PaddleOcrRecognitionResult result = rec.Recognize(bgr, width, height, stride, ImagePixelFormat.Bgr24);
        Assert.Equal("中文识别测试", result.Text);
        Assert.True(result.Score > 0.9f, $"score={result.Score}");
    }

    private static string[] ReadDictionary()
    {
        using Stream stream = ChineseV6SmallModel.Dictionary.OpenRead();
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().TrimEnd('\n').Split('\n');
    }

    private static (byte[] Bgr, int Width, int Height, int Stride) LoadBmp24(string resource)
    {
        using Stream stream = typeof(ChineseV6SmallPackageTests).Assembly.GetManifestResourceStream(resource)
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
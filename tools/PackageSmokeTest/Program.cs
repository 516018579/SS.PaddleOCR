using SS.PaddleOCR;
using SS.PaddleOCR.ModelProvider;
using SS.PaddleOCR.Models.ChineseV6Small;
using SS.PaddleOCR.Models.ChineseV6Medium;
using SS.PaddleOCR.Models.KoreanV5Mobile;

Console.OutputEncoding = System.Text.Encoding.UTF8;

string set = args.Length > 0 ? args[0] : "ko";
string? path = args.Length > 1 ? args[1] : null;

PaddleOcrModelBundle bundle;
IPaddleOcrModelProvider[] providers;
if (set == "zh-medium")
{
    bundle = ChineseV6MediumModels.Default;
    providers = ChineseV6MediumModel.All;
    path ??= Repo.Path(@"tools\PackageSmokeTest\samples\zh_lines.bmp");
}
else if (set == "zh-small")
{
    bundle = ChineseV6SmallModels.Default;
    providers = ChineseV6SmallModel.All;
    path ??= Repo.Path(@"tools\PackageSmokeTest\samples\zh_lines.bmp");
}
else
{
    bundle = KoreanV5MobileModels.Default;
    providers = KoreanV5MobileModel.All;
    path ??= Repo.Path(@"tools\PackageSmokeTest\samples\ko_lines2.bmp");
}

foreach (IPaddleOcrModelProvider p in providers)
{
    using Stream s = p.OpenRead();
    Console.WriteLine($"provider {p.Name} kind={p.Kind} fmt={p.Format} lang={p.LanguageCode} ver={p.Version} bytes={s.Length}");
}
Console.WriteLine($"bundle {bundle.Name} lang={bundle.LanguageCode}");

(byte[] bgr, int w, int h) = LoadBmp(path!);
Console.WriteLine($"image {Path.GetFileName(path)} {w}x{h}");

using PaddleOcrAll ocr = await PaddleOcrAll.LoadAsync(bundle);
var sw = System.Diagnostics.Stopwatch.StartNew();
PaddleOcrResult result = ocr.Run(bgr, w, h);
sw.Stop();
Console.WriteLine($"ocr {sw.ElapsedMilliseconds}ms detected={result.DetectedCount}");
foreach (PaddleOcrLine line in result.Lines)
{
    Console.WriteLine($"  ({line.Box.X1:F0},{line.Box.Y1:F0}) score={line.RecognitionScore:F4} cls={line.ClassificationLabel}/{line.ClassificationScore:F3} rot={line.AppliedRotationDegrees} {line.Text}");
}
Console.WriteLine("full text:\n" + result.Text);
return 0;

static (byte[], int, int) LoadBmp(string path)
{
    byte[] d = File.ReadAllBytes(path);
    int dataOffset = BitConverter.ToInt32(d, 10);
    int width = BitConverter.ToInt32(d, 18);
    int rawHeight = BitConverter.ToInt32(d, 22);
    int bpp = BitConverter.ToInt16(d, 28);
    if (bpp != 24) throw new NotSupportedException($"bmp {bpp}bpp not supported");
    int height = Math.Abs(rawHeight);
    int stride = (width * 3 + 3) / 4 * 4;
    bool bottomUp = rawHeight > 0;
    byte[] bgr = new byte[width * height * 3];
    for (int y = 0; y < height; y++)
    {
        int srcRow = bottomUp ? height - 1 - y : y;
        Buffer.BlockCopy(d, dataOffset + srcRow * stride, bgr, y * width * 3, width * 3);
    }
    return (bgr, width, height);
}
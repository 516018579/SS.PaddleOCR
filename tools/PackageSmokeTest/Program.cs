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

PaddleOcrOptions options = new();
bool noCls = args.Contains("--no-cls");
bool official = args.Contains("--official");
if (noCls)
{
    options = new PaddleOcrOptions { UseDirectionClassification = false };
}
else if (official)
{
    // PaddleOCR 3.x defaults for PP-OCRv5 det + textline orientation.
    options = new PaddleOcrOptions
    {
        Detector = new PaddleOcrDetectorOptions
        {
            BitmapThreshold = 0.3f,
            BoxThreshold = 0.6f,
            UnclipRatio = 1.5f,
            MaxCandidates = 1000,
        },
        Recognizer = new PaddleOcrRecognizerOptions { AdaptiveWidth = false, TargetWidth = 320 },
    };
}

using PaddleOcrAll ocr = await PaddleOcrAll.LoadAsync(bundle, options);
var sw = System.Diagnostics.Stopwatch.StartNew();
PaddleOcrResult result = ocr.Run(bgr, w, h);
sw.Stop();
Console.WriteLine($"ocr {sw.ElapsedMilliseconds}ms detected={result.DetectedCount}");
foreach (PaddleOcrLine line in result.Lines)
{
    Console.WriteLine($"  ({line.Box.X1:F0},{line.Box.Y1:F0}) {line.Box.X2 - line.Box.X1:F0}x{line.Box.Y2 - line.Box.Y1:F0} score={line.RecognitionScore:F4} cls={line.ClassificationLabel}/{line.ClassificationScore:F3} rot={line.AppliedRotationDegrees} {line.Text}");
}
Console.WriteLine("full text:\n" + result.Text);

if (args.Contains("--cls-probe"))
{
    using PaddleOcrClassifier classifier = new(bundle.Classification!.OpenRead());
    foreach (PaddleOcrLine line in result.Lines)
    {
        PaddleOcrDetectionBox b = line.Box;
        int x0 = (int)Math.Max(0, Math.Min(Math.Min(b.X1, b.X2), Math.Min(b.X3, b.X4)));
        int y0 = (int)Math.Max(0, Math.Min(Math.Min(b.Y1, b.Y2), Math.Min(b.Y3, b.Y4)));
        int x1 = (int)Math.Min(w - 1, Math.Max(Math.Max(b.X1, b.X2), Math.Max(b.X3, b.X4)));
        int y1 = (int)Math.Min(h - 1, Math.Max(Math.Max(b.Y1, b.Y2), Math.Max(b.Y3, b.Y4)));
        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
        byte[] crop = new byte[cw * ch * 3];
        for (int y = 0; y < ch; y++)
            Buffer.BlockCopy(bgr, (y0 + y) * w * 3 + x0 * 3, crop, y * cw * 3, cw * 3);
        PaddleOcrClassificationResult upright = classifier.Classify(crop, cw, ch);
        byte[] vflip = new byte[crop.Length];
        byte[] hflip = new byte[crop.Length];
        byte[] rot90 = new byte[crop.Length];
        for (int y = 0; y < ch; y++)
        {
            for (int x = 0; x < cw; x++)
            {
                int src = (y * cw + x) * 3;
                Buffer.BlockCopy(crop, src, vflip, ((ch - 1 - y) * cw + x) * 3, 3);
                Buffer.BlockCopy(crop, src, hflip, (y * cw + (cw - 1 - x)) * 3, 3);
            }
        }
        // rot90 in-plane: dst(x=ch-1-y, y=x)
        int rw = ch, rh = cw;
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
                Buffer.BlockCopy(crop, (y * cw + x) * 3, rot90, ((x * rw + (ch - 1 - y)) * 3), 3);
        byte[] flipped = (byte[])crop.Clone();
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
                for (int c = 0; c < 3; c++)
                    flipped[((ch - 1 - y) * cw + (cw - 1 - x)) * 3 + c] = crop[(y * cw + x) * 3 + c];
        PaddleOcrClassificationResult upsideDown = classifier.Classify(flipped, cw, ch);
        PaddleOcrClassificationResult vf = classifier.Classify(vflip, cw, ch);
        PaddleOcrClassificationResult hf = classifier.Classify(hflip, cw, ch);
        PaddleOcrClassificationResult r9 = classifier.Classify(rot90, rw, rh);
        Console.WriteLine($"  cls-probe {cw}x{ch} up={upright.Label}/{upright.Score:F3} " +
            $"rot180={upsideDown.Label}/{upsideDown.Score:F3} vflip={vf.Label}/{vf.Score:F3} " +
            $"hflip={hf.Label}/{hf.Score:F3} rot90={r9.Label}/{r9.Score:F3}  \"{line.Text}\"");
    }
}

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
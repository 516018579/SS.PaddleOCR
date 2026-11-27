using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SS.PaddleOCR;
using SS.PaddleOCR.OnnxSharp;

namespace OrtRef;

internal static class Program
{
    internal static readonly string ModelPath = Repo.Path(@"models\korean_v5_rec.onnx");
    internal static readonly string DictPath = Repo.Path(@"models\korean_v5_keys.txt");

    internal static int DumpRes(string dllPath, string? outPath)
    {
        var asm = System.Reflection.Assembly.LoadFrom(dllPath);
        foreach (string n in asm.GetManifestResourceNames())
        {
            Console.WriteLine("res " + n);
            if (outPath is not null && n.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            {
                using var s = asm.GetManifestResourceStream(n)!;
                using var f = File.Create(outPath);
                s.CopyTo(f);
                Console.WriteLine($"wrote {outPath} {f.Length} bytes");
            }
        }
        return 0;
    }

    internal static int RunAll(string bmpPath)
    {
        string detPath = Repo.Path(@"models\korean_v5_det.onnx");
        if (Environment.GetEnvironmentVariable("ALL_DET") is { Length: > 0 } d) detPath = d;
        SS.PaddleOCR.OnnxSharp.Model? cls = null;
        if (Environment.GetEnvironmentVariable("ALL_NOCLS") != "1")
        {
            using var cs = SS.PaddleOCR.Models.TextLineOrientation.TextLineOrientationModel.Provider.OpenRead();
            cls = SS.PaddleOCR.OnnxSharp.Model.Load(cs);
        }
        string recPath = ModelPath;
        if (Environment.GetEnvironmentVariable("ALL_REC") is { Length: > 0 } recOverride) recPath = recOverride;
        string dictPath = DictPath;
        if (Environment.GetEnvironmentVariable("ALL_DICT") is { Length: > 0 } dictOverride) dictPath = dictOverride;
        using var set = PaddleOcrModelSet.Load(SS.PaddleOCR.OnnxSharp.Model.Load(File.ReadAllBytes(detPath)), cls, SS.PaddleOCR.OnnxSharp.Model.Load(File.ReadAllBytes(recPath)), File.ReadAllBytes(dictPath));
        using var all = new PaddleOcrAll(set);
        byte[] bgr = LoadBmp(bmpPath, out int w, out int h);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = all.Run(bgr, w, h);
        sw.Stop();
        Console.WriteLine($"det+rec {w}x{h} detected={r.DetectedCount} resize={r.DetectorResizedWidth}x{r.DetectorResizedHeight} in {sw.ElapsedMilliseconds}ms");
        foreach (var line in r.Lines)
            Console.WriteLine($"  [{line.Box.X1,6:F0},{line.Box.Y1,5:F0}] score={line.RecognitionScore:F4} cls={line.ClassificationLabel}/{line.ClassificationScore:F3} rot={line.AppliedRotationDegrees} w={line.RecognitionContentWidth} {line.Text}");
        return 0;
    }

    internal static int RecPreprocess(byte[] bgr, int w, int h, int tw, Span<float> output, bool nhwc)
        => PPOCRPreprocess.Rec(bgr, w, h, w * 3, tw, output, nhwc, ImagePixelFormat.Bgr24);

    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "ladder") return Ladder.Run(args[1]);
        if (args.Length >= 2 && args[0] == "map") return MapProbe.Run(args[1]);
        if (args.Length >= 2 && args[0] == "all") return RunAll(args[1]);
        if (args.Length >= 2 && args[0] == "res") return DumpRes(args[1], args.Length > 2 ? args[2] : null);
        if (args.Length >= 2 && args[0] == "crop") return CropProbe.Run(args[1]);
        if (args.Length >= 2 && args[0] == "cls") return ClsProbe.Run(args[1], args.Length > 2 ? args[2] : Repo.Path(@"models\cls.onnx"));
        if (args.Length >= 1 && args[0] == "diag") return Ladder.Diag();
        string[] images = args.Length > 0
            ? args
            : new[]
            {
                Repo.Path(@"tools\OrtRefProbe\samples\ko_real_1.bmp"),
                Repo.Path(@"tools\OrtRefProbe\samples\ko_real_2.bmp"),
            };

        string[] labels = LoadDictionary(DictPath);
        Console.WriteLine($"dict={labels.Length}");

        byte[] modelBytes = File.ReadAllBytes(ModelPath);
        Model model = Model.Load(modelBytes);

        using var ortOptions = new SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
        ortOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        using var ortSession = new InferenceSession(modelBytes, ortOptions);
        string inputName = ortSession.InputMetadata.Keys.First();
        Console.WriteLine($"ort input='{inputName}' outputs=[{string.Join(",", ortSession.OutputMetadata.Keys)}]");


        foreach (string path in images)
        {
            if (!File.Exists(path)) { Console.WriteLine($"missing {path}"); continue; }
            byte[] bgr = LoadBmp(path, out int w, out int h);
            int tw = TargetWidth(w, h);
            Console.WriteLine();
            Console.WriteLine($"=== {Path.GetFileName(path)} {w}x{h} targetWidth={tw}");

            // --- engine: exact same preprocess code as the recognizer, read the tensor back ---
            var compiled = new CompiledModel(model, intraOpThreads: 1, releaseInput: false);
            using var sess = new CpuOcrSession(compiled);
            sess.Reshape(new[] { 1, 3, 48, tw });
            bool nhwc = sess.InputIsNhwc;
            int rw = PPOCRPreprocess.Rec(bgr, w, h, w * 3, tw, sess.InputData, nhwc, ImagePixelFormat.Bgr24);
            float[] engineInput = sess.InputData.ToArray();
            string engineInputHash = Hash(engineInput);

            float[] engineOut = sess.RunInternal(sess.InputData).ToArray();
            int[] engineShape = sess.OutputShape.Dimensions;
            Console.WriteLine($"engine  inputIsNhwc={nhwc} resizedW={rw} inHash={engineInputHash}");
            Console.WriteLine($"engine  outShape=[{string.Join(",", engineShape)}]");

            // --- ORT: feed the byte-identical tensor (converted to NCHW if engine used NHWC) ---
            float[] nchw = nhwc ? NhwcToNchw(engineInput, 3, 48, tw) : engineInput;
            var dense = new DenseTensor<float>(nchw, new[] { 1, 3, 48, tw });
            var feeds = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, dense) };
            float[] ortOut;
            int[] ortShape;
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = ortSession.Run(feeds))
            {
                Tensor<float> t = results.First().AsTensor<float>();
                ortShape = t.Dimensions.ToArray();
                ortOut = t.ToArray();
            }
            Console.WriteLine($"ort     outShape=[{string.Join(",", ortShape)}]");

            int tEngine = engineShape[^2], cEngine = engineShape[^1];
            int tOrt = ortShape[^2], cOrt = ortShape[^1];
            Console.WriteLine($"timeSteps engine={tEngine} ort={tOrt}  classes engine={cEngine} ort={cOrt}");

            if (tEngine != tOrt || cEngine != cOrt)
            {
                Console.WriteLine("SHAPE MISMATCH - comparing first min(t)*min(c) block only");
            }
            int ti = Math.Min(tEngine, tOrt), c = Math.Min(cEngine, cOrt);
            double sum = 0; int n = 0; int argmaxDiff = 0; float maxAbs = 0;
            for (int i = 0; i < ti; i++)
            {
                int bestE = 0, bestO = 0; float be = float.NegativeInfinity, bo = float.NegativeInfinity;
                for (int k = 0; k < c; k++)
                {
                    float a = engineOut[i * cEngine + k];
                    float b = ortOut[i * cOrt + k];
                    float d = Math.Abs(a - b);
                    if (d > maxAbs) maxAbs = d;
                    sum += d; n++;
                    if (a > be) { be = a; bestE = k; }
                    if (b > bo) { bo = b; bestO = k; }
                }
                if (bestE != bestO) argmaxDiff++;
            }
            Console.WriteLine($"diff    meanAbs={sum / Math.Max(1, n):E3} maxAbs={maxAbs:E3} argmaxMismatch={argmaxDiff}/{ti}");

            Console.WriteLine($"engine  -> {Decode(engineOut, tEngine, cEngine, labels, out float se)} score={se:F4}");
            Console.WriteLine($"ort     -> {Decode(ortOut, tOrt, cOrt, labels, out float so)} score={so:F4}");

            // hypothesis tests: is the engine's output buffer transposed relative to the logical [1,T,C]?
            if (tEngine == tOrt && cEngine == cOrt)
            {
                double e1 = 0, e2 = 0;
                for (int i = 0; i < ti; i++)
                    for (int k = 0; k < c; k++)
                    {
                        float flat = engineOut[i * c + k];
                        e1 += Math.Abs(flat - ortOut[(long)k * ti + i]);       // engine[i,k] = ort[k,i]
                        e2 += Math.Abs(flat - ortOut[(long)i * c + k]);
                    }
                Console.WriteLine($"hypo transpose meanAbs={e1 / ((double)ti * c):E3}   identity meanAbs={e2 / ((double)ti * c):E3}");
            }

            // per-step localization
            int big = 0, mid = 0, small = 0;
            for (int i = 0; i < ti; i++)
            {
                int bestE = -1, bestO = -1; float be = -1, bo = -1, stepMax = 0;
                int stepBig = 0;
                for (int k = 0; k < c; k++)
                {
                    float a = engineOut[i * cEngine + k];
                    float b = ortOut[i * cOrt + k];
                    float d = Math.Abs(a - b);
                    if (d > stepMax) stepMax = d;
                    if (d > 0.01f) stepBig++;
                    if (d > 0.5f) big++; else if (d > 0.01f) mid++; else if (d > 1e-4f) small++;
                    if (a > be) { be = a; bestE = k; }
                    if (b > bo) { bo = b; bestO = k; }
                }
                Console.WriteLine($"  step {i,3}: eng={Describe(bestE, labels, cEngine),-6} p={be:F4}  ort={Describe(bestO, labels, cOrt),-6} p={bo:F4}  maxDiff={stepMax:E2} bigClasses={stepBig}");
            }
            Console.WriteLine($"element diff buckets: >0.5:{big} >0.01:{mid} >1e-4:{small} total={n}");

            // list the wrong indices for the first rows to expose structure
            for (int i = 0; i < Math.Min(3, ti); i++)
            {
                var bad = new List<string>();
                for (int k = 0; k < c; k++)
                {
                    float a = engineOut[i * cEngine + k], b = ortOut[i * cOrt + k];
                    if (Math.Abs(a - b) > 0.008f) bad.Add($"k={k}(e={a:F4},o={b:F4})");
                }
                Console.WriteLine($"  row {i} bad[{bad.Count}]: {string.Join(" ", bad.Take(16))}");
            }

            // is the divergence a column permutation (sorted multisets equal) or genuinely different values?
            for (int i = 0; i < Math.Min(4, ti); i++)
            {
                var ea = new float[c]; var oa = new float[c];
                Array.Copy(engineOut, i * cEngine, ea, 0, c);
                Array.Copy(ortOut, i * cOrt, oa, 0, c);
                Array.Sort(ea); Array.Sort(oa);
                float sm = 0; int smc = 0;
                for (int k = 0; k < c; k++) { sm += Math.Abs(ea[k] - oa[k]); if (Math.Abs(ea[k] - oa[k]) > 0.01f) smc++; }
                Console.WriteLine($"  sortedRow {i}: meanAbs={sm / c:E2} bigCount={smc}  engTop3={Top3(engineOut, i * cEngine, c)}  ortTop3={Top3(ortOut, i * cOrt, c)}");
                // where did the engine's largest value go in ort?
                int eb = 0; for (int k = 1; k < c; k++) if (engineOut[i * cEngine + k] > engineOut[i * cEngine + eb]) eb = k;
                float ev = engineOut[i * cEngine + eb];
                int jb = -1; float bestJ = 0;
                for (int k = 0; k < c; k++) { float d = Math.Abs(ortOut[i * cOrt + k] - ev); if (jb < 0 || d < bestJ) { jb = k; bestJ = d; } }
                Console.WriteLine($"    engMax idx={eb} v={ev:F4} -> closest ort idx={jb} v={ortOut[i * cOrt + jb]:F4} | idx {eb} in ort={ortOut[i * cOrt + eb]:F4}");
            }

            // --- public recognizer for reference (its own path) ---
            using var rec = new PaddleOcrRecognizer(model, File.ReadAllBytes(DictPath));
            PaddleOcrRecognitionResult r = rec.Recognize(bgr, w, h, w * 3, ImagePixelFormat.Bgr24);
            Console.WriteLine($"public  -> {r.Text} score={r.Score:F4} emitted={r.EmittedCount} resizedW={r.ResizedWidth} steps={r.TimeSteps}");
        }

        return 0;
    }

    internal static string Hash(float[] data)
    {
        uint h = 2166136261;
        byte[] bytes = new byte[data.Length * 4];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        foreach (byte b in bytes) { h ^= b; h *= 16777619; }
        return h.ToString("x8");
    }

    internal static int TargetWidth(int w, int h)
    {
        long scaled = ((long)48 * w + h - 1) / h;
        long bucket = ((scaled + 31) / 32) * 32;
        return (int)Math.Max(bucket, 32L);
    }

    internal static float[] NhwcToNchw(float[] src, int c, int h, int w)
    {
        var dst = new float[src.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                for (int k = 0; k < c; k++)
                    dst[(k * h + y) * w + x] = src[(y * w + x) * c + k];
        return dst;
    }

    private static string Decode(float[] probs, int t, int c, string[] labels, out float score)
    {
        var sb = new StringBuilder();
        var kept = new List<float>();
        int prev = -1;
        for (int i = 0; i < t; i++)
        {
            int best = 0; float bp = float.NegativeInfinity;
            for (int k = 0; k < c; k++)
            {
                float p = probs[i * c + k];
                if (p > bp) { bp = p; best = k; }
            }
            if (best != 0 && best != prev)
            {
                sb.Append(CharFor(best, labels, c));
                kept.Add(bp);
            }
            prev = best;
        }
        score = kept.Count == 0 ? 0 : kept.Average();
        return sb.ToString();
    }

    private static string Top3(float[] data, int offset, int c)
    {
        var idx = new List<int>(3);
        for (int k = 0; k < c; k++)
        {
            if (idx.Count < 3) { idx.Add(k); idx.Sort((x, y) => data[offset + y].CompareTo(data[offset + x])); continue; }
            if (data[offset + k] > data[offset + idx[2]])
            {
                idx[2] = k;
                idx.Sort((x, y) => data[offset + y].CompareTo(data[offset + x]));
            }
        }
        return string.Join(",", idx.Select(k => $"{k}:{data[offset + k]:F3}"));
    }

    private static string Describe(int index, string[] labels, int classCount)
    {
        string ch = CharFor(index, labels, classCount);
        return index == 0 ? "blank" : $"{ch}({index})";
    }

    private static string CharFor(int index, string[] labels, int classCount)
    {
        if (index <= 0) return "";
        if (index <= labels.Length) return labels[index - 1];
        if (index == classCount - 1) return " ";
        return $"[{index}]";
    }

    internal static string[] LoadDictionary(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);
        if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        int count = lines.Length;
        if (count > 0 && lines[count - 1].Length == 0) count--;
        var list = new List<string>(count);
        for (int i = 0; i < count; i++) list.Add(lines[i]);
        return list.ToArray();
    }

    internal static byte[] LoadBmp(string path, out int width, out int height)
    {
        byte[] data = File.ReadAllBytes(path);
        int offset = BitConverter.ToInt32(data, 10);
        width = BitConverter.ToInt32(data, 18);
        height = BitConverter.ToInt32(data, 22);
        int bpp = BitConverter.ToInt16(data, 28);
        if (bpp != 24) throw new NotSupportedException($"bmp {bpp}bpp not supported");
        bool bottomUp = height > 0;
        height = Math.Abs(height);
        int stride = (width * 3 + 3) / 4 * 4;
        var bgr = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            int srcRow = offset + (bottomUp ? height - 1 - y : y) * stride;
            Buffer.BlockCopy(data, srcRow, bgr, y * width * 3, width * 3);
        }
        return bgr;
    }
}











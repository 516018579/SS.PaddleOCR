using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SS.PaddleOCR;
using Sdcb.SimdOnnx.Engine;
using SS.PaddleOCR.OnnxSharp;
using Compiled = SS.PaddleOCR.OnnxSharp.CompiledModel;
using OrtSession = Microsoft.ML.OnnxRuntime.InferenceSession;

namespace OrtRef;

/// <summary>
/// Layout-invariant ladder comparison: exposes a set of intermediate tensors as ORT graph outputs
/// (by appending ValueInfoProto entries to the GraphProto) and compares per-tensor statistics against
/// the SimdOnnx engine, which can be truncated at any tensor with CpuModel.WithOutput(name).
/// Both sides run the RAW graph (SIMDONNX_NOREWRITE=1, SIMDONNX_NOFUSE=1, ORT optimizations off) so
/// node i is the same computation on both sides.
/// </summary>
internal static class Ladder
{
    public static int Run(string bmpPath)
    {
        Environment.SetEnvironmentVariable("SIMDONNX_NOREWRITE", "1");
        Environment.SetEnvironmentVariable("SIMDONNX_NOFUSE", "1");
        Environment.SetEnvironmentVariable("SIMDONNX_THREADS", "1");

        string modelPath = Environment.GetEnvironmentVariable("LADDER_MODEL") is { Length: > 0 } mp ? mp : Program.ModelPath;
        byte[] modelBytes = File.ReadAllBytes(modelPath);
        List<(string Op, string Name, string[] Outs, string[] Ins)> nodes = ReadNodes(modelBytes);
        Console.WriteLine($"raw graph nodes={nodes.Count}");

        var pick = new List<int>();
        if (int.TryParse(Environment.GetEnvironmentVariable("LADDER_FROM"), out int from))
        {
            int to = int.TryParse(Environment.GetEnvironmentVariable("LADDER_TO"), out int toe) ? toe : nodes.Count - 1;
            int st = int.TryParse(Environment.GetEnvironmentVariable("LADDER_STEP"), out int ste) ? Math.Max(1, ste) : 1;
            for (int i = from; i <= Math.Min(to, nodes.Count - 1); i += st) pick.Add(i);
        }
        else
        {
            for (int i = 0; i < Math.Min(6, nodes.Count); i++) pick.Add(i);
            int step = Math.Max(1, nodes.Count / 18);
            for (int i = 6; i < nodes.Count - 8; i += step) pick.Add(i);
            for (int i = Math.Max(0, nodes.Count - 8); i < nodes.Count; i++) pick.Add(i);
        }
        pick = pick.Distinct()
            .Where(i => nodes[i].Outs.Length > 0 && nodes[i].Outs[0].Length > 0)
            .ToList();

        // graph.output names must be unique across input/output/value_info: skip already-declared names
        HashSet<string> declared = GraphDeclaredNames(modelBytes);
        Console.WriteLine($"graph declares {declared.Count} input/output/value_info names");
        var nodeNames = new HashSet<string>(nodes.Select(n => n.Name).Where(n => n.Length > 0), StringComparer.Ordinal);
        Console.WriteLine($"graph node names={nodeNames.Count}; {nodeNames.Count(n => nodes.Any(x => x.Outs.Length > 0 && x.Outs[0] == n))} of them equal a node output name");
        var outCount = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in nodes)
        {
            foreach (string o in n.Outs)
            {
                if (o.Length == 0) continue;
                outCount.TryGetValue(o, out int c);
                outCount[o] = c + 1;
            }
        }
        Console.WriteLine($"distinct value names={outCount.Count}, defined more than once={outCount.Count(kv => kv.Value > 1)}");
        pick = pick.Where(i => !declared.Contains(nodes[i].Outs[0])
                            && !nodeNames.Contains(nodes[i].Outs[0])
                            && nodes[i].Op != "Constant"
                            && outCount.TryGetValue(nodes[i].Outs[0], out int oc) && oc == 1).ToList();

        int maxCand = int.TryParse(Environment.GetEnvironmentVariable("LADDER_MAX"), out int envMax) ? envMax : int.MaxValue;
        if (pick.Count > maxCand) pick = pick.Take(maxCand).ToList();
        List<string> names = pick.Select(i => nodes[i].Outs[0]).ToList();
        if (Environment.GetEnvironmentVariable("LADDER_NAMES") is { Length: > 0 } forced)
        {
            names = forced.Split(',').ToList();
            Console.WriteLine("forced names: " + string.Join(",", names));
        }
        Console.WriteLine($"candidates={names.Count}: {string.Join(", ", pick.Select(i => i + ":" + nodes[i].Op))}");
        if (Environment.GetEnvironmentVariable("LADDER_LIST") == "1")
        {
            foreach (var n in nodes.Take(24)) Console.WriteLine($"  node {n.Op} name={n.Name} out={string.Join("|", n.Outs)}");
        }


        byte[] bgr = Program.LoadBmp(bmpPath, out int w, out int h);
        int tw = Program.TargetWidth(w, h);
        Console.WriteLine($"image {Path.GetFileName(bmpPath)} {w}x{h} targetWidth={tw}");

        Model model = Model.Load(modelBytes);
        using var compiled = new Compiled(model, intraOpThreads: 1, releaseInput: false);
        string patchedPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "paddleocr_ladder_patched.onnx");
        var shaped = new List<(string Name, int[] Shape)>();
        foreach (string nm in names)
        {
            int[]? sh = null;
            try { sh = compiled.Full.WithOutput(nm).OutputShape(new[] { 1, 3, 48, tw }); } catch { }
            shaped.Add((nm, sh ?? Array.Empty<int>()));
        }
        var (patched, outMap) = AppendGraphOutputs(modelBytes, shaped);
        File.WriteAllBytes(patchedPath, patched);
        Console.WriteLine($"patched model written {patched.Length} bytes -> {patchedPath}");
        try
        {
            Model pv = Model.Load(patched);
            Console.WriteLine($"engine parses patched model: nodes={pv.Onnx.Nodes.Count} (original {nodes.Count})");
            using var pc = new Compiled(pv, intraOpThreads: 1, releaseInput: false);
            int[] lsh = pc.Full.WithOutput("ladder_out_0").OutputShape(new[] { 1, 3, 48, tw });
            Console.WriteLine($"ladder_out_0 shape = [{string.Join(",", lsh)}]");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"engine rejected patched model: {ex.GetType().Name}: {ex.Message}");
        }
        using var cpu = new CpuSession(compiled.Full);
        cpu.Reshape(new[] { 1, 3, 48, tw });
        bool nhwc = compiled.InputIsNhwc;
        Program.RecPreprocess(bgr, w, h, tw, cpu.Input, nhwc);
        float[] engineInput = cpu.Input.ToArray();
        float[] nchw = nhwc ? Program.NhwcToNchw(engineInput, 3, 48, tw) : engineInput;
        var dense = new DenseTensor<float>(nchw, new[] { 1, 3, 48, tw });

        OrtSession? ort = null;
        var skip = new HashSet<int>();
        for (int attempt = 0; attempt < 120; attempt++)
        {
            var live = shaped.Select((s, i) => (s, i)).Where(t => !skip.Contains(t.i)).ToList();
            (patched, outMap) = AppendGraphOutputs(modelBytes, live.Select(t => t.s).ToList());
            var opts = new SessionOptions
            {
                LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL,
            };
            try
            {
                ort = new OrtSession(patched, opts);
                break;
            }
            catch (OnnxRuntimeException ex)
            {
                string msg = ex.Message;
                Console.WriteLine($"  init failed (attempt {attempt}, live {live.Count}): {msg.Split('\n')[0]}");
                int? idx = null;
                Match m = Regex.Match(msg, @"output arg \(ladder_out_(\d+)\)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int k) && k < live.Count) idx = live[k].i;
                else if (BadName(msg) is { } bad)
                {
                    int at = live.FindIndex(t => t.s.Name == bad);
                    if (at >= 0) idx = live[at].i;
                }
                if (idx is null) { Console.WriteLine("  cannot attribute failure - aborting"); return 3; }
                Console.WriteLine($"    dropping {live.First(t => t.i == idx.Value).s.Name}");
                skip.Add(idx.Value);
                opts.Dispose();
            }
        }
        Console.WriteLine($"patch settled with {shaped.Count - skip.Count} tensors -> {patched.Length} bytes");
        File.WriteAllBytes(patchedPath, patched);
        if (ort is null) { Console.WriteLine("could not build a valid patched model"); return 3; }var ortVals = new Dictionary<string, (int[] Shape, float[] Data)>(StringComparer.Ordinal);
        using (ort)
        {
        string inputName = ort!.InputMetadata.Keys.First();
        var feeds = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, dense) };
        try
        {
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> res = ort.Run(feeds);
            foreach (DisposableNamedOnnxValue v in res)
            {
                Tensor<float> t = v.AsTensor<float>();
                ortVals[v.Name] = (t.Dimensions.ToArray(), t.ToArray());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ORT run failed: {ex.GetType().Name}: {ex.Message}");
            return 2;
        }
        Console.WriteLine($"ORT fetched {ortVals.Count} tensors");
        }

        Console.WriteLine();
        Console.WriteLine(string.Format("{0,-5} {1,-12} {2,-12} {3,-40} {4,-40} {5}", "#", "op", "tensor", "engine", "ort", "verdict"));
        int firstDiverged = -1;
        var liveFinal = shaped.Where((s, i) => !skip.Contains(i)).ToList();
        for (int k = 0; k < liveFinal.Count; k++)
        {
            string name = liveFinal[k].Name;
            int idx = nodes.FindIndex(x => x.Outs.Length > 0 && x.Outs[0] == name);
            string inNames = idx >= 0 ? string.Join("|", nodes[idx].Ins) : "?";
            if (!ortVals.TryGetValue(outMap[name], out var ov))
            {
                Console.WriteLine($"{idx,-5} {nodes[idx].Op,-12} {Short(name),-12} (ort missing)");
            if (Environment.GetEnvironmentVariable("LADDER_DETAIL") is { Length: > 0 } dn && name.Contains(dn))
            {
                // nothing yet: engine value unavailable without a variant; handled below
            }
                continue;
            }
            float[] ev;
            try
            {
                var variant = compiled.Full.WithOutput(name);
                ev = cpu.Run(variant).ToArray();
                if (Environment.GetEnvironmentVariable("LADDER_DETAIL") is { Length: > 0 } dn && name.Contains(dn))
                {
                    Detail(name, ev, cpu.OutputShape(variant), ov.Data, ov.Shape);
                }
            if (Environment.GetEnvironmentVariable("LADDER_DUMPDIR") is { Length: > 0 } dd)
            {
                Directory.CreateDirectory(dd);
                string safe = name.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
                File.WriteAllBytes(Path.Combine(dd, safe + ".eng.f32"), ToBytes(ev));
                File.WriteAllBytes(Path.Combine(dd, safe + ".ort.f32"), ToBytes(ov.Data));
                File.WriteAllText(Path.Combine(dd, safe + ".shape.txt"), $"eng=[{string.Join(",", cpu.OutputShape(variant))}] ort=[{string.Join(",", ov.Shape)}]");
            }
                if (ev.Length != ov.Data.Length || !SameStats(ev, ov.Data))
                {
                    Console.WriteLine($"  >> engine first5=[{string.Join(",", ev.Take(5).Select(v => v.ToString("F4")))}]  ort first5=[{string.Join(",", ov.Data.Take(5).Select(v => v.ToString("F4")))}]");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{idx,-5} {nodes[idx].Op,-12} {Short(name),-12} ENGINE-ERROR {ex.GetType().Name}: {ex.Message}");
                continue;
            }
            int multiset = 0;
            if (ev.Length == ov.Data.Length && ev.Length > 0)
            {
                float[] a1 = (float[])ev.Clone(), b1 = (float[])ov.Data.Clone();
                Array.Sort(a1); Array.Sort(b1);
                for (int q = 0; q < a1.Length; q++) if (Math.Abs(a1[q] - b1[q]) > 1e-5f) multiset++;
            }
            else multiset = -1;
            bool diverged = ev.Length != ov.Data.Length || !SameStats(ev, ov.Data) || multiset > 0;
            if (diverged && firstDiverged < 0) firstDiverged = idx;
            Console.WriteLine($"{idx,-5} {nodes[idx].Op,-12} {Short(name),-12} {Stats(ev),-40} {Stats(ov.Data),-40} {(diverged ? "DIVERGED" : "ok")} ms={multiset} in={inNames}");
        }

        Console.WriteLine();
        Console.WriteLine(firstDiverged < 0
            ? "no divergence in the sampled ladder"
            : $"FIRST DIVERGENCE at node #{firstDiverged} ({nodes[firstDiverged].Op})");
        return 0;
    }

    // Permutation-aware comparison for tensors whose shape allows a deterministic NHWC<->NCHW read:
    // stats matching is necessary but not sufficient (a pure permutation keeps n/avg/min/max intact).
    private static byte[] ToBytes(float[] a)
    {
        var r = new byte[a.Length * 4];
        Buffer.BlockCopy(a, 0, r, 0, r.Length);
        return r;
    }
    private static void Detail(string name, float[] eng, int[] engShape, float[] ort, int[] ortShape)
    {
        int n = Math.Min(eng.Length, ort.Length);
        Console.WriteLine($"  detail {name}: engShape=[{string.Join(",", engShape)}] ortShape=[{string.Join(",", ortShape)}] n={n}");
        int idMismatch = 0;
        float idMax = 0;
        for (int i = 0; i < n; i++) { float d = Math.Abs(eng[i] - ort[i]); if (d > 1e-5f) idMismatch++; if (d > idMax) idMax = d; }
        Console.WriteLine($"    identity: mismatch={idMismatch}/{n} maxAbs={idMax:E3}");
        if (engShape.Length == 4 && ortShape.Length == 4 && engShape[0] == ortShape[0] && engShape[1] == ortShape[1]
            && engShape[2] == ortShape[2] && engShape[3] == ortShape[3])
        {
            int N = engShape[0], C = engShape[1], H = engShape[2], W = engShape[3];
            if ((long)N * C * H * W == n)
            {
                int cwMismatch = 0;
                float cwMax = 0;
                for (int nn = 0; nn < N; nn++)
                for (int c = 0; c < C; c++)
                for (int h = 0; h < H; h++)
                for (int w = 0; w < W; w++)
                {
                    float e = eng[((nn * H + h) * W + w) * C + c]; // engine read as NHWC
                    float o = ort[((nn * C + c) * H + h) * W + w];   // ort as NCHW
                    float d = Math.Abs(e - o);
                    if (d > 1e-5f) cwMismatch++;
                    if (d > cwMax) cwMax = d;
                }
                Console.WriteLine($"    nhwc->nchw: mismatch={cwMismatch}/{n} maxAbs={cwMax:E3}");
            }
        }
        float[] es = (float[])eng.Clone(), os = (float[])ort.Clone();
        Array.Sort(es); Array.Sort(os);
        int sortedMismatch = 0;
        for (int i = 0; i < n; i++) if (Math.Abs(es[i] - os[i]) > 1e-5f) sortedMismatch++;
        Console.WriteLine($"    sorted multiset: mismatch={sortedMismatch}/{n}");
        Console.WriteLine($"    eng[0..4]=[{string.Join(", ", eng.Take(4).Select(v => v.ToString("F5")))}] ort[0..4]=[{string.Join(", ", ort.Take(4).Select(v => v.ToString("F5")))}]");
    }
    private static string? BadName(string message)
    {

        const string p1 = "Duplicate definition of name (";
        const string p2 = "Duplicate definition-site for (";
        foreach (string prefix in new[] { p1, p2 })
        {
            int p = message.IndexOf(prefix, StringComparison.Ordinal);
            if (p < 0) continue;
            int start = p + prefix.Length;
            int b = message.IndexOf(')', start);
            if (b > start) return message.Substring(start, b - start);
        }
        return null;
    }
    public static int Diag()
    {
        byte[] m = File.ReadAllBytes(Program.ModelPath);
        int pos = 0;
        Console.WriteLine("--- ModelProto top-level fields");
        while (pos < m.Length)
        {
            int start = pos;
            int tag = Varint(m, ref pos);
            int f = tag >> 3, wire = tag & 7;
            if (wire == 2)
            {
                int len = Varint(m, ref pos);
                Console.WriteLine($"field={f} wire=2 start={start} payload={pos} len={len}");
                pos += len;
            }
            else if (wire == 0)
            {
                int v = Varint(m, ref pos);
                Console.WriteLine($"field={f} wire=0 start={start} value={v}");
            }
            else if (wire == 1) { pos += 8; Console.WriteLine($"field={f} wire=1"); }
            else if (wire == 5) { pos += 4; Console.WriteLine($"field={f} wire=5"); }
            else { Console.WriteLine($"field={f} wire={wire} UNEXPECTED at {start}"); return 1; }
        }
        Console.WriteLine($"total={m.Length}");

        var declared = GraphDeclaredNames(m);
        Console.WriteLine("declared: " + string.Join("|", declared));

        foreach (string name in new[] { "zzz_dummy_probe", declared.First() })
        {
            (byte[] p, _) = AppendGraphOutputs(m, new List<(string, int[])> { (name, Array.Empty<int>()) });
            Console.WriteLine($"--- append '{name}' -> {p.Length} bytes (delta {p.Length - m.Length})");
            using var o = new SessionOptions
            {
                LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL,
            };
            try
            {
                using var s = new OrtSession(p, o);
                Console.WriteLine($"    loaded ok, outputs=[{string.Join(",", s.OutputMetadata.Keys)}]");
            }
            catch (Exception ex) { Console.WriteLine($"    {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
        }
        return 0;
    }
    private static string Short(string name) => name.Length <= 12 ? name : name[^12..];

    private static string Stats(float[] d)
    {
        if (d.Length == 0) return "n=0";
        double sumAbs = 0;
        float max = float.NegativeInfinity, min = float.PositiveInfinity;
        foreach (float x in d)
        {
            if (float.IsNaN(x) || float.IsInfinity(x)) return $"n={d.Length} NON-FINITE";
            sumAbs += Math.Abs((double)x);
            if (x > max) max = x;
            if (x < min) min = x;
        }
        return $"n={d.Length} avg={sumAbs / d.Length:F5} max={max:F4} min={min:F4}";
    }

    private static bool SameStats(float[] a, float[] b)
    {
        if (a.Length != b.Length) return false;
        if (a.Length == 0) return true;
        double sa = 0, sb = 0;
        float maxA = float.NegativeInfinity, maxB = float.NegativeInfinity;
        foreach (float x in a) { sa += Math.Abs((double)x); if (x > maxA) maxA = x; }
        foreach (float x in b) { sb += Math.Abs((double)x); if (x > maxB) maxB = x; }
        double avgA = sa / a.Length, avgB = sb / b.Length;
        double scale = Math.Max(1e-6, Math.Max(avgA, avgB));
        return Math.Abs(avgA - avgB) <= 2e-3 * scale && Math.Abs(maxA - maxB) <= 2e-3 * scale;
    }

    // ---------------- protobuf helpers ----------------

    private static HashSet<string> GraphDeclaredNames(byte[] model)
    {
        (int gs, int glen) = FindField(model, 0, model.Length, 7);
        var set = new HashSet<string>(StringComparer.Ordinal);
        int end = gs + glen, pos = gs;
        while (pos < end)
        {
            int tag = Varint(model, ref pos);
            int f = tag >> 3, wire = tag & 7;
            if (wire == 2)
            {
                int len = Varint(model, ref pos);
                if (f is 11 or 12 or 13)
                {
                    (int ns, int nlen) = FindField(model, pos, pos + len, 1);
                    set.Add(Encoding.UTF8.GetString(model, ns, nlen));
                }
                else if (f == 5)
                {
                    // TensorProto: name is field 8 (initializers are definition sites too)
                    (int ns, int nlen) = FindField(model, pos, pos + len, 8);
                    set.Add(Encoding.UTF8.GetString(model, ns, nlen));
                }
                pos += len;
            }
            else if (wire == 0) Varint(model, ref pos);
            else if (wire == 1) pos += 8;
            else if (wire == 5) pos += 4;
            else throw new InvalidDataException("unexpected wire " + wire + " in GraphProto");
        }
        return set;
    }

    private static List<(string Op, string Name, string[] Outs, string[] Ins)> ReadNodes(byte[] model)
    {
        (int gs, int glen) = FindField(model, 0, model.Length, 7);
        var list = new List<(string, string, string[], string[])>();
        int end = gs + glen, pos = gs;
        while (pos < end)
        {
            int tag = Varint(model, ref pos);
            int f = tag >> 3, wire = tag & 7;
            if (wire == 2)
            {
                int len = Varint(model, ref pos);
                if (f == 1)
                {
                    (string op, string name, string[] outs, string[] ins) = ParseNode(model, pos, len);
                    list.Add((op, name, outs, ins));
                }
                pos += len;
            }
            else if (wire == 0) Varint(model, ref pos);
            else if (wire == 1) pos += 8;
            else if (wire == 5) pos += 4;
            else throw new InvalidDataException("unexpected wire " + wire + " in GraphProto");
        }
        return list;
    }

    private static (string Op, string Name, string[] Outs, string[] Ins) ParseNode(byte[] b, int pos, int len)
    {
        int end = pos + len;
        string op = "", nodeName = "";
        var outs = new List<string>();
        var ins = new List<string>();
        while (pos < end)
        {
            int tag = Varint(b, ref pos);
            int f = tag >> 3, wire = tag & 7;
            if (wire == 2)
            {
                int l = Varint(b, ref pos);
                if (f == 1) ins.Add(Encoding.UTF8.GetString(b, pos, l));
                else if (f == 2) outs.Add(Encoding.UTF8.GetString(b, pos, l));
                else if (f == 4) op = Encoding.UTF8.GetString(b, pos, l);
                else if (f == 3) nodeName = Encoding.UTF8.GetString(b, pos, l);
                pos += l;
            }
            else if (wire == 0) Varint(b, ref pos);
            else if (wire == 1) pos += 8;
            else if (wire == 5) pos += 4;
            else throw new InvalidDataException("unexpected wire " + wire + " in NodeProto");
        }
        return (op, nodeName, outs.ToArray(), ins.ToArray());
    }

    private static (byte[] Model, Dictionary<string, string> Map) AppendGraphOutputs(byte[] model, List<(string Name, int[] Shape)> names)
    {
        (int gs, int glen) = FindField(model, 0, model.Length, 7);
        int pos = 0, lenStart = -1, payloadStart = -1, payloadLen = 0;
        while (pos < model.Length)
        {
            int tag = Varint(model, ref pos);
            int f = tag >> 3, wire = tag & 7;
            if (wire == 2)
            {
                lenStart = pos;
                int len = Varint(model, ref pos);
                if (f == 7) { payloadStart = pos; payloadLen = len; break; }
                pos += len;
            }
            else if (wire == 0) Varint(model, ref pos);
            else if (wire == 1) pos += 8;
            else if (wire == 5) pos += 4;
            else throw new InvalidDataException("unexpected wire " + wire + " in ModelProto");
        }
        if (payloadStart != gs || payloadLen != glen) throw new InvalidDataException("graph offsets disagree");

        // The graph reuses tensor names as node names and even as in-place inputs/outputs
        // (paddle2onnx style), so a tensor name cannot be re-declared as a graph output.
        // Instead append `Identity(source)` nodes with fresh unique output names and expose those.
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var go = new MemoryStream();
        go.Write(model, gs, glen); // original graph payload untouched
        for (int i = 0; i < names.Count; i++)
        {
            string newOut = "ladder_out_" + i.ToString();
            map[names[i].Name] = newOut;
            byte[] node = IdentityNode(names[i].Name, newOut, "ladder_node_" + i.ToString());
            go.WriteByte(0x0A); // GraphProto.node (1)
            WriteVarint(go, node.Length);
            go.Write(node, 0, node.Length);
            byte[] vi = ValueInfo(newOut, names[i].Shape);
            go.WriteByte(0x62); // GraphProto.output (12)
            WriteVarint(go, vi.Length);
            go.Write(vi, 0, vi.Length);
        }

        byte[] gnew = go.ToArray();
        using var outp = new MemoryStream();
        outp.Write(model, 0, lenStart);
        WriteVarint(outp, gnew.Length);
        outp.Write(gnew, 0, gnew.Length);
        outp.Write(model, payloadStart + glen, model.Length - payloadStart - glen);
        return (outp.ToArray(), map);
    }

    private static byte[] IdentityNode(string input, string output, string nodeName)
    {
        byte[] i = Encoding.UTF8.GetBytes(input);
        byte[] o = Encoding.UTF8.GetBytes(output);
        byte[] n = Encoding.UTF8.GetBytes(nodeName);
        byte[] op = Encoding.UTF8.GetBytes("Identity");
        using var ms = new MemoryStream();
        ms.WriteByte(0x0A); WriteVarint(ms, i.Length); ms.Write(i, 0, i.Length);
        ms.WriteByte(0x12); WriteVarint(ms, o.Length); ms.Write(o, 0, o.Length);
        ms.WriteByte(0x1A); WriteVarint(ms, n.Length); ms.Write(n, 0, n.Length);
        ms.WriteByte(0x22); WriteVarint(ms, op.Length); ms.Write(op, 0, op.Length);
        return ms.ToArray();
    }

    private static byte[] ValueInfo(string name, int[] shape)
    {
        byte[] n = Encoding.UTF8.GetBytes(name);
        using var tensor = new MemoryStream();
        tensor.WriteByte(0x08); // Tensor.elem_type (1) = FLOAT
        WriteVarint(tensor, 1);
        if (shape.Length > 0)
        {
            using var sp = new MemoryStream();
            foreach (int d in shape)
            {
                using var dim = new MemoryStream();
                dim.WriteByte(0x08); // TensorShapeProto.Dim.dim_value (1)
                WriteVarint(dim, d);
                byte[] db = dim.ToArray();
                sp.WriteByte(0x0A); // TensorShapeProto.dim (1)
                WriteVarint(sp, db.Length);
                sp.Write(db, 0, db.Length);
            }
            byte[] sb = sp.ToArray();
            tensor.WriteByte(0x12); // Tensor.shape (2)
            WriteVarint(tensor, sb.Length);
            tensor.Write(sb, 0, sb.Length);
        }
        byte[] tb = tensor.ToArray();
        using var ms = new MemoryStream();
        ms.WriteByte(0x0A); WriteVarint(ms, n.Length); ms.Write(n, 0, n.Length);            // ValueInfo.name (1)
        ms.WriteByte(0x12); WriteVarint(ms, tb.Length + 2);                                 // ValueInfo.type (2)
        ms.WriteByte(0x0A); WriteVarint(ms, tb.Length); ms.Write(tb, 0, tb.Length);         // TypeProto.tensor_type (1)
        return ms.ToArray();
    }

    private static (int Start, int Len) FindField(byte[] b, int pos, int end, int wanted)
    {
        while (pos < end)
        {
            int tag = Varint(b, ref pos);
            int f = tag >> 3, wire = tag & 7;
            if (wire == 2)
            {
                int len = Varint(b, ref pos);
                if (f == wanted) return (pos, len);
                pos += len;
            }
            else if (wire == 0) Varint(b, ref pos);
            else if (wire == 1) pos += 8;
            else if (wire == 5) pos += 4;
            else throw new InvalidDataException("unexpected wire " + wire);
        }
        throw new InvalidDataException("field " + wanted + " not found");
    }

    private static int Varint(byte[] b, ref int pos)
    {
        long v = 0;
        int shift = 0;
        byte c;
        do
        {
            c = b[pos++];
            v |= (long)(c & 0x7F) << shift;
            shift += 7;
        } while ((c & 0x80) != 0);
        return checked((int)v);
    }

    private static void WriteVarint(Stream s, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80) { s.WriteByte((byte)(v | 0x80)); v >>= 7; }
        s.WriteByte((byte)v);
    }
}


























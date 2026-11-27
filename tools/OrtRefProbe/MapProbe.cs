using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class MapProbe
{
    private static float[] Load(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        float[] a = new float[b.Length / 4];
        Buffer.BlockCopy(b, 0, a, 0, b.Length);
        return a;
    }

    public static int Run(string dir)
    {
        float[] c = Load(Path.Combine(dir, "Concat.4.eng.f32"));
        string e60 = "auto.cast.60.eng.f32";
        float[] a = File.Exists(Path.Combine(dir, e60)) ? Load(Path.Combine(dir, e60)) : null!;
        if (a is null) { Console.WriteLine("need " + e60); return 1; }

        // Build a lookup from value -> indices (rounded to 5 decimals) for candidate sources.
        var srcs = new Dictionary<string, float[]>();
        foreach (string f in Directory.GetFiles(dir, "*.eng.f32"))
        {
            string n = Path.GetFileName(f).Replace(".eng.f32", "");
            if (n == "Concat.4") continue;
            srcs[n] = Load(f);
        }
        Console.WriteLine($"concat n={c.Length}; sources: {string.Join(", ", srcs.Keys)}");

        var lut = new Dictionary<string, Dictionary<long, List<int>>>();
        foreach (var kv in srcs)
        {
            var d = new Dictionary<long, List<int>>();
            for (int i = 0; i < kv.Value.Length; i++)
            {
                long k = (long)Math.Round(kv.Value[i] * 100000.0);
                if (!d.TryGetValue(k, out var lst)) d[k] = lst = new List<int>();
                if (lst.Count < 4) lst.Add(i);
            }
            lut[kv.Key] = d;
        }

        for (int i = 0; i < 24; i++)
        {
            long k = (long)Math.Round(c[i] * 100000.0);
            var hits = new List<string>();
            foreach (var kv in lut)
                if (kv.Value.TryGetValue(k, out var lst))
                    foreach (int j in lst) hits.Add($"{kv.Key}[{j}]");
            Console.WriteLine($"c[{i}]={c[i]:F6} -> {(hits.Count > 0 ? string.Join(" ", hits) : "?")}");
        }

        // Hypothesis scoring: candidate index maps f(i) over the whole array.
        Console.WriteLine();
        Console.WriteLine("hypothesis scores (fraction of positions matching within 1e-5):");
        foreach (var kv in srcs)
        {
            float[] s = kv.Value;
            Score($"c[i] = {kv.Key}[i]", i => i < s.Length ? s[i] : (float?)null, c);
            Score($"c[i] = {kv.Key}[2i]", i => 2 * i < s.Length ? s[2 * i] : (float?)null, c);
            Score($"c[i] = {kv.Key}[i/2*2]", i => i < s.Length ? s[(i / 2) * 2] : (float?)null, c);
            Score($"c[i] = {kv.Key}[i*24]", i => i * 24 < s.Length ? s[i * 24] : (float?)null, c);
            Score($"c[i] = {kv.Key}[i/24 + (i%24)*480]", i =>
            {
                int j = i / 24 + (i % 24) * 480;
                return j < s.Length ? s[j] : (float?)null;
            }, c);
        }
        return 0;
    }

    private static void Score(string label, Func<int, float?> pick, float[] c, int limit = 4000)
    {
        int match = 0, tested = 0;
        for (int i = 0; i < Math.Min(limit, c.Length); i++)
        {
            float? v = pick(i);
            if (v is null) continue;
            tested++;
            if (Math.Abs(c[i] - v.Value) <= 1e-5f) match++;
        }
        Console.WriteLine($"  {label,-45} {match}/{tested}");
    }
}

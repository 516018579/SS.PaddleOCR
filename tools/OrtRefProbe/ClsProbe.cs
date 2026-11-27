using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SS.PaddleOCR;
using OrtRef;

internal static class ClsProbe
{
    public static int Run(string bmpPath, string clsPath)
    {
        Environment.SetEnvironmentVariable("SIMDONNX_THREADS", "1");
        byte[] bgr = Program.LoadBmp(bmpPath, out int w, out int h);
        var model = SS.PaddleOCR.OnnxSharp.Model.Load(File.ReadAllBytes(clsPath));
        using var compiled = new SS.PaddleOCR.OnnxSharp.CompiledModel(model, intraOpThreads: 1, releaseInput: false);
        using var sess = new SS.PaddleOCR.OnnxSharp.CpuOcrSession(compiled);
        int iw = 160, ih = 80;
        sess.Reshape([1, 3, ih, iw]);
        int resized = PPOCRPreprocess.Cls(bgr, w, h, w * 3, sess.InputData, sess.InputIsNhwc, ImagePixelFormat.Bgr24);
        var input = sess.InputData.ToArray();
        var eng = sess.RunInternal(sess.InputData).ToArray();

        float[] nchw = compiled.InputIsNhwc ? Program.NhwcToNchw(input, 3, ih, iw) : input;

        var opts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL, IntraOpNumThreads = 1 };
        using var ort = new InferenceSession(clsPath, opts);
        var dt = new DenseTensor<float>(nchw, new[] { 1, 3, ih, iw });
        var names = new List<string>();
        foreach (var o in ort.OutputMetadata.Keys) names.Add(o);
        using var res = ort.Run(new[] { NamedOnnxValue.CreateFromTensor("x", dt) }, names);
        float[]? ortv = null;
        foreach (var r in res) { ortv = (r.AsTensor<float>()).ToArray(); Console.WriteLine($"ort out {r.Name} shape={string.Join('x', ((Tensor<float>)r.AsTensor<float>()).Dimensions.ToArray())}"); }
        Console.WriteLine($"resized={resized} eng=[{eng[0]:F6},{eng[1]:F6}] ort=[{(ortv is null ? 0 : ortv[0]):F6},{(ortv is null ? 0 : ortv[1]):F6}]");
        Console.WriteLine($"label eng={(eng[1] > eng[0] ? 1 : 0)} ort={((ortv![1] > ortv![0]) ? 1 : 0)}");
        return 0;
    }
}




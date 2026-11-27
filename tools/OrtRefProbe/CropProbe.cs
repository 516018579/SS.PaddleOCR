using System;
using System.IO;
using OrtRef;
using SS.PaddleOCR;
using SS.PaddleOCR.OnnxSharp;

internal static class CropProbe
{
    public static void SaveBmp(string path, byte[] bgr, int w, int h)
    {
        int stride = w * 3;
        int pad = (4 - stride % 4) % 4;
        int rowBytes = stride + pad;
        int size = 54 + rowBytes * h;
        using var f = File.Create(path);
        using var bw = new BinaryWriter(f);
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(size); bw.Write(0); bw.Write(54);
        bw.Write(40); bw.Write(w); bw.Write(h); bw.Write((short)1); bw.Write((short)24);
        bw.Write(0); bw.Write(rowBytes * h); bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);
        byte[] row = new byte[rowBytes];
        for (int y = h - 1; y >= 0; y--)   // bottom-up
        {
            Array.Copy(bgr, y * stride, row, 0, stride);
            for (int i = stride; i < rowBytes; i++) row[i] = 0;
            bw.Write(row);
        }
    }

    public static int Run(string bmpPath)
    {
        Environment.SetEnvironmentVariable("SIMDONNX_THREADS", "1");
        string detPath = Repo.Path(@"models\korean_v5_det.onnx");
        using var det = new PaddleOcrDetector(Model.Load(File.ReadAllBytes(detPath)));
        byte[] bgr = Program.LoadBmp(bmpPath, out int w, out int h);
        var d = det.Detect(bgr, w, h);
        Console.WriteLine($"detected={d.Boxes.Length} resize={d.ResizedWidth}x{d.ResizedHeight}");
        for (int i = 0; i < d.Boxes.Length; i++)
        {
            var box = d.Boxes[i];
            byte[] crop = PPOCRCrop.Extract(bgr, w, h, w * 3, box, out int cw, out int ch);
            string outPath = System.IO.Path.Combine(Repo.Path(@"tools\OrtRefProbe\samples"), $"crop{i}.bmp");
            SaveBmp(outPath, crop, cw, ch);
            Console.WriteLine($"  box{i} ({box.X1:F0},{box.Y1:F0})-({box.X2:F0},{box.Y2:F0})-({box.X3:F0},{box.Y3:F0})-({box.X4:F0},{box.Y4:F0}) crop={cw}x{ch} -> {outPath}");
        }
        return 0;
    }
}


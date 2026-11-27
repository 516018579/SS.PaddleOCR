# tools

两个本地验证工具。它们不参与打包，也不进入 `SS.PaddleOCR.slnx`（`dotnet build` 整个 slnx 时不会被牵连）。

## OrtRefProbe — 引擎数值对拍（ONNX Runtime 参考实现）

定位 SimdOnnx 引擎与 ONNX Runtime 的数值分歧：两侧同时取出任意中间张量，比较与布局无关的统计量
（元素数 / 均值 / 极值 / 排序后多重集），从而找出**第一个**算错的节点。就是靠它定位到
`Concat` 的 axis 与物理布局不匹配（见 `docs/v5_rec.md`）。

```powershell
cd tools\OrtRefProbe
dotnet build -c Debug
dotnet run -c Debug --no-build                      # 默认：rec 结果 + 中间张量对拍
dotnet run -c Debug --no-build -- ladder ..\..\tools\OrtRefProbe\samples\ko_real_1.bmp   # 逐节点爬梯
dotnet run -c Debug --no-build -- all ..\..\tools\PackageSmokeTest\samples\ko_lines2.bmp # det+cls+rec 整页
```

- 模型默认取仓库 `models\` 下的 `korean_v5_rec.onnx` / `korean_v5_keys.txt` / `korean_v5_det.onnx` / `cls.onnx`
  （路径通过构建期注入的 `RepoRoot` 解析，与当前工作目录无关）。
- `lib\Microsoft.ML.OnnxRuntime.dll` + `lib\onnxruntime.dll` 来自 NuGet 包 **Microsoft.ML.OnnxRuntime 1.32.0**
  （托管包 + win-x64 原生库）与 **microsoft.ml.onnxruntime.managed 1.32.0**；这两个二进制未纳入版本管理时，
  按此版本重新解包放到 `lib\` 即可。
- 环境变量：`SIMDONNX_THREADS=1`、`SIMDONNX_NOREWRITE=1`、`SIMDONNX_NOFUSE=1`、`SIMDONNX_DUMP=2` 等
  可切换引擎行为/打印执行计划；`LADDER_FROM/LADDER_TO/LADDER_NAMES/LADDER_DETAIL` 控制爬梯范围。
- 工程 `AssemblyName` 故意设成 `SS.PaddleOCR.Tests`，以命中核心包的 `InternalsVisibleTo`，从而能直接用
  `CompiledModel` / `CpuSession` / `PPOCRPreprocess` 等 internal 类型。

## PackageSmokeTest — 只吃 nupkg 的干净房冒烟测试

不引用任何源码工程，只从 `nupkgs\` 装包（`nuget.config` 里指向 `..\..\nupkgs`），
验证「发布出去的东西真的能用」。

```powershell
cd tools\PackageSmokeTest
dotnet run -c Debug -- ko          # 韩语 PP-OCRv5（默认 ko_lines2.bmp）
dotnet run -c Debug -- zh-small    # 中文 PP-OCRv6 small
dotnet run -c Debug -- zh-medium   # 中文 PP-OCRv6 medium
```

会打印每个 provider 的 Name/Kind/Format/Language/Version/字节数，再跑 `PaddleOcrAll` 并输出逐行结果。
每次跑前建议删掉 `packages\`、`obj\`、`bin\`，确保是真正的全新还原。
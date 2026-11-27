using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OrtRef;

/// <summary>仓库根目录定位（构建时通过 AssemblyMetadata 注入，运行时无需依赖当前工作目录）。</summary>
internal static class Repo
{
    public static readonly string Root = Resolve();

    public static string Path(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relative));

    private static string Resolve()
    {
        string? meta = typeof(Repo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepoRoot")?.Value;
        if (!string.IsNullOrEmpty(meta))
            return System.IO.Path.GetFullPath(meta);

        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "models")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
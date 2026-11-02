using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace InFalsusRating;

public class ExternalSaveManager
{
    private static readonly Dictionary<string, FileActions> FileDic = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetRootPath() => Settings.ConfigRoot;

    public static void RegisterFile(string path, Action onLoaded)
    {
        string relativePath = Path.GetRelativePath(GetRootPath(), path);
        if (relativePath.StartsWith(".."))
        {
            Plugin.Logger.LogError($"[SaveManager] 错误：{path}存在路径逃逸");
            return;
        }

        if (FileDic.ContainsKey(relativePath))
        {
            Plugin.Logger.LogError($"[SaveManager] 错误：{relativePath}已被注册，此次注册将忽略");
            return;
        }

        FileDic[relativePath] = new FileActions
        {
            OnLoaded = (Action)onLoaded.Clone(),
        };
    }

    public static void ExportSave()
    {
        using var plainMemoryStream = new MemoryStream();
        using (var zipArchive = new ZipArchive(plainMemoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (KeyValuePair<string, FileActions> pair in FileDic)
            {
                try
                {
                    string fullPath = Path.Combine(GetRootPath(), pair.Key);
                    string zipInternalPath = pair.Key.Replace(Path.DirectorySeparatorChar, '/');
                    using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);

                    var entry = zipArchive.CreateEntry(zipInternalPath, CompressionLevel.Optimal);
                    using var zipStream = entry.Open();
                    fileStream.CopyTo(zipStream);
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError($"[SaveManager] 错误：无法导出 {pair.Key}");
                    Plugin.Logger.LogError($"[SaveManager] {e}");
                }
            }
        }

        // 有点想写个加密
        string zipPath = Path.Combine(Settings.ExportRoot, "ExportedSave.zip");
        try
        {
            using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write);
            fs.Write(plainMemoryStream.ToArray());
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"[SaveManager] 错误：无法保存到 {zipPath}");
            Plugin.Logger.LogError($"[SaveManager] {e}");
        }
    }

    public static void ImportSave(string saveFilePath)
    {
        // 有点想写个解密
        byte[] data;
        try
        {
            data = File.ReadAllBytes(saveFilePath);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"[SaveManager] 错误：无法读取存档zip {saveFilePath}");
            Plugin.Logger.LogError($"[SaveManager] {e}");
            return;
        }

        using var plainMemoryStream = new MemoryStream(data);
        using var zipArchive = new ZipArchive(plainMemoryStream, ZipArchiveMode.Read);
        try
        {
            zipArchive.ExtractToDirectory(GetRootPath(), true);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"[SaveManager] 错误：无法解压存档zip {saveFilePath}");
            Plugin.Logger.LogError($"[SaveManager] {e}");
            return;
        }

        foreach (KeyValuePair<string, FileActions> pair in FileDic)
        {
            try
            {
                pair.Value.OnLoaded();
            } catch (Exception e)
            {
                Plugin.Logger.LogError($"[SaveManager] 错误：无法加载存档 {pair.Key}");
                Plugin.Logger.LogError($"[SaveManager] {e}");
            }
        }
    }

    struct FileActions
    {
        public Action OnLoaded;
    }
}
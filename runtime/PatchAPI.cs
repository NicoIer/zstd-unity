using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace zstd
{
    public class UnknownException : Exception
    {
    }

    [Serializable]
    public class Manifest
    {
        public string version { get; set; }
        public Dictionary<string, string> fileName2Md5 { get; set; }
    }

    [Serializable]
    public class PatchManifest
    {
        public HashSet<string> patchFiles { get; set; }

        public PatchManifest()
        {
            patchFiles = new HashSet<string>();
        }
    }

    public static class PatchAPI
    {
        /// <summary>
        /// 某个版本的资源文件夹前缀
        /// </summary>
        public const string VersionFolderPrefix = "v";

        public const string ManifestFileName = "manifest.json";
        public const string PatchManifestFileName = "manifest.json";
        public const string PatchFolderName = "patches";
        public const string PatchFileSuffix = "patch";


        public static Manifest GenerateManifest(string folderPath)
        {
            var manifest = new Manifest();
            string folderName = Path.GetFileName(folderPath);
            manifest.version = folderName;

            var dirInfo = new System.IO.DirectoryInfo(folderPath);
            var bundleFiles = dirInfo.GetFiles($"*");
            manifest.fileName2Md5 = new Dictionary<string, string>();
            foreach (var fileInfo in bundleFiles)
            {
                string filePath = System.IO.Path.Combine(folderPath, fileInfo.Name);
                using FileStream stream = File.OpenRead(filePath);
                MD5 md5 = MD5.Create();
                byte[] hash = md5.ComputeHash(stream);
                StringBuilder hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                    hex.AppendFormat("{0:x2}", b);
                manifest.fileName2Md5.Add(fileInfo.Name, hex.ToString());
            }

            return manifest;
        }

        public static bool ContainsPatch(
            in string currentFileName,
            Manifest current,
            Manifest newest,
            PatchManifest patchManifest
        )
        {
            // 莫名其妙的 当前的manifest里面都没有这个文件
            if (!current.fileName2Md5.ContainsKey(currentFileName)) return false;
            // 因为最新的manifest里面没有这个文件 所以一定不存在patch
            if (!newest.fileName2Md5.ContainsKey(currentFileName)) return false;

            string currentMd5 = current.fileName2Md5[currentFileName];
            string newMd5 = newest.fileName2Md5[currentFileName];

            string patchName = $"{currentMd5}_{newMd5}.{PatchFileSuffix}";
            if (patchManifest.patchFiles.Contains(patchName)) return true;
            return false;
        }


        public static void CreatePatch(
            string currentVersionFolder,
            string newestVersionFolder,
            Manifest currentManifest,
            Manifest newestManifest,
            string patchesFolder,
            ref PatchManifest patchManifest
        )
        {
            string currentVersion = currentManifest.version;
            string newestVersion = newestManifest.version;

            foreach (var (fileName, newMd5) in newestManifest.fileName2Md5)
            {
                // 如果当前版本没有这个文件，跳过
                if (!currentManifest.fileName2Md5.ContainsKey(fileName)) continue;
                // 如果md5一样，跳过
                string currentMd5 = currentManifest.fileName2Md5[fileName];
                if (currentMd5 == newMd5) continue;
                // 生成patch
                string patchName = $"{currentMd5}_{newMd5}.{PatchFileSuffix}";
                string patchPath = Path.Combine(patchesFolder, patchName);
                if (File.Exists(patchPath))
                    throw new NotImplementedException(
                        $"patch already exists from[{currentVersion}_{fileName}] to[{newestVersion}_{fileName}]");
                string currentFilePath = Path.Combine(currentVersionFolder, fileName);
                string newestFilePath = Path.Combine(newestVersionFolder, fileName);

                // 使用zstd生成patch
                byte[] newestData = File.ReadAllBytes(newestFilePath);
                byte[] refData = File.ReadAllBytes(currentFilePath);
                var patchBytes = ZStandardDiffAPI.CompressPatch(newestData, refData);
                File.WriteAllBytes(patchPath, patchBytes);
                patchManifest.patchFiles.Add(patchName);
            }
        }
    }
}
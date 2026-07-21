// Copyright (c) 2025 NicoIer and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

#if ZEBRA_YOOASSET

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using YooAsset;
using zstd;

namespace zebra
{
    /// <summary>
    /// 增量更新进度信息
    /// </summary>
    public class PatchProgressInfo
    {
        public enum PatchPhase
        {
            /// <summary>初始化</summary>
            Initializing,

            /// <summary>获取版本信息</summary>
            FetchingVersions,

            /// <summary>获取清单文件</summary>
            FetchingManifests,

            /// <summary>对比资源包</summary>
            ComparingBundles,

            /// <summary>增量更新中</summary>
            Patching,

            /// <summary>更新完成</summary>
            Done,

            /// <summary>更新失败</summary>
            Failed
        }

        public List<long> needFullDownloadBytes { get; internal set; } = new();

        /// <summary>当前阶段</summary>
        public PatchPhase Phase { get; internal set; } = PatchPhase.Initializing;

        /// <summary>需要增量更新的资源包总数</summary>
        public int TotalPatchCount { get; internal set; }

        /// <summary>已完成的资源包数量</summary>
        public int CompletedPatchCount { get; internal set; }

        /// <summary>失败的资源包数量</summary>
        public int FailedPatchCount { get; internal set; }

        /// <summary>跳过的资源包数量（已经是最新或无需更新）</summary>
        public int SkippedCount { get; internal set; }

        /// <summary>远程资源包总数</summary>
        public int TotalBundleCount { get; internal set; }

        /// <summary>需要下载的增量文件总大小（字节）</summary>
        public long TotalDownloadBytes { get; internal set; }

        /// <summary>已下载的增量文件大小（字节）</summary>
        public long DownloadedBytes { get; internal set; }

        /// <summary>当前正在处理的资源包名称</summary>
        public string CurrentBundleName { get; internal set; } = string.Empty;

        /// <summary>描述信息</summary>
        public string Message { get; internal set; } = string.Empty;

        /// <summary>
        /// 归一化进度 0~1
        /// 阶段权重: FetchingVersions=0.05, FetchingManifests=0.10, ComparingBundles=0.05, Patching=0.80
        /// </summary>
        public float Progress
        {
            get
            {
                switch (Phase)
                {
                    case PatchPhase.Initializing:
                        return 0f;
                    case PatchPhase.FetchingVersions:
                        return 0.02f;
                    case PatchPhase.FetchingManifests:
                        return 0.05f;
                    case PatchPhase.ComparingBundles:
                        return 0.15f;
                    case PatchPhase.Patching:
                        if (TotalPatchCount <= 0) return 0.15f;
                        float patchProgress = (float)CompletedPatchCount / TotalPatchCount;
                        return 0.15f + patchProgress * 0.85f;
                    case PatchPhase.Done:
                        return 1f;
                    case PatchPhase.Failed:
                        return 1f;
                    default:
                        return 0f;
                }
            }
        }

        public override string ToString()
        {
            return
                $"[{Phase}] {Progress:P1} - 总计:{TotalPatchCount} 完成:{CompletedPatchCount} 失败:{FailedPatchCount} 跳过:{SkippedCount} " +
                $"下载:{PatchAPI.GetFileSize(DownloadedBytes)}/{PatchAPI.GetFileSize(TotalDownloadBytes)} " +
                $"当前:{CurrentBundleName} {Message}";
        }
    }

    public static class ZebraYooAsset
    {
        /// <summary>
        /// 同时可以进行多少个Patch任务
        /// </summary>
        public static int maxConcurrentPatches = 10;

        /// <summary>
        /// 同时可以使用多少内存进行Patch操作
        /// </summary>
        public static int maxBytesCanUseForPatch = 1024 * 1024 * 200; // 200MB

        /// <summary>
        /// Patch资源根目录的Url
        /// </summary>
        public static string patchUrl = "http://10.30.15.189:8080/";

        /// <summary>
        /// Patch资源根目录的备用Url
        /// </summary>
        public static string patchBackupUrl = "http://10.30.15.189:8080/";

        /// <summary>
        /// YooAsset资源根目录的Url
        /// </summary>
        public static string yooAssetUrl = "http://10.30.15.189:8080/";

        /// <summary>
        /// YooAsset资源根目录的备用Url
        /// </summary>
        public static string yooAssetBackupUrl = "http://10.30.15.189:8080/";

        public static string localVersionFileName = "local_version";
        public static bool isAppendFileExtensionEnabled = false;

        public static int retryCount = 10;
        public static float timeout = 30f;

        public enum BuildInFileNameStyle
        {
            HashName,
            BundleName,
            BundleName_HashName
        }

        public static BuildInFileNameStyle buildInFileNameStyle = BuildInFileNameStyle.BundleName;


        /// <summary>
        /// 带进度监控的增量更新入口
        /// </summary>
        /// <param name="packageName">包名</param>
        /// <param name="onProgress">进度回调，每次进度变化时调用，传入当前进度信息快照</param>
        public static async Task SmokeAndMirrors(string packageName,
            Action<PatchProgressInfo> onProgress = null,
            Action<PatchProgressInfo> onGotDownloadInfo = null,
            Func<Task<bool>> canDownload = null)
        {
            var progress = new PatchProgressInfo();

            void ReportProgress(string message = null)
            {
                if (message != null)
                    progress.Message = message;
                try
                {
                    onProgress?.Invoke(progress);
                }
                catch (Exception e)
                {
                    ZebraLogger.LogError($"进度回调异常：{e}");
                }
            }

            // ---- 初始化阶段 ----
            progress.Phase = PatchProgressInfo.PatchPhase.Initializing;
            ReportProgress("开始复制内置清单...");

            // Copy BuildIn Manifest to PersistentDataPath
            await CopyBuildInManifestToPersistentDataPath(packageName);

            // ---- 获取版本信息阶段 ----
            progress.Phase = PatchProgressInfo.PatchPhase.FetchingVersions;
            ReportProgress("获取版本信息...");

            string buildInVersion = await GetBuildInVersion(packageName);
            ZebraLogger.Log($"内置版本号：{buildInVersion}");

            // 拿到本地的版本号
            string localVersion = await GetLocalVersion(packageName);
            if (string.IsNullOrEmpty(localVersion))
            {
                ZebraLogger.LogError($"{packageName}本地资源版本获取失败");
                progress.Phase = PatchProgressInfo.PatchPhase.Failed;
                ReportProgress("本地资源版本获取失败");
                return;
            }

            ZebraLogger.Log($"本地版本号：{localVersion}");
            bool isBuildIn = buildInVersion == localVersion;
            ZebraLogger.Log($"本地版本号来源：{(isBuildIn ? "内置" : "持久化存储")}");

            // 拿到远程的版本号
            string remoteVersion = await GetRemoteVersion(packageName);
            if (string.IsNullOrEmpty(remoteVersion))
            {
                ZebraLogger.LogError($"{packageName}远程资源版本获取失败");
                progress.Phase = PatchProgressInfo.PatchPhase.Failed;
                ReportProgress("远程资源版本获取失败");
                return;
            }

            if (buildInVersion == remoteVersion)
            {
                ZebraLogger.Log("远程版本号与内置版本号一致，无需更新");
                progress.Phase = PatchProgressInfo.PatchPhase.Done;
                ReportProgress("资源已经是最新版本，无需更新");
                WriteLocalVersion(packageName, buildInVersion);
                return;
            }


            // ---- 获取清单阶段 ----
            progress.Phase = PatchProgressInfo.PatchPhase.FetchingManifests;
            ReportProgress("获取清单文件...");

            // 拿到Local的清单文件
            PackageManifest localManifest = await GetLocalPackageManifest(packageName, localVersion);
            if (localManifest == null)
            {
                ZebraLogger.LogError("本地清单获取失败");
                progress.Phase = PatchProgressInfo.PatchPhase.Failed;
                ReportProgress("本地清单获取失败");
                return;
            }

            // 拿到Remote的清单文件
            PackageManifest remoteManifest = await GetRemotePackageManifest(packageName, remoteVersion);
            if (remoteManifest == null)
            {
                ZebraLogger.LogError("远程清单获取失败");
                progress.Phase = PatchProgressInfo.PatchPhase.Failed;
                ReportProgress("远程清单获取失败");
                return;
            }


            var cacheRoot = YooAssetSettingsData.GetYooDefaultCacheRoot();
            string cachePackageRoot = Path.Combine(cacheRoot, packageName);
            string cacheBundleFilesRoot =
                Path.Combine(cachePackageRoot, DefaultCacheFileSystemDefine.BundleFilesFolderName);


            var remoteBundles = CreateBundleDict(remoteManifest);
            var localBundles = CreateBundleDict(localManifest);

            var verifyLevel = EFileVerifyLevel.Middle;

            // ---- 对比资源包阶段 ----
            progress.Phase = PatchProgressInfo.PatchPhase.ComparingBundles;
            progress.TotalBundleCount = remoteBundles.Count;
            ReportProgress("获取增量清单并对比资源包...");

            PatchManifest patchManifest = await GetRemotePatchManifest(downloadCompressedFile: true);

            if (patchManifest == null)
            {
                ZebraLogger.LogError("远程增量清单获取失败");
                progress.Phase = PatchProgressInfo.PatchPhase.Failed;
                ReportProgress("远程增量清单获取失败");
                return;
            }

            using var semaphore = new SemaphoreSlim(maxConcurrentPatches);
            using var memoryLimiter = new MemoryLimiter(maxBytesCanUseForPatch);

            List<Task> patchTasks = new List<Task>();

            // 先收集所有需要增量更新的Bundle信息，用于计算总量
            long totalDownloadBytes = 0;
            int totalPatchCount = 0;

            // 用于存储需要patch的bundle信息
            var patchItems =
                new System.Collections.Concurrent.ConcurrentBag<(string bundleName, PackageBundle remoteBundle,
                    PackageBundle localBundle,
                    string patchFileName, byte[] preloadedLocalData)>();

            string cachedPath = Path.Combine(Application.streamingAssetsPath,
                YooAssetSettingsData.Setting.DefaultYooFolderName, packageName);

            // ========== 阶段1：在线程池中并行检查哪些 bundle 需要更新 ==========
            // 这个阶段不涉及 UnityWebRequest，可以安全地在线程池中执行

            // 用于存储需要从内置资源读取的 bundle 信息（稍后在主线程读取）
            var bundlesNeedingBuiltInData = new System.Collections.Concurrent.ConcurrentBag<(
                string bundleName,
                PackageBundle remoteBundle,
                PackageBundle localBundle,
                string patchFileName,
                long patchFileSize)>();

            // 并行处理限制
            var checkSemaphore = new SemaphoreSlim(maxConcurrentPatches);

            // 用于线程安全的计数
            int skippedCount = 0;

            // 并行处理所有 bundle 的检查
            var checkTasks = new List<Task>();
            foreach (var (bundleName, remotePackageBundle) in remoteBundles)
            {
                // 捕获循环变量
                var capturedBundleName = bundleName;
                var capturedRemoteBundle = remotePackageBundle;

                var checkTask = Task.Run(async () =>
                {
                    await checkSemaphore.WaitAsync();
                    try
                    {
                        // 远程Bundle已经在本地存在且校验通过
                        // ZebraLogger.Log($"检查远程资源包文件:{capturedBundleName}");
                        if (await IsBundleFileOkAsync(packageName, capturedRemoteBundle, cacheBundleFilesRoot,
                                verifyLevel))
                        {
                            // ZebraLogger.Log($"最新的远程资源包已经存在且校验通过：{capturedBundleName}");
                            Interlocked.Increment(ref skippedCount);
                            return;
                        }

                        if (!localBundles.TryGetValue(capturedBundleName, out var localPackageBundle)) return;

                        // ZebraLogger.Log($"校验本地资源包文件{capturedBundleName},buildIn:{isBuildIn}");

                        // 内置资源是打包时内置的，一定是正确的，不需要检查是否存在
                        // 非内置资源需要检查缓存文件是否存在且校验通过
                        if (!isBuildIn)
                        {
                            if (!await IsBundleFileOkAsync(packageName, localPackageBundle, cacheBundleFilesRoot,
                                    verifyLevel))
                            {
                                // ZebraLogger.Log($"本地的资源包不存在或校验失败：{capturedBundleName}");
                                Interlocked.Increment(ref skippedCount);
                                return;
                            }

                            // ZebraLogger.Log($"本地的资源包已经存在且校验通过：{capturedBundleName}");
                        }

                        if (localPackageBundle.FileHash != capturedRemoteBundle.FileHash)
                        {
                            ZebraLogger.Log($"本地资源包和远程资源包不一致，准备增量更新:{capturedBundleName}");
                            // 1. 拿到增量文件
                            string patchFileName =
                                $"{localPackageBundle.FileHash}_{capturedRemoteBundle.FileHash}.{PatchAPI.PatchFileSuffix}";
                            if (!patchManifest.patchFiles.ContainsKey(patchFileName))
                            {
                                ZebraLogger.Log(
                                    $"bundle:{capturedRemoteBundle.FileName}对应的增量文件不存在于增量清单中：{patchFileName}");
                                Interlocked.Increment(ref skippedCount);
                                return;
                            }

                            long patchFileSize = patchManifest.patchFiles[patchFileName];

                            if (isBuildIn)
                            {
                                // 内置资源需要稍后在主线程读取，先收集信息
                                bundlesNeedingBuiltInData.Add((capturedBundleName, capturedRemoteBundle,
                                    localPackageBundle, patchFileName, patchFileSize));
                            }
                            else
                            {
                                // 非内置资源，直接添加到 patchItems（localFileData 为 null，稍后从缓存读取）
                                patchItems.Add((capturedBundleName, capturedRemoteBundle, localPackageBundle,
                                    patchFileName, null));
                                Interlocked.Add(ref totalDownloadBytes, patchFileSize);
                                Interlocked.Increment(ref totalPatchCount);
                            }
                        }
                        else
                        {
                            ZebraLogger.Log($"本地资源包和远程资源包一致,跳过:{capturedBundleName}");
                            Interlocked.Increment(ref skippedCount);
                        }
                    }
                    finally
                    {
                        checkSemaphore.Release();
                    }
                });
                checkTasks.Add(checkTask);
            }

            // 等待所有检查任务完成
            await Task.WhenAll(checkTasks);
            checkSemaphore.Dispose();

            // ========== 阶段2：在主线程中批量读取内置资源（Android 使用 UnityWebRequest）==========
            var builtInList = bundlesNeedingBuiltInData.ToList();
            if (builtInList.Count > 0)
            {
                ZebraLogger.Log($"开始读取 {builtInList.Count} 个内置资源包...");
                ReportProgress($"读取内置资源包 (0/{builtInList.Count})...");

                int loadedCount = 0;

#if UNITY_ANDROID
                // Android 平台：使用 UnityWebRequest，发起所有请求后统一等待
                var webRequests = new List<(UnityWebRequestAsyncOperation op,
                    string bundleName, PackageBundle remoteBundle, PackageBundle localBundle,
                    string patchFileName, long patchFileSize)>();

                // 发起所有请求（不等待）
                foreach (var item in builtInList)
                {
                    string loadPath = Path.Combine(cachedPath, item.bundleName);
                    var uri = new Uri(loadPath).AbsoluteUri;
                    var www = UnityWebRequest.Get(uri);
                    var op = www.SendWebRequest();
                    webRequests.Add((op, item.bundleName, item.remoteBundle, item.localBundle, item.patchFileName,
                        item.patchFileSize));
                }

                // 等待所有请求完成
                foreach (var (op, bundleName, remoteBundle, localBundle, patchFileName, patchFileSize) in webRequests)
                {
                    // 等待单个请求完成
                    while (!op.isDone)
                    {
                        await Task.Yield();
                    }

                    var www = op.webRequest;
                    if (www.result != UnityWebRequest.Result.Success)
                    {
                        ZebraLogger.LogError($"内置资源包读取失败：{bundleName}");
                        Interlocked.Increment(ref skippedCount);
                        www.Dispose();
                        continue;
                    }

                    byte[] localFileData = www.downloadHandler.data;
                    www.Dispose();

                    patchItems.Add((bundleName, remoteBundle, localBundle, patchFileName, localFileData));
                    totalDownloadBytes += patchFileSize;
                    totalPatchCount++;

                    loadedCount++;
                    if (loadedCount % 10 == 0 || loadedCount == builtInList.Count)
                    {
                        ReportProgress($"读取内置资源包 ({loadedCount}/{builtInList.Count})...");
                    }
                }
#else
                // 非 Android 平台：使用 Task.WhenAll 并行读取
                var readTasks = builtInList.Select(async item =>
                {
                    string loadPath = Path.Combine(cachedPath, item.bundleName);
                    if (File.Exists(loadPath))
                    {
                        byte[] data = await File.ReadAllBytesAsync(loadPath);
                        return (success: true, item.bundleName, item.remoteBundle, item.localBundle, item.patchFileName,
                            item.patchFileSize, data);
                    }
                    else
                    {
                        ZebraLogger.LogError($"内置资源包不存在：{loadPath}");
                        return (success: false, item.bundleName, item.remoteBundle, item.localBundle,
                            item.patchFileName, item.patchFileSize, data: (byte[])null);
                    }
                }).ToList();

                var results = await Task.WhenAll(readTasks);

                foreach (var result in results)
                {
                    if (result.success)
                    {
                        patchItems.Add((result.bundleName, result.remoteBundle, result.localBundle,
                            result.patchFileName, result.data));
                        totalDownloadBytes += result.patchFileSize;
                        totalPatchCount++;
                        loadedCount++;
                    }
                    else
                    {
                        Interlocked.Increment(ref skippedCount);
                    }
                }

                ReportProgress($"读取内置资源包 ({loadedCount}/{builtInList.Count})...");
#endif

                ZebraLogger.Log($"内置资源包读取完成，成功 {loadedCount} 个");
            }

            // 更新进度信息
            progress.SkippedCount = skippedCount;

            // 计算需要全量下载的字节数
            string fullDownloadKey = $"{localVersion}_{remoteVersion}";
            if (patchManifest.needFullDownloads != null &&
                patchManifest.needFullDownloads.TryGetValue(fullDownloadKey, out var fullDownloadSizes))
            {
                progress.needFullDownloadBytes = fullDownloadSizes;
            }
            progress.needFullDownloadBytes ??= new();

            // ---- 增量更新阶段 ----
            progress.Phase = PatchProgressInfo.PatchPhase.Patching;
            progress.TotalPatchCount = totalPatchCount;
            progress.TotalDownloadBytes = totalDownloadBytes;

            if (totalPatchCount <= 0)
            {
                ZebraLogger.Log("没有需要增量更新的资源包");
                progress.Phase = PatchProgressInfo.PatchPhase.Done;
                progress.CurrentBundleName = string.Empty;
                WriteLocalVersion(packageName, remoteVersion);
                return;
            }

            // 通知调用方下载信息已计算完毕
            try
            {
                onGotDownloadInfo?.Invoke(progress);
            }
            catch (Exception e)
            {
                ZebraLogger.LogError($"onGotDownloadInfo回调异常：{e}");
            }

            ReportProgress($"开始增量更新，共{totalPatchCount}个资源包，预计下载{PatchAPI.GetFileSize(totalDownloadBytes)}");


            // 询问调用方是否允许继续下载
            if (canDownload != null && !await canDownload())
            {
                ZebraLogger.Log("canDownload返回false，取消增量更新");
                progress.Phase = PatchProgressInfo.PatchPhase.Done;
                progress.CurrentBundleName = string.Empty;
                ReportProgress("用户取消下载");
                return;
            }

            // 用于线程安全的计数
            int _completedCount = 0;
            int _failedCount = 0;
            long _downloadedBytes = 0;

            foreach (var (bundleName, remotePackageBundle, localPackageBundle, patchFileName, preloadedLocalData) in
                     patchItems)
            {
                Task patchTask = Task.Run(async () =>
                {
                    var t = Thread.CurrentThread;
                    ZebraLogger.Log(
                        $"ID: {t.ManagedThreadId}, Name: {t.Name}, IsThreadPool: {t.IsThreadPoolThread}");

                    long totalSize = localPackageBundle.FileSize + remotePackageBundle.FileSize +
                                     patchManifest.patchFiles[patchFileName];

                    bool semaphoreTaken = false;
                    bool memoryTaken = false;

                    byte[] localFileData = preloadedLocalData;

                    try
                    {
                        await semaphore.WaitAsync();
                        semaphoreTaken = true;

                        await memoryLimiter.AcquireAsync(totalSize);
                        memoryTaken = true;

                        progress.CurrentBundleName = bundleName;

                        byte[] patchData =
                            await DownloadPatchFile($"{PatchAPI.PatchFolderName}/{patchFileName}");
                        if (patchData == null)
                        {
                            // ZebraLogger.LogError($"增量文件下载失败：{patchFileName}");
                            Interlocked.Increment(ref _failedCount);
                            progress.FailedPatchCount = _failedCount;
                            ReportProgress($"增量文件下载失败：{patchFileName}");
                            return;
                        }

                        // 更新已下载字节数
                        Interlocked.Add(ref _downloadedBytes, patchData.Length);
                        progress.DownloadedBytes = Interlocked.Read(ref _downloadedBytes);

                        if (localFileData == null)
                        {
                            if (isBuildIn)
                            {
                                // ZebraLogger.LogError($"增量更新失败，内置资源包读取异常：{bundleName}");
                                Interlocked.Increment(ref _failedCount);
                                progress.FailedPatchCount = _failedCount;
                                ReportProgress($"增量更新失败，内置资源包读取异常：{bundleName}");
                                return;
                            }
                            else
                            {
                                // ZebraLogger.Log($"从持久化存储路径中读取本地文件：{bundleName}");
                                string dataFilePath = DefaultCacheFileSystem.GetBundleDataFilePath(
                                    localPackageBundle, cacheBundleFilesRoot, IsAppendFileExtensionEnabled());
                                localFileData = await File.ReadAllBytesAsync(dataFilePath);
                                ZebraLogger.Log($"持久化存储路径资源包读取完成，文件大小：{localFileData.Length}");
                            }
                        }

                        byte[] newFileData = ZStandardDiffAPI.DecompressPatch(patchData, localFileData);
                        ZebraLogger.Log(
                            $"增量更新完成，文件大小：旧文件:{PatchAPI.GetFileSize(localFileData.Length)} 增量文件:{PatchAPI.GetFileSize(patchData.Length)} 新文件:{PatchAPI.GetFileSize(newFileData.Length)}");

                        // 3. 写入新文件到缓存
                        await WriteNewBundle(newFileData, remotePackageBundle, cacheBundleFilesRoot,
                            IsAppendFileExtensionEnabled());
                        // ZebraLogger.Log($"增量更新资源包完成:{bundleName}");

                        Interlocked.Increment(ref _completedCount);
                        progress.CompletedPatchCount = _completedCount;
                        ReportProgress($"资源包增量更新完成：{bundleName}");
                    }
                    catch (Exception ex)
                    {
                        // ZebraLogger.LogError($"增量更新发生异常：{bundleName} - {ex}");
                        Interlocked.Increment(ref _failedCount);
                        progress.FailedPatchCount = _failedCount;
                        ReportProgress($"增量更新异常：{bundleName}  - {ex}");
                    }
                    finally
                    {
                        // 只有获取成功才释放，避免二次异常
                        if (memoryTaken)
                            await memoryLimiter.ReleaseAsync(totalSize);
                        if (semaphoreTaken)
                            semaphore.Release();
                    }
                });
                patchTasks.Add(patchTask);
            }

            await Task.WhenAll(patchTasks);

            WriteLocalVersion(packageName, remoteVersion);

            // ---- 完成阶段 ----
            progress.Phase = PatchProgressInfo.PatchPhase.Done;
            progress.CurrentBundleName = string.Empty;
            ReportProgress($"{packageName}增量资源更新完成，当前版本号：{remoteVersion}");
            // ZebraLogger.Log($"{packageName}增量资源更新完成，当前版本号：{remoteVersion}");
        }


        private static async Task WriteNewBundle(byte[] data, PackageBundle remotePackageBundle,
            string cacheBundleFilesRoot, bool isAppendFileExtensionEnabled)
        {
            string infoFilePath =
                DefaultCacheFileSystem.GetBundleInfoFilePath(remotePackageBundle, cacheBundleFilesRoot);
            string dataFilePath =
                DefaultCacheFileSystem.GetBundleDataFilePath(remotePackageBundle, cacheBundleFilesRoot,
                    isAppendFileExtensionEnabled);

            if (File.Exists(infoFilePath))
                File.Delete(infoFilePath);
            if (File.Exists(dataFilePath))
                File.Delete(dataFilePath);

            FileUtility.CreateFileDirectory(dataFilePath);

            await File.WriteAllBytesAsync(dataFilePath, data);

            DefaultCacheFileSystem.WriteBundleInfoFile2(infoFilePath, remotePackageBundle.FileCRC,
                remotePackageBundle.FileSize);
        }

        private static async Task<byte[]> DownloadPatchFile(string s)
        {
            string url = $"{patchUrl}/{s}";
            string backupUrl = $"{patchBackupUrl}/{s}";

            return await DownloadDataWithUrl(url, backupUrl, retryCount, timeout);
        }

        private static async Task<PatchManifest> GetRemotePatchManifest(bool downloadCompressedFile = false)
        {
            string json;
            if (downloadCompressedFile)
            {
                string url = $"{patchUrl}/{PatchAPI.PatchFolderName}/{PatchAPI.CompressedManifestFileName}";
                string backupUrl = $"{patchBackupUrl}/{PatchAPI.PatchFolderName}/{PatchAPI.CompressedManifestFileName}";

                ZebraLogger.Log($"尝试下载远程增量清单文件\nurl={url},backupUrl={backupUrl}");
                byte[] data = await DownloadDataWithUrl(url, backupUrl, retryCount, timeout);

                if (data == null)
                {
                    ZebraLogger.LogError($"远程增量清单文件获取失败：{url}");
                    return null;
                }

                byte[] unCompressedData = ZStandardAPI.Decompress(data);
                json = System.Text.Encoding.UTF8.GetString(unCompressedData);
            }
            else
            {
                string url = $"{patchUrl}/{PatchAPI.PatchFolderName}/{PatchAPI.ManifestFileName}";
                string backupUrl = $"{patchBackupUrl}/{PatchAPI.PatchFolderName}/{PatchAPI.ManifestFileName}";

                ZebraLogger.Log($"尝试下载远程增量清单文件\nurl={url},backupUrl={backupUrl}");
                var data = await DownloadDataWithUrl(url, backupUrl, retryCount, timeout);

                if (data == null)
                {
                    ZebraLogger.LogError($"远程增量清单文件获取失败：{url}");
                    return null;
                }

                json = System.Text.Encoding.UTF8.GetString(data);
            }
            ZebraLogger.Log($"远程增量清单文件内容：{json}");
            return JsonConvert.DeserializeObject<PatchManifest>(json);
        }

        /// <summary>
        /// 将YooAsset内置的清单文件复制到持久化存储路径
        /// </summary>
        /// <param name="packageName"></param>
        private static async Task CopyBuildInManifestToPersistentDataPath(string packageName)
        {
            string version = await GetBuildInVersion(packageName);
            string buildInManifestFileName =
                YooAssetSettingsData.GetManifestBinaryFileName(packageName, version);
            string buildInManifestFilePath = Path.Combine(Application.streamingAssetsPath,
                YooAssetSettingsData.Setting.DefaultYooFolderName, packageName, buildInManifestFileName);

            string rootRoot = YooAssetSettingsData.GetYooDefaultCacheRoot();
            string manifestRoot =
                Path.Combine(rootRoot, packageName, DefaultCacheFileSystemDefine.ManifestFilesFolderName);

            string fileName = YooAssetSettingsData.GetManifestBinaryFileName(packageName, version);
            var filePath = Path.Combine(manifestRoot, fileName);

            if (File.Exists(filePath))
            {
                ZebraLogger.Log("内置资源清单已经复制到持久化存储路径，无需重复操作");
                return;
            }

            ZebraLogger.Log("开始复制内置资源清单到持久化存储路径...");

#if UNITY_ANDROID
            var uri = new Uri(buildInManifestFilePath).AbsoluteUri;
            ZebraLogger.Log($"Android需要使用WebRequest来读取StreamingAssets下的文件 uri{uri}");

            using (UnityWebRequest www = UnityWebRequest.Get(uri))
            {
                await www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    ZebraLogger.LogError($"内置清单文件不存在：{buildInManifestFilePath}");
                    return;
                }

                byte[] data = www.downloadHandler.data;

                // 创建目录
                FileUtility.CreateFileDirectory(filePath);

                // 写入文件
                await File.WriteAllBytesAsync(filePath, data);
                ZebraLogger.Log("内置资源清单复制完成");
            }

#else
            if (!File.Exists(buildInManifestFilePath))
            {
                ZebraLogger.LogError($"内置清单文件不存在：{buildInManifestFilePath}");
                return;
            }

            FileUtility.CreateFileDirectory(filePath);
            File.Copy(buildInManifestFilePath, filePath, true);
            ZebraLogger.Log("内置资源清单复制完成");
#endif
        }

        private static async Task<PackageManifest> GetLocalPackageManifest(string packageName, string version)
        {
            string rootRoot = YooAssetSettingsData.GetYooDefaultCacheRoot();
            string manifestRoot =
                Path.Combine(rootRoot, packageName, DefaultCacheFileSystemDefine.ManifestFilesFolderName);

            string fileName = YooAssetSettingsData.GetManifestBinaryFileName(packageName, version);
            var filePath = Path.Combine(manifestRoot, fileName);
            if (!File.Exists(filePath))
            {
                ZebraLogger.LogError($"本地清单文件不存在：{filePath}");
                return null;
            }

            var data = await File.ReadAllBytesAsync(filePath);
            return DeserializeManifestOperation.DeserializeSync(null, data);
        }

        private static bool IsBundleFileOk(string packageName, PackageBundle packageBundle, string fileRoot,
            in EFileVerifyLevel verifyLevel)
        {
            string infoFilePath = DefaultCacheFileSystem.GetBundleInfoFilePath(packageBundle, fileRoot);
            string dataFilePath =
                DefaultCacheFileSystem.GetBundleDataFilePath(packageBundle, fileRoot, IsAppendFileExtensionEnabled());

            // 使用快速版本读取 info 文件，内部会处理文件不存在的情况
            if (!DefaultCacheFileSystem.TryReadBundleInfoFileFast(infoFilePath, out var recordedCRC,
                    out var recordedSize))
            {
                return false;
            }

            // 快速检查：如果 info 文件记录的信息与期望不符，直接返回失败
            if (recordedCRC != packageBundle.FileCRC || recordedSize != packageBundle.FileSize)
            {
                return false;
            }

            // 检查数据文件是否存在
            if (!File.Exists(dataFilePath))
            {
                return false;
            }

            // Low 级别：仅验证文件大小，跳过耗时的 CRC 计算
            if (verifyLevel == EFileVerifyLevel.Low)
            {
                long actualSize = FileUtility.GetFileSize(dataFilePath);
                return actualSize == packageBundle.FileSize;
            }

            // High 级别：完整 CRC 验证
            var result = FileVerifyHelper.FileVerify(dataFilePath, packageBundle.FileSize, packageBundle.FileCRC,
                verifyLevel);
            return result == EFileVerifyResult.Succeed;
        }

        /// <summary>
        /// 异步版本的 IsBundleFileOk，适用于并行检查场景
        /// 使用异步 I/O 操作，减少线程阻塞
        /// </summary>
        // ReSharper disable once UnusedParameter.Local
        private static async Task<bool> IsBundleFileOkAsync(string packageName, PackageBundle packageBundle,
            string fileRoot,
            EFileVerifyLevel verifyLevel)
        {
            string infoFilePath = DefaultCacheFileSystem.GetBundleInfoFilePath(packageBundle, fileRoot);
            string dataFilePath =
                DefaultCacheFileSystem.GetBundleDataFilePath(packageBundle, fileRoot, IsAppendFileExtensionEnabled());

            // 使用异步版本读取 info 文件
            var (success, recordedCRC, recordedSize) =
                await DefaultCacheFileSystem.TryReadBundleInfoFileFastAsync(infoFilePath);
            if (!success)
            {
                return false;
            }

            // 快速检查：如果 info 文件记录的信息与期望不符，直接返回失败
            if (recordedCRC != packageBundle.FileCRC || recordedSize != packageBundle.FileSize)
            {
                return false;
            }

            // 检查数据文件是否存在（这个操作很快，不需要异步）
            if (!File.Exists(dataFilePath))
            {
                return false;
            }

            // Low 级别：仅验证文件大小，跳过耗时的 CRC 计算
            if (verifyLevel == EFileVerifyLevel.Low)
            {
                // 获取文件大小是快速操作
                long actualSize = FileUtility.GetFileSize(dataFilePath);
                return actualSize == packageBundle.FileSize;
            }

            // High 级别：使用异步方式计算 CRC
            // 异步读取文件并计算 CRC32
            return await Task.Run(() =>
            {
                var result = FileVerifyHelper.FileVerify(dataFilePath, packageBundle.FileSize, packageBundle.FileCRC,
                    verifyLevel);
                return result == EFileVerifyResult.Succeed;
            });
        }

        private static Dictionary<string, PackageBundle> CreateBundleDict(PackageManifest manifest)
        {
            var dict = new Dictionary<string, PackageBundle>();
            foreach (var packageBundle in manifest.BundleList)
            {
                dict.Add(packageBundle.BundleName, packageBundle);
            }

            return dict;
        }

        private static async Task<PackageManifest> GetRemotePackageManifest(string packageName, string version)
        {
            string fileName = YooAssetSettingsData.GetManifestBinaryFileName(packageName, version);
            if (File.Exists(fileName))
            {
                return await GetLocalPackageManifest(packageName, version);
            }

            string url = $"{yooAssetUrl}/{fileName}";
            string backupUrl = $"{yooAssetBackupUrl}/{fileName}";
            // 开始web请求下载
            var data = await DownloadDataWithUrl(url, backupUrl, retryCount, timeout);
            if (data == null)
            {
                ZebraLogger.LogError($"远程清单文件获取失败：{url}");
                return null;
            }

            ZebraLogger.Log($"远程清单文件下载完成，大小：{PatchAPI.GetFileSize(data.Length)}");
            return DeserializeManifestOperation.DeserializeSync(null, data);
        }


        private static async Task<string> GetRemoteVersion(string packageName)
        {
            string versionFileName = YooAssetSettingsData.GetPackageVersionFileName(packageName);
            string versionUrl = $"{yooAssetUrl}/{versionFileName}";
            string versionBackupUrl = $"{yooAssetBackupUrl}/{versionFileName}";


            byte[] data = await DownloadDataWithUrl(versionUrl, versionBackupUrl, retryCount, timeout);

            if (data == null)
            {
                ZebraLogger.LogError($"远程版本文件获取失败：{versionUrl}");
                return null;
            }

            string version = System.Text.Encoding.UTF8.GetString(data).Trim();

            ZebraLogger.Log($"远程版本号：{version}");
            return version;
        }

        /// <summary>
        /// 写入某个Package的最新本地版本号
        /// </summary>
        /// <param name="packageName"></param>
        /// <param name="version"></param>
        private static void WriteLocalVersion(string packageName, string version)
        {
            string localVersionFilePath =
                Path.Combine(Application.persistentDataPath, packageName, localVersionFileName);
            FileUtility.CreateFileDirectory(localVersionFilePath);
            File.WriteAllText(localVersionFilePath, version);
        }

        private static async Task<string> GetLocalVersion(string packageName)
        {
            string localVersionFilePath =
                Path.Combine(Application.persistentDataPath, packageName, localVersionFileName);
            if (File.Exists(localVersionFilePath))
            {
                return await File.ReadAllTextAsync(localVersionFilePath);
            }

            return await GetBuildInVersion(packageName);
        }


        private static async Task<string> GetBuildInVersion(string packageName)
        {
            string buildInVersion;
            string versionFileName = YooAssetSettingsData.GetPackageVersionFileName(packageName);
            string buildInVersionFilePath = Path.Combine(Application.streamingAssetsPath,
                YooAssetSettingsData.Setting.DefaultYooFolderName, packageName, versionFileName);


            // 安卓平台下StreamingAssets是一个压缩包 需要用UnityWebRequest来读取文件内容
#if UNITY_ANDROID
            string uri = new Uri(buildInVersionFilePath).AbsoluteUri;
            using (UnityWebRequest www = UnityWebRequest.Get(uri))
            {
                await www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    ZebraLogger.LogWarning($"内置版本文件不存在：{buildInVersionFilePath}");
                    return null;
                }

                buildInVersion = www.downloadHandler.text.Trim();
                return buildInVersion;
            }

#else
            if (!File.Exists(buildInVersionFilePath))
            {
                ZebraLogger.LogWarning($"内置版本文件不存在：{buildInVersionFilePath}");
                return null;
            }

            buildInVersion = await File.ReadAllTextAsync(buildInVersionFilePath);
            return buildInVersion;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAppendFileExtensionEnabled()
        {
            return isAppendFileExtensionEnabled;
        }


        // private static async Task<byte[]> DownloadDataWithUrl(string url, int maxRetryCount, float timeoutSeconds)
        // {
        //     int currentTry = 0;
        //     while (currentTry < maxRetryCount)
        //     {
        //         ++currentTry;
        //
        //         using (UnityWebRequest www = UnityWebRequest.Get(url))
        //         {
        //             www.timeout = (int)timeoutSeconds;
        //             await www.SendWebRequest();
        //             if (www.result == UnityWebRequest.Result.Success)
        //             {
        //                 return www.downloadHandler.data;
        //             }
        //
        //             ZebraLogger.LogWarning($"远程文件获取失败：{url}，正在重试...({currentTry}/{maxRetryCount})");
        //         }
        //     }
        //
        //     return null;
        // }

        private static readonly HttpClient _sharedClient;

        static ZebraYooAsset()
        {
            // Unity 使用 Mono HTTP 栈，不支持 SocketsHttpHandler.PooledConnectionLifetime
            // 通过 ServicePointManager 控制 DNS 缓存刷新，避免 IP 变更后连到旧地址
            ServicePointManager.DnsRefreshTimeout = 120_000; // DNS 缓存 120 秒后刷新
            ServicePointManager.DefaultConnectionLimit = 10; // 同域名最大并发连接数

            _sharedClient = new HttpClient()
            {
                Timeout = Timeout.InfiniteTimeSpan // 用 CTS 控制超时
            };
        }

        public static async Task<byte[]> DownloadDataWithUrl(string url, string backupUrl, int maxRetryCount,
            float timeoutSeconds)
        {
            Exception lastException = null;
            bool hasBackup = !string.IsNullOrEmpty(backupUrl);

            for (int currentTry = 1; currentTry <= maxRetryCount; currentTry++)
            {
                // 奇数次用主URL，偶数次用备用URL
                string curUrl = (currentTry % 2 == 1 || !hasBackup) ? url : backupUrl;

                try
                {
                    // 整体超时控制（连接 + 下载）
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds * 2));

                    // 不使用 using，让 ReadWithIdleTimeout 完整读完 stream 后再 dispose
                    // 避免并发场景下 response 提前 dispose 导致底层 socket 被回收
                    var response = await _sharedClient.GetAsync(
                        curUrl,
                        HttpCompletionOption.ResponseHeadersRead,
                        cts.Token);

                    try
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            if (currentTry == maxRetryCount)
                            {
                                ZebraLogger.LogError($"服务器返回错误 {(int)response.StatusCode}: {curUrl}");
                            }

                            continue;
                        }

                        return await ReadWithIdleTimeout(response.Content, timeoutSeconds);
                    }
                    finally
                    {
                        response.Dispose();
                    }
                }
                catch (OperationCanceledException ex)
                {
                    lastException = ex;
                    if (currentTry == maxRetryCount)
                    {
                        ZebraLogger.LogError($"连接超时: {curUrl}");
                    }
                }
                catch (TimeoutException ex)
                {
                    lastException = ex;
                    if (currentTry == maxRetryCount)
                    {
                        ZebraLogger.LogError($"下载超时: {curUrl}");
                    }
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    if (currentTry == maxRetryCount)
                    {
                        ZebraLogger.LogError($"网络错误: {curUrl} - {ex.Message}");
                    }
                }
                catch (ObjectDisposedException ex)
                {
                    // 高并发下连接池中的 socket 可能被其他请求 dispose，重试即可
                    lastException = ex;
                    if (currentTry == maxRetryCount)
                    {
                        ZebraLogger.LogError($"连接被回收: {curUrl} - {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    ZebraLogger.LogError($"未知错误: {curUrl} - {ex}");
                    throw; // 未知错误直接抛出，不重试
                }

                if (currentTry < maxRetryCount)
                {
                    // 指数退避
                    int delayMs = Math.Min(1000 * (int)Math.Pow(2, currentTry - 1), 10000);
                    await Task.Delay(delayMs);
                }
            }

            ZebraLogger.LogError($"下载失败，已重试 {maxRetryCount} 次: {url} 备用地址：{backupUrl}");
            return null;
        }

        private static async Task<byte[]> ReadWithIdleTimeout(HttpContent content, float timeoutSeconds)
        {
            using var stream = await content.ReadAsStreamAsync();
            using var memoryStream = new MemoryStream();
            using var cts = new CancellationTokenSource();

            var lastLength = 0L;
            var lastDataTime = DateTime.UtcNow;
            var idleTimeout = TimeSpan.FromSeconds(timeoutSeconds);

            var idleTimedOut = false;
            var copyTask = stream.CopyToAsync(memoryStream, 81920, cts.Token);

            try
            {
                while (!copyTask.IsCompleted)
                {
                    await Task.Delay(100);

                    // 检查任务是否出错
                    if (copyTask.IsFaulted)
                    {
                        await copyTask; // 立即重新抛出异常
                    }

                    long currentLength = memoryStream.Length;

                    if (currentLength > lastLength)
                    {
                        lastLength = currentLength;
                        lastDataTime = DateTime.UtcNow;
                    }
                    else if (DateTime.UtcNow - lastDataTime > idleTimeout)
                    {
                        idleTimedOut = true;
                        cts.Cancel(); // 取消下载任务
                        throw new TimeoutException($"空闲超时：超过 {timeoutSeconds} 秒未收到数据");
                    }
                }

                await copyTask;
                return memoryStream.ToArray();
            }
            catch (OperationCanceledException) when (idleTimedOut)
            {
                throw new TimeoutException($"空闲超时：超过 {timeoutSeconds} 秒未收到数据");
            }
        }
    }
}

#endif
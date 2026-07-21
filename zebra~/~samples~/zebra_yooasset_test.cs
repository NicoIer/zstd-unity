#if ZEBRA_YOOASSET
using System.Collections;
using UnityEngine;
using YooAsset;

namespace zebra.samples
{
    public class zebra_yooasset_test : MonoBehaviour
    {
        public string patchUrl = "http://10.30.15.230:8080/";
        public string yooAssetUrl = "http://10.30.15.230:8080/Bundles/StandaloneOSX/";
        public string packageName = "DefaultPackage";
        
        
        public string hostServerUrl = "http://10.30.15.230:8080/Bundles/StandaloneOSX/DefaultPackage/";
        

        private async void Awake()
        {
            ZebraYooAsset.patchUrl = patchUrl;
            ZebraYooAsset.yooAssetUrl = yooAssetUrl;
            ZebraYooAsset.isAppendFileExtensionEnabled = false;
            ZebraYooAsset.buildInFileNameStyle = ZebraYooAsset.BuildInFileNameStyle.BundleName;
            ZebraYooAsset.maxBytesCanUseForPatch = 200 * 1024 * 1024; // 并发时最多能使用200MB内存进行Patch
            ZebraYooAsset.maxConcurrentPatches = 100; // 100 个并发Patch任务
            
            await ZebraYooAsset.SmokeAndMirrors(packageName);
            StartCoroutine(InitYooAsset());
        }


        private IEnumerator InitYooAsset()
        {
            YooAssets.Initialize();
            var package = YooAssets.CreatePackage(packageName);
            IRemoteServices remoteServices = new RemoteServices(hostServerUrl, hostServerUrl);
            var cacheFileSystemParams = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices);
            var buildInFileSystemParams = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();

            var createParameters = new HostPlayModeParameters();
            createParameters.BuildinFileSystemParameters = buildInFileSystemParams;
            createParameters.CacheFileSystemParameters = cacheFileSystemParams;

            yield return package.InitializeAsync(createParameters);


            string packageVersion;
            var operation2 = package.RequestPackageVersionAsync();
            yield return operation2;

            if (operation2.Status == EOperationStatus.Succeed)
            {
                //更新成功
                packageVersion = operation2.PackageVersion;
                Debug.Log($"Request package Version : {packageVersion}");
            }
            else
            {
                //更新失败
                Debug.LogError(operation2.Error);
                yield break;
            }


            var operation = package.UpdatePackageManifestAsync(packageVersion);
            yield return operation;

            if (operation.Status == EOperationStatus.Succeed)
            {
                //更新成功
            }
            else
            {
                //更新失败
                Debug.LogError(operation.Error);
            }

            Debug.Log("开始下载资源文件");
            yield return Download();
        }

        IEnumerator Download()
        {
            int downloadingMaxNum = 10;
            int failedTryAgain = 3;
            var package = YooAssets.GetPackage("DefaultPackage");
            var downloader = package.CreateResourceDownloader(downloadingMaxNum, failedTryAgain);

            //没有需要下载的资源
            if (downloader.TotalDownloadCount == 0)
            {
                Debug.Log("没有需要下载的资源");
                yield break;
            }

            //需要下载的文件总数和总大小
            int totalDownloadCount = downloader.TotalDownloadCount;
            Debug.Log($"需要下载的文件总数: {totalDownloadCount}");
            long totalDownloadBytes = downloader.TotalDownloadBytes;

            //注册回调方法
            downloader.DownloadFinishCallback = OnDownloadFinishFunction; //当下载器结束（无论成功或失败）
            downloader.DownloadErrorCallback = OnDownloadErrorFunction; //当下载器发生错误
            downloader.DownloadUpdateCallback = OnDownloadUpdateFunction; //当下载进度发生变化
            downloader.DownloadFileBeginCallback = OnDownloadFileBeginFunction; //当开始下载某个文件

            //开启下载
            downloader.BeginDownload();
            yield return downloader;

            //检测下载结果
            if (downloader.Status == EOperationStatus.Succeed)
            {
                //下载成功
            }
            else
            {
                //下载失败
            }
        }

        private void OnDownloadFileBeginFunction(DownloadFileData data)
        {
            Debug.Log($"开始下载文件: {data}");
        }

        private void OnDownloadUpdateFunction(DownloadUpdateData data)
        {
        }

        private void OnDownloadErrorFunction(DownloadErrorData data)
        {
            Debug.LogError($"下载错误: {data}");
        }

        private void OnDownloadFinishFunction(DownloaderFinishData data)
        {
            Debug.Log($"下载完成 {data}");
        }
    }
}

#endif
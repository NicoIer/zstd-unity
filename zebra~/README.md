
        // // 只是记录一下YooAsset是怎么
        // private static void SearchAndVerify()
        // {
        //     bool appendFileExtension = IsAppendFileExtensionEnabled();
        //
        //     var cacheRoot = YooAssetSettingsData.GetYooDefaultCacheRoot();
        //
        //     string packageName = "DefaultPackage";
        //
        //     string cachePackageRoot = Path.Combine(cacheRoot, packageName);
        //
        //     string cacheBundleFilesRoot =
        //         Path.Combine(cachePackageRoot, DefaultCacheFileSystemDefine.BundleFilesFolderName);
        //
        //
        //     var directories = Directory.EnumerateDirectories(cacheBundleFilesRoot);
        //
        //
        //     var verifyLevel = EFileVerifyLevel.High;
        //
        //     foreach (var directory in directories)
        //     {
        //         var childDirectories = Directory.EnumerateDirectories(directory);
        //
        //         foreach (var childDirectory in childDirectories)
        //         {
        //             string bundleGUID = Path.GetFileName(childDirectory);
        //
        //             string dataFilePath = Path.Combine(childDirectory, DefaultCacheFileSystemDefine.BundleDataFileName);
        //             string infoFilePath = Path.Combine(childDirectory, DefaultCacheFileSystemDefine.BundleInfoFileName);
        //
        //             if (appendFileExtension)
        //             {
        //                 string dataFileExtension = SearchCacheFilesOperation.FindDataFileExtension(childDirectory);
        //                 if (!string.IsNullOrEmpty(dataFileExtension) == false)
        //                 {
        //                     dataFilePath += dataFileExtension;
        //                 }
        //             }
        //
        //             VerifyFileElement element = new VerifyFileElement(packageName, bundleGUID, childDirectory,
        //                 dataFilePath, infoFilePath);
        //
        //
        //             DefaultCacheFileSystem.ReadBundleInfoFile(element.InfoFilePath, out element.DataFileCRC,
        //                 out element.DataFileSize);
        //
        //             FileVerifyHelper.FileVerify(element.DataFilePath, element.DataFileSize, element.DataFileCRC,
        //                 verifyLevel);
        //         }
        //     }
        // }
        //
        //
        // // 只是记录一下YooAsset是怎么
        // private static void WriteToCache()
        // {
        //     var cacheRoot = YooAssetSettingsData.GetYooDefaultCacheRoot();
        //
        //     string packageName = "DefaultPackage";
        //
        //     string cachePackageRoot = Path.Combine(cacheRoot, packageName);
        //
        //     string cacheBundleFilesRoot =
        //         Path.Combine(cachePackageRoot, DefaultCacheFileSystemDefine.BundleFilesFolderName);
        //
        //
        //     PackageBundle bundle = null;
        //     string copyPath = null; 
        //     byte[] data = null;
        //
        //     BufferWriter bufferWriter = new BufferWriter(1024);
        //
        //     string infoFilePath = DefaultCacheFileSystem.GetBundleInfoFilePath(bundle, cacheBundleFilesRoot);
        //     string dataFilePath =
        //         DefaultCacheFileSystem.GetBundleDataFilePath(bundle, cacheBundleFilesRoot,
        //             IsAppendFileExtensionEnabled());
        //
        //     if (File.Exists(infoFilePath))
        //         File.Delete(infoFilePath);
        //     if (File.Exists(dataFilePath))
        //         File.Delete(dataFilePath);
        //
        //
        //     FileUtility.CreateFileDirectory(dataFilePath);
        //
        //     // 拷贝数据文件 这一步算了吧 直接写入就行
        //     // FileInfo fileInfo = new FileInfo(copyPath);
        //     // fileInfo.CopyTo(dataFilePath);
        //     File.WriteAllBytes(dataFilePath, data);
        //
        //     // 写入文件信息
        //     DefaultCacheFileSystem.WriteBundleInfoFile(infoFilePath, bundle.FileCRC, bundle.FileSize, bufferWriter);
        //
        //
        //     // 这一步不用做
        //     // var recordFileElement = new RecordFileElement(infoFilePath, dataFilePath, bundle.FileCRC, bundle.FileSize);
        //     // return RecordBundleFile(bundle.BundleGUID, recordFileElement);
        // }
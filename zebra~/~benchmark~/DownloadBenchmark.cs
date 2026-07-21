#if ZEBRA_YOOASSET

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace zebra.benchmark
{
    public class DownloadBenchmark : MonoBehaviour
    {
        [Header("Server Config")]
        [Tooltip("Test file server base URL")]
        public string serverUrl = "http://localhost:8080";

        [Header("Test Parameters")]
        [Tooltip("Max retry count per download")]
        public int retryCount = 3;

        [Tooltip("Timeout in seconds")]
        public float timeout = 30f;

        [Tooltip("Number of concurrent downloads for concurrency test")]
        public int concurrency = 10;

        [Tooltip("How many rounds to run for each test case")]
        public int rounds = 3;

        [Header("Test Files")]
        [Tooltip("File names on the server to test (under serverUrl)")]
        public string[] testFiles = new[]
        {
            "1KB.bin",
            "10KB.bin",
            "100KB.bin",
            "500KB.bin",
            "1MB.bin",
            "5MB.bin",
            "10MB.bin",
            "50MB.bin",
            "100MB.bin",
        };

        [Header("Controls")]
        public bool runSequentialTest = true;
        public bool runConcurrentTest = true;
        public bool runStressTest = true;

        [Header("Runtime")]
        [SerializeField] private bool _isRunning;

        private readonly List<string> _logLines = new List<string>();

        public async void RunBenchmark()
        {
            if (_isRunning)
            {
                Debug.LogWarning("[Benchmark] Already running");
                return;
            }

            _isRunning = true;
            _logLines.Clear();

            Log("========================================");
            Log("  ZebraYooAsset Download Benchmark");
            Log("========================================");
            Log($"Server: {serverUrl}");
            Log($"Timeout: {timeout}s, Retry: {retryCount}, Rounds: {rounds}");
            Log("");

            try
            {
                // Verify server is reachable
                if (!await CheckServer())
                {
                    Log("[FAIL] Server not reachable. Run setup_test_server.py first.");
                    return;
                }

                if (runSequentialTest)
                    await RunSequentialDownloadTest();

                if (runConcurrentTest)
                    await RunConcurrentDownloadTest();

                if (runStressTest)
                    await RunStressTest();

                Log("");
                Log("========================================");
                Log("  All benchmarks completed");
                Log("========================================");
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Benchmark failed: {ex}");
            }
            finally
            {
                _isRunning = false;
            }
        }

        /// <summary>
        /// Test 1: Download each file sequentially, measure throughput
        /// </summary>
        private async Task RunSequentialDownloadTest()
        {
            Log("--- Test 1: Sequential Download ---");
            Log($"{"File",-12} {"Size",10} {"Time",10} {"Speed",12} {"Status",8}");
            Log(new string('-', 56));

            foreach (var file in testFiles)
            {
                var results = new List<(long bytes, double ms, bool ok)>();

                for (int r = 0; r < rounds; r++)
                {
                    var sw = Stopwatch.StartNew();
                    byte[] data = await DownloadFile($"{serverUrl}/{file}");
                    sw.Stop();

                    results.Add((data?.Length ?? 0, sw.Elapsed.TotalMilliseconds, data != null));
                }

                var successes = results.Where(x => x.ok).ToList();
                if (successes.Count > 0)
                {
                    long bytes = successes[0].bytes;
                    double avgMs = successes.Average(x => x.ms);
                    double speed = bytes / (avgMs / 1000.0);
                    Log($"{file,-12} {FormatSize(bytes),10} {avgMs,8:F1}ms {FormatSize((long)speed)+"/s",12} {"OK",8}");
                }
                else
                {
                    Log($"{file,-12} {"?",10} {"?",10} {"?",12} {"FAIL",8}");
                }
            }

            Log("");
        }

        /// <summary>
        /// Test 2: Download N files concurrently, measure total throughput
        /// </summary>
        private async Task RunConcurrentDownloadTest()
        {
            Log($"--- Test 2: Concurrent Download (x{concurrency}) ---");

            // Pick a medium-sized file for concurrency test
            string testFile = testFiles.Length > 4 ? testFiles[4] : testFiles[testFiles.Length - 1];
            Log($"File: {testFile}, Concurrency: {concurrency}");
            Log($"{"Concurrency",12} {"Total",10} {"Time",10} {"Throughput",12}");
            Log(new string('-', 48));

            int[] concurrencyLevels = { 1, 5, 10, 20, 50 };

            foreach (int c in concurrencyLevels)
            {
                if (c > concurrency) break;

                var sw = Stopwatch.StartNew();
                var tasks = new List<Task<byte[]>>();

                for (int i = 0; i < c; i++)
                {
                    tasks.Add(DownloadFile($"{serverUrl}/{testFile}"));
                }

                var results = await Task.WhenAll(tasks);
                sw.Stop();

                int okCount = results.Count(r => r != null);
                long totalBytes = results.Where(r => r != null).Sum(r => (long)r.Length);
                double totalSpeed = totalBytes / (sw.Elapsed.TotalSeconds);

                Log($"{c,12} {FormatSize(totalBytes),10} {sw.Elapsed.TotalMilliseconds,8:F1}ms {FormatSize((long)totalSpeed)+"/s",12} ({okCount}/{c} ok)");
            }

            Log("");
        }

        /// <summary>
        /// Test 3: Rapid fire many small requests to stress connection handling
        /// </summary>
        private async Task RunStressTest()
        {
            Log("--- Test 3: Stress Test (many small files) ---");

            string smallFile = testFiles[0]; // 1KB
            int totalRequests = concurrency * 10;
            Log($"File: {smallFile}, Total requests: {totalRequests}, Concurrency: {concurrency}");

            var sw = Stopwatch.StartNew();
            int okCount = 0;
            int failCount = 0;

            using var semaphore = new SemaphoreSlim(concurrency);
            var tasks = new List<Task>();

            for (int i = 0; i < totalRequests; i++)
            {
                await semaphore.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var data = await DownloadFile($"{serverUrl}/{smallFile}");
                        if (data != null)
                            Interlocked.Increment(ref okCount);
                        else
                            Interlocked.Increment(ref failCount);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);
            sw.Stop();

            double rps = totalRequests / sw.Elapsed.TotalSeconds;
            Log($"  Completed: {okCount}/{totalRequests} ({failCount} failed)");
            Log($"  Time: {sw.Elapsed.TotalMilliseconds:F1}ms");
            Log($"  Throughput: {rps:F1} req/s");
            Log("");
        }

        private async Task<bool> CheckServer()
        {
            try
            {
                var data = await DownloadFile($"{serverUrl}/{testFiles[0]}");
                return data != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Calls ZebraYooAsset.DownloadDataWithUrl via reflection since it's private
        /// </summary>
        private async Task<byte[]> DownloadFile(string url)
        {
            var method = typeof(ZebraYooAsset).GetMethod(
                "DownloadDataWithUrl",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            if (method == null)
            {
                Debug.LogError("[Benchmark] Cannot find DownloadDataWithUrl via reflection");
                return null;
            }

            var task = (Task<byte[]>)method.Invoke(null, new object[] { url, retryCount, timeout });
            return await task;
        }

        private void Log(string msg)
        {
            _logLines.Add(msg);
            Debug.Log($"[Benchmark] {msg}");
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F2}MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2}GB";
        }

        #region Editor GUI

#if UNITY_EDITOR
        [UnityEditor.CustomEditor(typeof(DownloadBenchmark))]
        public class DownloadBenchmarkEditor : UnityEditor.Editor
        {
            public override void OnInspectorGUI()
            {
                base.OnInspectorGUI();

                var benchmark = (DownloadBenchmark)target;

                UnityEditor.EditorGUILayout.Space(10);

                using (new UnityEditor.EditorGUI.DisabledGroupScope(benchmark._isRunning))
                {
                    if (GUILayout.Button(benchmark._isRunning ? "Running..." : "Run Benchmark", GUILayout.Height(40)))
                    {
                        benchmark.RunBenchmark();
                    }
                }

                if (benchmark._logLines.Count > 0)
                {
                    UnityEditor.EditorGUILayout.Space(5);
                    UnityEditor.EditorGUILayout.LabelField("Results", UnityEditor.EditorStyles.boldLabel);

                    var style = new GUIStyle(UnityEditor.EditorStyles.label)
                    {
                        font = Font.CreateDynamicFontFromOSFont("Courier New", 12),
                        richText = false,
                        wordWrap = false
                    };

                    foreach (var line in benchmark._logLines)
                    {
                        UnityEditor.EditorGUILayout.LabelField(line, style);
                    }

                    if (GUILayout.Button("Clear Results"))
                    {
                        benchmark._logLines.Clear();
                    }

                    if (GUILayout.Button("Copy to Clipboard"))
                    {
                        GUIUtility.systemCopyBuffer = string.Join("\n", benchmark._logLines);
                    }
                }
            }
        }
#endif

        #endregion
    }
}

#endif

using System;
using System.Threading;
using System.Threading.Tasks;

namespace zebra
{
    public class MemoryLimiter : IDisposable
    {
        private readonly long _maxBytes;

        private long _currentBytes;

        private bool _disposed;

        // 使用 SemaphoreSlim 作为锁来保护 _currentBytes，比 lock 更适合异步
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        // 用于通知等待者"有内存释放了"
        private readonly AsyncAutoResetEvent _spaceAvailableEvent = new AsyncAutoResetEvent();

        public MemoryLimiter(long maxBytes)
        {
            _maxBytes = maxBytes;
            _currentBytes = 0;
        }

        public async Task AcquireAsync(long bytes)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MemoryLimiter));

            // 如果单个请求直接超过总限额，等待所有已占用额度释放后强制放行（避免死锁）
            if (bytes > _maxBytes)
            {
                while (true)
                {
                    await _lock.WaitAsync();
                    try
                    {
                        if (_currentBytes == 0)
                        {
                            _currentBytes += bytes;
                            return;
                        }
                    }
                    finally
                    {
                        _lock.Release();
                    }

                    await _spaceAvailableEvent.WaitAsync();
                }
            }

            while (true)
            {
                await _lock.WaitAsync();
                try
                {
                    if (_currentBytes + bytes <= _maxBytes)
                    {
                        _currentBytes += bytes;
                        return; // 申请成功
                    }
                }
                finally
                {
                    _lock.Release();
                }

                // 额度不足，等待有人释放内存
                await _spaceAvailableEvent.WaitAsync();
            }
        }

        public async Task ReleaseAsync(long bytes)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MemoryLimiter));

            await _lock.WaitAsync();
            try
            {
                _currentBytes -= bytes;
                if (_currentBytes < 0) _currentBytes = 0;
            }
            finally
            {
                _lock.Release();
            }

            // 通知所有等待的任务检查是否足够
            _spaceAvailableEvent.Set();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 唤醒所有等待者，使其能够检测到 disposed 状态并退出
            _spaceAvailableEvent.Cancel();
            _lock?.Dispose();
        }
    }

    // 辅助类：线程安全的异步事件信号
    public class AsyncAutoResetEvent
    {
        private volatile TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();
        private readonly object _syncRoot = new object();

        public Task WaitAsync()
        {
            lock (_syncRoot)
            {
                return _tcs.Task;
            }
        }

        public void Set()
        {
            TaskCompletionSource<bool> tcs;
            lock (_syncRoot)
            {
                tcs = _tcs;
                // 重新创建一个新的 TCS 供下一轮等待
                _tcs = new TaskCompletionSource<bool>();
            }

            // 在锁外触发，避免在锁内执行延续回调导致死锁
            tcs.TrySetResult(true);
        }

        // 取消所有等待者（用于 Dispose 时清理）
        public void Cancel()
        {
            TaskCompletionSource<bool> tcs;
            lock (_syncRoot)
            {
                tcs = _tcs;
                _tcs = new TaskCompletionSource<bool>();
            }

            tcs.TrySetCanceled();
        }
    }
}

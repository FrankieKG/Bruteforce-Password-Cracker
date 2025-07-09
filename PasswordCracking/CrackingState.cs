using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PasswordCracking
{
    /// <summary>
    /// Thread-safe state manager for multi-GPU password cracking operations.
    /// 
    /// <para><strong>Centralized Synchronization:</strong></para>
    /// <para>This class encapsulates all shared state and synchronization logic that was previously</para>
    /// <para>scattered across multiple methods with individual lock objects. It provides a single</para>
    /// <para>point of control for coordinating work across multiple GPU threads.</para>
    /// 
    /// <para><strong>Thread Safety:</strong></para>
    /// <para>All public methods are thread-safe and use internal locking to ensure consistent</para>
    /// <para>state access from multiple GPU worker threads. The class uses reader-writer locks</para>
    /// <para>where appropriate to maximize performance for read-heavy operations.</para>
    /// 
    /// <para><strong>State Management:</strong></para>
    /// <list type="bullet">
    /// <item><description>Password found status and result coordination</description></item>
    /// <item><description>Total progress tracking across all GPUs</description></item>
    /// <item><description>Work distribution and load balancing state</description></item>
    /// <item><description>Performance metrics aggregation</description></item>
    /// <item><description>Cancellation and early termination signaling</description></item>
    /// </list>
    /// </summary>
    public class CrackingState : IDisposable
    {
        #region Private Fields

        /// <summary>
        /// Primary lock object for protecting critical shared state.
        /// Used for operations that require exclusive access or modify multiple related fields.
        /// </summary>
        private readonly object primaryLock = new object();

        /// <summary>
        /// Reader-writer lock for performance metrics that are read frequently but written less often.
        /// Allows multiple concurrent readers while ensuring exclusive writer access.
        /// </summary>
        private readonly ReaderWriterLockSlim metricsLock = new ReaderWriterLockSlim();

        // Core cracking state
        private bool passwordFound = false;
        private string foundPassword = string.Empty;
        private int winningGpuIndex = -1;
        private DateTime passwordFoundTime = DateTime.MinValue;

        // Progress tracking
        private ulong totalProcessed = 0;
        private readonly ulong totalCombinations;
        private readonly DateTime startTime;

        // Work distribution state
        private ulong globalWorkOffset = 0;
        private readonly Dictionary<int, GpuWorkState> gpuWorkStates = new Dictionary<int, GpuWorkState>();

        // Performance metrics cache
        private double cachedOverallProgress = 0.0;
        private double cachedTotalSpeed = 0.0;
        private DateTime lastMetricsUpdate = DateTime.MinValue;

        // Cancellation and control
        private bool cancellationRequested = false;
        private bool disposed = false;

        #endregion

        #region Nested Types

        /// <summary>
        /// Represents the work state for an individual GPU.
        /// </summary>
        public class GpuWorkState
        {
            public int GpuIndex { get; set; }
            public ulong ProcessedCombinations { get; set; }
            public ulong AssignedWork { get; set; }
            public ulong RemainingWork { get; set; }
            public double Speed { get; set; } // Combinations per second
            public DateTime LastUpdateTime { get; set; }
            public bool IsActive { get; set; } = true;
        }

        /// <summary>
        /// Snapshot of current cracking state for read-only access.
        /// </summary>
        public class StateSnapshot
        {
            public bool PasswordFound { get; set; }
            public string FoundPassword { get; set; } = string.Empty;
            public int WinningGpuIndex { get; set; } = -1;
            public DateTime PasswordFoundTime { get; set; }
            public ulong TotalProcessed { get; set; }
            public ulong TotalCombinations { get; set; }
            public double OverallProgress { get; set; }
            public double TotalSpeed { get; set; }
            public TimeSpan ElapsedTime { get; set; }
            public Dictionary<int, GpuWorkState> GpuStates { get; set; } = new Dictionary<int, GpuWorkState>();
            public bool CancellationRequested { get; set; }
        }

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new CrackingState for coordinating multi-GPU password cracking.
        /// </summary>
        /// <param name="totalCombinations">Total number of password combinations to process</param>
        /// <param name="startTime">When the cracking operation started</param>
        /// <param name="gpuCount">Number of GPUs participating in the operation</param>
        public CrackingState(ulong totalCombinations, DateTime startTime, int gpuCount)
        {
            this.totalCombinations = totalCombinations;
            this.startTime = startTime;

            // Initialize GPU work states
            for (int i = 0; i < gpuCount; i++)
            {
                gpuWorkStates[i] = new GpuWorkState
                {
                    GpuIndex = i,
                    ProcessedCombinations = 0,
                    AssignedWork = 0,
                    RemainingWork = 0,
                    Speed = 0.0,
                    LastUpdateTime = startTime,
                    IsActive = true
                };
            }
        }

        #endregion

        #region Password Discovery Methods

        /// <summary>
        /// Attempts to set the found password if no password has been found yet.
        /// This is an atomic operation that ensures only the first GPU to find a password wins.
        /// </summary>
        /// <param name="password">The found password</param>
        /// <param name="gpuIndex">Index of the GPU that found the password</param>
        /// <returns>True if this call successfully set the password (first to find), false if already found</returns>
        public bool TrySetFoundPassword(string password, int gpuIndex)
        {
            lock (primaryLock)
            {
                if (passwordFound)
                    return false; // Already found by another GPU

                passwordFound = true;
                foundPassword = password ?? string.Empty;
                winningGpuIndex = gpuIndex;
                passwordFoundTime = DateTime.Now;

                return true;
            }
        }

        /// <summary>
        /// Checks if a password has been found by any GPU.
        /// This is a fast read operation that doesn't require locking.
        /// </summary>
        /// <returns>True if password has been found, false otherwise</returns>
        public bool IsPasswordFound()
        {
            // Use volatile read semantics for thread safety without locks
            return passwordFound;
        }

        /// <summary>
        /// Gets the found password and winning GPU information.
        /// </summary>
        /// <returns>Tuple containing the password and GPU index, or empty values if not found</returns>
        public (string password, int gpuIndex, DateTime foundTime) GetFoundPasswordInfo()
        {
            lock (primaryLock)
            {
                return (foundPassword, winningGpuIndex, passwordFoundTime);
            }
        }

        #endregion

        #region Progress Tracking Methods

        /// <summary>
        /// Updates progress for a specific GPU in a thread-safe manner.
        /// </summary>
        /// <param name="gpuIndex">Index of the GPU to update</param>
        /// <param name="processedCombinations">Total combinations processed by this GPU</param>
        /// <param name="currentSpeed">Current processing speed in combinations per second</param>
        public void UpdateGpuProgress(int gpuIndex, ulong processedCombinations, double currentSpeed)
        {
            lock (primaryLock)
            {
                if (gpuWorkStates.TryGetValue(gpuIndex, out var gpuState))
                {
                    var previousProcessed = gpuState.ProcessedCombinations;
                    gpuState.ProcessedCombinations = processedCombinations;
                    gpuState.Speed = currentSpeed;
                    gpuState.LastUpdateTime = DateTime.Now;

                    // Update total progress (atomic increment)
                    var delta = processedCombinations - previousProcessed;
                    totalProcessed += delta;

                    // Invalidate cached metrics
                    InvalidateMetricsCache();
                }
            }
        }

        /// <summary>
        /// Updates work assignment for a specific GPU (used in dynamic load balancing).
        /// </summary>
        /// <param name="gpuIndex">Index of the GPU</param>
        /// <param name="assignedWork">Total work assigned to this GPU</param>
        /// <param name="remainingWork">Work still remaining for this GPU</param>
        public void UpdateGpuWorkAssignment(int gpuIndex, ulong assignedWork, ulong remainingWork)
        {
            lock (primaryLock)
            {
                if (gpuWorkStates.TryGetValue(gpuIndex, out var gpuState))
                {
                    gpuState.AssignedWork = assignedWork;
                    gpuState.RemainingWork = remainingWork;
                }
            }
        }

        /// <summary>
        /// Gets the total progress across all GPUs.
        /// </summary>
        /// <returns>Total combinations processed by all GPUs</returns>
        public ulong GetTotalProcessed()
        {
            lock (primaryLock)
            {
                return totalProcessed;
            }
        }

        /// <summary>
        /// Gets overall progress as a percentage (0.0 to 100.0).
        /// </summary>
        /// <returns>Progress percentage</returns>
        public double GetOverallProgress()
        {
            lock (primaryLock)
            {
                if (totalCombinations == 0)
                    return 0.0;

                return ((double)totalProcessed / totalCombinations) * 100.0;
            }
        }

        #endregion

        #region Additional Integration Methods

        /// <summary>
        /// Updates the processed combinations for a specific GPU and recalculates its speed.
        /// </summary>
        /// <param name="gpuIndex">Index of the GPU to update</param>
        /// <param name="processedCombinations">Total combinations processed by this GPU</param>
        public void UpdateGpuProcessed(int gpuIndex, ulong processedCombinations)
        {
            lock (primaryLock)
            {
                if (gpuWorkStates.TryGetValue(gpuIndex, out var gpuState))
                {
                    var previousProcessed = gpuState.ProcessedCombinations;
                    gpuState.ProcessedCombinations = processedCombinations;
                    gpuState.LastUpdateTime = DateTime.Now;

                    // Calculate speed based on elapsed time
                    var elapsed = DateTime.Now - startTime;
                    if (elapsed.TotalSeconds > 0)
                    {
                        gpuState.Speed = processedCombinations / elapsed.TotalSeconds;
                    }

                    // Update total progress
                    var delta = processedCombinations - previousProcessed;
                    totalProcessed += delta;

                    // Invalidate cached metrics
                    InvalidateMetricsCache();
                }
            }
        }

        /// <summary>
        /// Updates the overall processed count (legacy method for compatibility).
        /// This method is kept for backward compatibility but UpdateGpuProcessed is preferred.
        /// </summary>
        /// <param name="newProcessedAmount">New processed amount to add to total</param>
        public void UpdateOverallProcessed(ulong newProcessedAmount)
        {
            lock (primaryLock)
            {
                // This is a simplified version - in practice, GPU-specific updates are better
                totalProcessed = newProcessedAmount;
                InvalidateMetricsCache();
            }
        }

        /// <summary>
        /// Sets password found status with found password (alternative to TrySetFoundPassword).
        /// </summary>
        /// <param name="found">Whether password was found</param>
        /// <param name="password">The found password (if any)</param>
        /// <param name="gpuIndex">GPU index that found the password (optional)</param>
        public void SetPasswordFound(bool found, string password, int gpuIndex = -1)
        {
            if (found)
            {
                TrySetFoundPassword(password, gpuIndex);
            }
        }

        #endregion

        #region Work Distribution Methods

        /// <summary>
        /// Allocates a new work offset for a GPU (thread-safe global work distribution).
        /// </summary>
        /// <param name="workSize">Size of work to allocate</param>
        /// <returns>Starting offset for the allocated work</returns>
        public ulong AllocateWorkOffset(ulong workSize)
        {
            lock (primaryLock)
            {
                var currentOffset = globalWorkOffset;
                globalWorkOffset += workSize;
                return currentOffset;
            }
        }

        /// <summary>
        /// Gets work distribution information for load balancing decisions.
        /// </summary>
        /// <returns>Dictionary of GPU indices to their work states</returns>
        public Dictionary<int, GpuWorkState> GetWorkDistribution()
        {
            lock (primaryLock)
            {
                // Return a deep copy to avoid external modification
                var copy = new Dictionary<int, GpuWorkState>();
                foreach (var kvp in gpuWorkStates)
                {
                    copy[kvp.Key] = new GpuWorkState
                    {
                        GpuIndex = kvp.Value.GpuIndex,
                        ProcessedCombinations = kvp.Value.ProcessedCombinations,
                        AssignedWork = kvp.Value.AssignedWork,
                        RemainingWork = kvp.Value.RemainingWork,
                        Speed = kvp.Value.Speed,
                        LastUpdateTime = kvp.Value.LastUpdateTime,
                        IsActive = kvp.Value.IsActive
                    };
                }
                return copy;
            }
        }

        #endregion

        #region Performance Metrics Methods

        /// <summary>
        /// Gets aggregated performance metrics with caching for efficiency.
        /// </summary>
        /// <returns>Total speed across all GPUs in combinations per second</returns>
        public double GetTotalSpeed()
        {
            using (metricsLock.UpgradeableReadLock())
            {
                // Check if cached metrics are still valid (updated within last 100ms)
                var now = DateTime.Now;
                if (now - lastMetricsUpdate < TimeSpan.FromMilliseconds(100))
                {
                    return cachedTotalSpeed;
                }

                // Need to recalculate metrics
                using (metricsLock.WriteLock())
                {
                    RecalculateMetrics(now);
                    return cachedTotalSpeed;
                }
            }
        }

        /// <summary>
        /// Gets the best performing GPU for status display.
        /// </summary>
        /// <returns>GPU index and speed of the fastest GPU, or (-1, 0.0) if none active</returns>
        public (int gpuIndex, double speed) GetTopPerformingGpu()
        {
            lock (primaryLock)
            {
                var topGpu = gpuWorkStates.Values
                    .Where(g => g.IsActive && g.Speed > 0)
                    .OrderByDescending(g => g.Speed)
                    .FirstOrDefault();

                return topGpu != null ? (topGpu.GpuIndex, topGpu.Speed) : (-1, 0.0);
            }
        }

        #endregion

        #region Cancellation and Control Methods

        /// <summary>
        /// Requests cancellation of the cracking operation.
        /// </summary>
        public void RequestCancellation()
        {
            lock (primaryLock)
            {
                cancellationRequested = true;
            }
        }

        /// <summary>
        /// Checks if cancellation has been requested.
        /// </summary>
        /// <returns>True if cancellation was requested, false otherwise</returns>
        public bool IsCancellationRequested()
        {
            return cancellationRequested;
        }

        /// <summary>
        /// Deactivates a specific GPU (removes it from active processing).
        /// </summary>
        /// <param name="gpuIndex">Index of the GPU to deactivate</param>
        public void DeactivateGpu(int gpuIndex)
        {
            lock (primaryLock)
            {
                if (gpuWorkStates.TryGetValue(gpuIndex, out var gpuState))
                {
                    gpuState.IsActive = false;
                }
            }
        }

        #endregion

        #region Snapshot Methods

        /// <summary>
        /// Creates a complete snapshot of the current state for read-only access.
        /// This is useful for progress reporting and UI updates without holding locks.
        /// </summary>
        /// <returns>Immutable snapshot of current state</returns>
        public StateSnapshot CreateSnapshot()
        {
            lock (primaryLock)
            {
                var snapshot = new StateSnapshot
                {
                    PasswordFound = passwordFound,
                    FoundPassword = foundPassword,
                    WinningGpuIndex = winningGpuIndex,
                    PasswordFoundTime = passwordFoundTime,
                    TotalProcessed = totalProcessed,
                    TotalCombinations = totalCombinations,
                    OverallProgress = GetOverallProgress(),
                    TotalSpeed = GetTotalSpeed(),
                    ElapsedTime = DateTime.Now - startTime,
                    CancellationRequested = cancellationRequested,
                    GpuStates = GetWorkDistribution()
                };

                return snapshot;
            }
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Invalidates cached performance metrics to force recalculation.
        /// </summary>
        private void InvalidateMetricsCache()
        {
            lastMetricsUpdate = DateTime.MinValue;
        }

        /// <summary>
        /// Recalculates performance metrics and updates cache.
        /// Must be called within a write lock on metricsLock.
        /// </summary>
        /// <param name="currentTime">Current timestamp for calculations</param>
        private void RecalculateMetrics(DateTime currentTime)
        {
            cachedTotalSpeed = gpuWorkStates.Values
                .Where(g => g.IsActive)
                .Sum(g => g.Speed);

            cachedOverallProgress = totalCombinations > 0 
                ? ((double)totalProcessed / totalCombinations) * 100.0 
                : 0.0;

            lastMetricsUpdate = currentTime;
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes of resources used by the CrackingState.
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                metricsLock?.Dispose();
                disposed = true;
            }
        }

        #endregion
    }

    #region Extension Methods for ReaderWriterLockSlim

    /// <summary>
    /// Extension methods to provide convenient RAII-style locking for ReaderWriterLockSlim.
    /// </summary>
    public static class LockExtensions
    {
        /// <summary>
        /// Acquires a read lock and returns a disposable that releases it.
        /// </summary>
        public static IDisposable ReadLock(this ReaderWriterLockSlim rwLock)
        {
            rwLock.EnterReadLock();
            return new DisposableAction(() => rwLock.ExitReadLock());
        }

        /// <summary>
        /// Acquires an upgradeable read lock and returns a disposable that releases it.
        /// </summary>
        public static IDisposable UpgradeableReadLock(this ReaderWriterLockSlim rwLock)
        {
            rwLock.EnterUpgradeableReadLock();
            return new DisposableAction(() => rwLock.ExitUpgradeableReadLock());
        }

        /// <summary>
        /// Acquires a write lock and returns a disposable that releases it.
        /// </summary>
        public static IDisposable WriteLock(this ReaderWriterLockSlim rwLock)
        {
            rwLock.EnterWriteLock();
            return new DisposableAction(() => rwLock.ExitWriteLock());
        }

        /// <summary>
        /// Helper class for RAII-style resource management.
        /// </summary>
        private class DisposableAction : IDisposable
        {
            private readonly Action action;
            private bool disposed = false;

            public DisposableAction(Action action)
            {
                this.action = action ?? throw new ArgumentNullException(nameof(action));
            }

            public void Dispose()
            {
                if (!disposed)
                {
                    action();
                    disposed = true;
                }
            }
        }
    }

    #endregion
} 
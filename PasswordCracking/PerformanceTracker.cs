using System;
using System.Collections.Generic;
using System.Linq;

namespace PasswordCracking
{
    /// <summary>
    /// Centralized performance tracking and metrics calculation for GPU password cracking operations.
    /// Handles individual GPU performance, aggregate metrics, progress calculations, and formatting.
    /// </summary>
    public class PerformanceTracker
    {
        #region Performance Data

        /// <summary>
        /// Performance snapshot for a specific point in time
        /// </summary>
        public class PerformanceSnapshot
        {
            public DateTime Timestamp { get; set; }
            public ulong TotalCombinationsProcessed { get; set; }
            public double TotalSpeed { get; set; } // combinations per second
            public double OverallProgress { get; set; } // percentage (0-100)
            public TimeSpan ElapsedTime { get; set; }
            public TimeSpan EstimatedTimeRemaining { get; set; }
            public List<GpuPerformanceData> GpuMetrics { get; set; } = new List<GpuPerformanceData>();
            public string FormattedStatus { get; set; } = string.Empty;
        }

        /// <summary>
        /// Performance data for an individual GPU
        /// </summary>
        public class GpuPerformanceData
        {
            public int Index { get; set; }
            public string Name { get; set; } = string.Empty;
            public ulong CombinationsProcessed { get; set; }
            public double Speed { get; set; } // combinations per second
            public double Progress { get; set; } // percentage (0-100) 
            public double PerformanceRatio { get; set; } // relative to total performance
            public ulong WorkAssigned { get; set; }
            public ulong WorkRemaining { get; set; }
            public TimeSpan GpuElapsedTime { get; set; }
        }

        #endregion

        #region Private Fields

        private readonly DateTime startTime;
        private readonly ulong totalCombinations;
        private readonly List<GpuInfo> gpus;
        private readonly object lockObject = new object();

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new PerformanceTracker for monitoring GPU cracking performance
        /// </summary>
        /// <param name="selectedGpus">List of GPUs to track</param>
        /// <param name="totalCombinations">Total search space size</param>
        /// <param name="startTime">When the cracking operation started</param>
        public PerformanceTracker(List<GpuInfo> selectedGpus, ulong totalCombinations, DateTime startTime)
        {
            this.gpus = selectedGpus ?? throw new ArgumentNullException(nameof(selectedGpus));
            this.totalCombinations = totalCombinations;
            this.startTime = startTime;

            InitializeGpuMetrics();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Updates performance metrics for a specific GPU
        /// </summary>
        /// <param name="gpuIndex">Index of the GPU to update</param>
        /// <param name="combinationsProcessed">Total combinations processed by this GPU</param>
        /// <param name="currentTime">Current timestamp for calculations</param>
        public void UpdateGpuPerformance(int gpuIndex, ulong combinationsProcessed, DateTime currentTime)
        {
            if (gpuIndex < 0 || gpuIndex >= gpus.Count)
                return;

            lock (lockObject)
            {
                var gpu = gpus[gpuIndex];
                gpu.TotalProcessed = combinationsProcessed;
                gpu.LastUpdateTime = currentTime;

                // Calculate speed (combinations per second)
                var elapsed = currentTime - startTime;
                if (elapsed.TotalSeconds > 0)
                {
                    gpu.CombinationsPerSecond = combinationsProcessed / elapsed.TotalSeconds;
                }
            }
        }

        /// <summary>
        /// Updates performance ratios for dynamic load balancing
        /// </summary>
        public void UpdatePerformanceRatios()
        {
            lock (lockObject)
            {
                var elapsed = DateTime.Now - startTime;
                double totalPerformance = 0;

                // Calculate individual GPU performance
                foreach (var gpu in gpus)
                {
                    if (elapsed.TotalSeconds > 0 && gpu.TotalProcessed > 0)
                    {
                        gpu.CombinationsPerSecond = gpu.TotalProcessed / elapsed.TotalSeconds;
                        totalPerformance += gpu.CombinationsPerSecond;
                    }
                }

                // Calculate performance ratios
                if (totalPerformance > 0)
                {
                    foreach (var gpu in gpus)
                    {
                        gpu.PerformanceRatio = gpu.CombinationsPerSecond / totalPerformance;
                    }
                }
            }
        }

        /// <summary>
        /// Gets current performance snapshot with all metrics
        /// </summary>
        /// <returns>Complete performance snapshot</returns>
        public PerformanceSnapshot GetCurrentSnapshot()
        {
            lock (lockObject)
            {
                var currentTime = DateTime.Now;
                var elapsed = currentTime - startTime;

                var snapshot = new PerformanceSnapshot
                {
                    Timestamp = currentTime,
                    ElapsedTime = elapsed
                };

                // Calculate aggregate metrics
                snapshot.TotalCombinationsProcessed = (ulong)gpus.Sum(g => (long)g.TotalProcessed);
                snapshot.TotalSpeed = gpus.Sum(g => g.CombinationsPerSecond);
                
                // Calculate progress percentage
                snapshot.OverallProgress = totalCombinations > 0 
                    ? (double)snapshot.TotalCombinationsProcessed / totalCombinations * 100.0 
                    : 0.0;

                // Calculate ETA
                if (snapshot.TotalSpeed > 0 && snapshot.OverallProgress < 100)
                {
                    var remainingCombinations = totalCombinations - snapshot.TotalCombinationsProcessed;
                    var etaSeconds = remainingCombinations / snapshot.TotalSpeed;
                    snapshot.EstimatedTimeRemaining = TimeSpan.FromSeconds(etaSeconds);
                }

                // Copy GPU metrics
                snapshot.GpuMetrics = gpus.Select((gpu, index) => new GpuPerformanceData
                {
                    Index = index,
                    Name = gpu.Name,
                    CombinationsProcessed = gpu.TotalProcessed,
                    Speed = gpu.CombinationsPerSecond,
                    Progress = gpu.WorkAssigned > 0 ? (double)gpu.TotalProcessed / gpu.WorkAssigned * 100.0 : 0.0,
                    PerformanceRatio = gpu.PerformanceRatio,
                    WorkAssigned = gpu.WorkAssigned,
                    WorkRemaining = gpu.WorkRemaining,
                    GpuElapsedTime = elapsed
                }).ToList();

                // Generate formatted status
                snapshot.FormattedStatus = GenerateStatusMessage(snapshot);

                return snapshot;
            }
        }

        /// <summary>
        /// Gets simple progress information for progress callbacks
        /// </summary>
        /// <returns>Progress value (0.0 to 1.0) and status message</returns>
        public (double progress, string status) GetProgressInfo()
        {
            var snapshot = GetCurrentSnapshot();
            return (snapshot.OverallProgress / 100.0, snapshot.FormattedStatus);
        }

        /// <summary>
        /// Gets aggregated total progress across all GPUs
        /// </summary>
        /// <returns>Total combinations processed by all GPUs</returns>
        public ulong GetTotalProcessed()
        {
            lock (lockObject)
            {
                return (ulong)gpus.Sum(g => (long)g.TotalProcessed);
            }
        }

        /// <summary>
        /// Gets the GPU with highest performance (for winner identification)
        /// </summary>
        /// <returns>Best performing GPU or null</returns>
        public GpuInfo? GetTopPerformingGpu()
        {
            lock (lockObject)
            {
                return gpus.OrderByDescending(g => g.CombinationsPerSecond).FirstOrDefault();
            }
        }

        #endregion

        #region Formatting Methods

        /// <summary>
        /// Formats speed in human-readable format (K/s, M/s, B/s)
        /// </summary>
        /// <param name="combinationsPerSecond">Speed to format</param>
        /// <returns>Formatted speed string</returns>
        public static string FormatSpeed(double combinationsPerSecond)
        {
            if (combinationsPerSecond >= KernelConstants.SPEED_THRESHOLD_BILLION)
                return $"{combinationsPerSecond / KernelConstants.SPEED_THRESHOLD_BILLION:F1}B/s";
            else if (combinationsPerSecond >= KernelConstants.SPEED_THRESHOLD_MILLION)
                return $"{combinationsPerSecond / KernelConstants.SPEED_THRESHOLD_MILLION:F1}M/s";
            else if (combinationsPerSecond >= KernelConstants.SPEED_THRESHOLD_THOUSAND)
                return $"{combinationsPerSecond / KernelConstants.SPEED_THRESHOLD_THOUSAND:F1}K/s";
            else
                return $"{combinationsPerSecond:F0}/s";
        }

        /// <summary>
        /// Formats large numbers in human-readable format (K, M, B, T)
        /// </summary>
        /// <param name="number">Number to format</param>
        /// <returns>Formatted number string</returns>
        public static string FormatLargeNumber(ulong number)
        {
            if (number >= 1000000000000UL)
                return $"{number / 1000000000000.0:F1}T";
            else if (number >= 1000000000UL)
                return $"{number / 1000000000.0:F1}B";
            else if (number >= 1000000UL)
                return $"{number / 1000000.0:F1}M";
            else if (number >= 1000UL)
                return $"{number / 1000.0:F1}K";
            else
                return number.ToString("N0");
        }

        /// <summary>
        /// Formats time duration in human-readable format
        /// </summary>
        /// <param name="timeSpan">Time duration to format</param>
        /// <returns>Formatted time string</returns>
        public static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalHours >= 1)
                return $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            else
                return $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        }

        /// <summary>
        /// Creates a progress bar string for console display
        /// </summary>
        /// <param name="progress">Progress percentage (0-100)</param>
        /// <param name="width">Width of the progress bar in characters</param>
        /// <returns>Progress bar string</returns>
        public static string CreateProgressBar(double progress, int width = 30)
        {
            int filledWidth = (int)(progress / 100.0 * width);
            return "[" + new string('█', filledWidth) + new string('░', width - filledWidth) + "]";
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Initializes GPU performance metrics
        /// </summary>
        private void InitializeGpuMetrics()
        {
            foreach (var gpu in gpus)
            {
                gpu.CombinationsPerSecond = 0;
                gpu.TotalProcessed = 0;
                gpu.LastUpdateTime = startTime;
                gpu.PerformanceRatio = 1.0 / gpus.Count; // Start with equal distribution
            }
        }

        /// <summary>
        /// Generates a formatted status message for the current performance snapshot
        /// </summary>
        /// <param name="snapshot">Performance snapshot to describe</param>
        /// <returns>Formatted status message</returns>
        private string GenerateStatusMessage(PerformanceSnapshot snapshot)
        {
            if (snapshot.OverallProgress >= 100)
            {
                return "Search completed";
            }
            else if (snapshot.TotalSpeed > 0)
            {
                var eta = FormatTimeSpan(snapshot.EstimatedTimeRemaining);
                return $"Searching... {snapshot.OverallProgress:F1}% | {FormatSpeed(snapshot.TotalSpeed)} | ETA: {eta}";
            }
            else
            {
                return $"Searching... {snapshot.OverallProgress:F1}%";
            }
        }

        #endregion
    }
} 
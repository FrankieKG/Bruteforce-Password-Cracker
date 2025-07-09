using OpenCL.Net;

namespace PasswordCracking
{
    public class GpuInfo
    {
        // Core GPU properties (always used)
        public string Name { get; set; } = string.Empty;
        public Device Device { get; set; }
        public Platform OpenCLPlatform { get; set; }
        public long MemoryBytes { get; set; }
        public int ComputeUnits { get; set; }
        
        // User configuration (always used)
        public ulong BatchSize { get; set; }
        public int MemoryLimitPercent { get; set; }
        public bool IsSelected { get; set; }
        
        // Dynamic load balancing properties (only used in dynamic algorithm)
        public double CombinationsPerSecond { get; set; }
        public ulong TotalProcessed { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public double PerformanceRatio { get; set; }
        public ulong WorkAssigned { get; set; }
        public ulong WorkRemaining { get; set; }
    }
}
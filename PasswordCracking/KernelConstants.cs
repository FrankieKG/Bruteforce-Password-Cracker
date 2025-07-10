namespace PasswordCracking
{
    /// <summary>
    /// Constants for GPU password cracking operations
    /// </summary>
    public static class KernelConstants
    {
        // OpenCL Kernel Configuration
        public const string KERNEL_FILENAME = "sha256.cl";
        public const string KERNEL_FUNCTION_NAME = "generate_and_hash_kernel";
        
        // Password Length Configuration
        public const int MIN_PASSWORD_LENGTH = 1;
        public const int MAX_PASSWORD_LENGTH_SUPPORTED = 16; // Practical maximum for brute force
        public const int DEFAULT_MAX_PASSWORD_LENGTH = 8; // Reasonable default for testing
        public const int RECOMMENDED_MAX_LENGTH_FAST = 6; // Fast cracking for testing
        public const int RECOMMENDED_MAX_LENGTH_THOROUGH = 12; // More thorough search
        
        // Buffer and Memory Configuration
        public const int MAX_PASSWORD_LENGTH_BYTES = 32;
        public const int PASSWORD_BUFFER_SIZE = 32; // Must match MAX_PASSWORD_LENGTH_BYTES
        public const int SHA256_HASH_SIZE_BYTES = 32; // SHA-256 produces 32-byte hashes
        
        // Memory conversion constants
        public const long BYTES_PER_GB = 1024L * 1024L * 1024L; // 1 GB in bytes
        
        // Default GPU Settings
        public const ulong DEFAULT_BATCH_SIZE = 1_000_000;
        public const int DEFAULT_MEMORY_LIMIT_PERCENT = 80;
        public const ulong MAX_BATCH_SIZE = 100_000_000; // 100 million combinations
        public const int MIN_MEMORY_LIMIT_PERCENT = 1;
        public const int MAX_MEMORY_LIMIT_PERCENT = 100;
        
        // Performance and Progress Configuration
        public const double PERFORMANCE_UPDATE_INTERVAL_SECONDS = 0.5; // Update every 500ms
        public const int PROGRESS_UPDATE_INTERVAL_MS = 50; // GUI progress task polling interval
        public const int CONSOLE_PROGRESS_UPDATE_INTERVAL_MS = 100; // Console progress update
        public const int DYNAMIC_ALGORITHM_UPDATE_INTERVAL_MS = 200; // Dynamic load balancing
        
        // Progress Bar Display Configuration
        public const int PROGRESS_BAR_WIDTH = 50;
        public const int PROGRESS_BAR_WIDTH_SMALL = 40; // For individual GPU bars (50-10)
        public const double PROGRESS_COMPLETE_PERCENT = 100.0;
        
        // Speed Formatting Thresholds
        public const double SPEED_THRESHOLD_BILLION = 1_000_000_000;
        public const double SPEED_THRESHOLD_MILLION = 1_000_000;
        public const double SPEED_THRESHOLD_THOUSAND = 1_000;
        
        // Large Number Formatting Thresholds  
        public const ulong NUMBER_THRESHOLD_TRILLION = 1_000_000_000_000;
        public const ulong NUMBER_THRESHOLD_BILLION = 1_000_000_000;
        public const ulong NUMBER_THRESHOLD_MILLION = 1_000_000;
        public const ulong NUMBER_THRESHOLD_THOUSAND = 1_000;
        
        // Algorithm Selection
        public const string ALGORITHM_AUTO_SELECT = "Auto-Select";
        public const string ALGORITHM_SIMPLE = "Simple Multi-GPU";
        public const string ALGORITHM_DYNAMIC = "Dynamic Load Balance";
        
        // OpenCL Kernel Arguments (for documentation and validation)
        public const int KERNEL_ARG_CHARSET = 0;
        public const int KERNEL_ARG_CHARSET_LENGTH = 1;
        public const int KERNEL_ARG_MAX_LENGTH = 2;
        public const int KERNEL_ARG_START_LENGTH = 3;
        public const int KERNEL_ARG_TARGET_HASH = 4;
        public const int KERNEL_ARG_FOUND_FLAG = 5;
        public const int KERNEL_ARG_FOUND_PASSWORD = 6;
        public const int KERNEL_ARG_TOTAL_COMBINATIONS = 7;
        public const int KERNEL_ARG_BATCH_OFFSET = 8;
        public const int KERNEL_ARG_FOUND_INDEX = 9;
        
        // Recommended Settings by GPU Memory (in GB)
        public static class RecommendedSettings
        {
            // High-end GPUs (8+ GB)
            public const ulong HIGH_MEMORY_BATCH_SIZE = 5_000_000;
            public const int HIGH_MEMORY_THRESHOLD_GB = 8;
            
            // Mid-range GPUs (4-8 GB)  
            public const ulong MEDIUM_MEMORY_BATCH_SIZE = 2_000_000;
            public const int MEDIUM_MEMORY_THRESHOLD_GB = 4;
            
            // Low-end GPUs (2-4 GB)
            public const ulong LOW_MEMORY_BATCH_SIZE = 500_000;
            public const int LOW_MEMORY_THRESHOLD_GB = 2;
            
            // Very low memory GPUs (< 2 GB)
            public const ulong VERY_LOW_MEMORY_BATCH_SIZE = 100_000;
        }
        
        // Error Messages
        public static class ErrorMessages
        {
            public const string NO_GPUS_SELECTED = "At least one GPU must be selected";
            public const string BATCH_SIZE_TOO_LARGE = "Batch size too large (max: {0:N0}). Please enter a smaller value.";
            public const string INVALID_MEMORY_LIMIT = "Invalid memory limit. Please enter a number between {0}-{1}.";
            public const string INVALID_PASSWORD_LENGTH = "Password length must be between {0} and {1} characters.";
            public const string OPENCL_ERROR = "OpenCL operation failed with error: {0}";
            public const string GPU_OPERATION_FAILED = "GPU {0} operation failed: {1}";
            public const string RESOURCE_CLEANUP_FAILED = "Failed to cleanup OpenCL resources: {0}";
            public const string KERNEL_BUILD_FAILED = "OpenCL kernel compilation failed. Check kernel file syntax.";
            public const string DEVICE_INITIALIZATION_FAILED = "Failed to initialize OpenCL device: {0}";
        }
    }
} 
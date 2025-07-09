using OpenCL.Net;
using System;
using System.Collections.Generic;

namespace PasswordCracking
{
    /// <summary>
    /// Utility class for detecting and configuring OpenCL-compatible GPUs.
    /// 
    /// <para><strong>Purpose:</strong></para>
    /// <para>Consolidates GPU detection logic that was previously duplicated between console and GUI applications.
    /// Provides a clean, reusable interface for discovering OpenCL devices and creating GpuInfo objects.</para>
    /// 
    /// <para><strong>OpenCL Detection Process:</strong></para>
    /// <list type="number">
    /// <item><description>Platform Enumeration: Discovers all OpenCL platforms (NVIDIA CUDA, AMD ROCm, etc.)</description></item>
    /// <item><description>Device Discovery: Finds GPU devices within each platform</description></item>
    /// <item><description>Device Information: Queries device capabilities (name, memory, compute units)</description></item>
    /// <item><description>GpuInfo Creation: Constructs properly configured GpuInfo objects</description></item>
    /// </list>
    /// 
    /// <para><strong>Error Handling:</strong></para>
    /// <para>Gracefully handles OpenCL errors and missing devices. Individual device failures don't prevent
    /// detection of other working GPUs. Supports both throwing exceptions and silent error handling modes.</para>
    /// </summary>
    public static class OpenClGpuDetector
    {
        /// <summary>
        /// Detects all available OpenCL-compatible GPUs in the system.
        /// 
        /// <para><strong>Detection Strategy:</strong></para>
        /// <para>Enumerates all OpenCL platforms and devices, extracting GPU capabilities and creating
        /// properly initialized GpuInfo objects. Continues processing even if individual devices fail.</para>
        /// 
        /// <para><strong>Default Configuration:</strong></para>
        /// <para>Sets reasonable default values for batch size and memory limits that work across
        /// different GPU configurations. These can be customized later through user settings.</para>
        /// </summary>
        /// <param name="enableDefaultSettings">If true, sets default batch size and memory limit values</param>
        /// <param name="silentErrors">If true, logs errors without throwing exceptions</param>
        /// <returns>List of detected GPUs with their capabilities and default settings</returns>
        /// <exception cref="OpenClException">If silentErrors is false and OpenCL operations fail critically</exception>
        public static List<GpuInfo> DetectAvailableGpus(bool enableDefaultSettings = true, bool silentErrors = true)
        {
            var gpus = new List<GpuInfo>();

            try
            {
                // Query available OpenCL platforms
                uint numPlatforms;
                ErrorCode error = Cl.GetPlatformIDs(0, null, out numPlatforms);
                
                if (!silentErrors && error != ErrorCode.Success)
                    throw new OpenClException(error, "Failed to query OpenCL platforms");
                
                if (numPlatforms == 0)
                    return gpus; // No platforms available

                // Get platform list
                var platforms = new Platform[numPlatforms];
                error = Cl.GetPlatformIDs(numPlatforms, platforms, out numPlatforms);
                
                if (!silentErrors && error != ErrorCode.Success)
                    throw new OpenClException(error, "Failed to retrieve OpenCL platforms");

                // Enumerate devices in each platform
                foreach (var platform in platforms)
                {
                    try
                    {
                        var devices = Cl.GetDeviceIDs(platform, DeviceType.Gpu, out error);
                        
                        // Skip this platform if no GPU devices or error occurred
                        if (error != ErrorCode.Success || devices == null)
                            continue;

                        // Process each GPU device
                        foreach (var device in devices)
                        {
                            try
                            {
                                var gpu = CreateGpuInfoFromDevice(device, platform, enableDefaultSettings, silentErrors);
                                if (gpu != null)
                                    gpus.Add(gpu);
                            }
                            catch (Exception ex)
                            {
                                if (!silentErrors)
                                    throw;
                                
                                // Log error but continue with other devices
                                Console.WriteLine($"Warning: Failed to process GPU device: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!silentErrors)
                            throw;
                        
                        // Log platform error but continue with other platforms
                        Console.WriteLine($"Warning: Failed to process OpenCL platform: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!silentErrors)
                    throw new OpenClException(ErrorCode.DeviceNotFound, 
                        $"GPU detection failed: {ex.Message}", ex);
                
                // Silent error mode - log and return empty list
                Console.WriteLine($"GPU detection error: {ex.Message}");
            }

            return gpus;
        }

        /// <summary>
        /// Creates a GpuInfo object from an OpenCL device with comprehensive device information.
        /// 
        /// <para><strong>Device Information Extraction:</strong></para>
        /// <para>Queries the OpenCL device for essential properties including name, memory size,
        /// and compute units. Handles extraction errors gracefully to ensure partial information
        /// doesn't prevent GPU object creation.</para>
        /// 
        /// <para><strong>Default Settings Logic:</strong></para>
        /// <para>When enabled, applies intelligent defaults based on GPU memory size:
        /// - High-end GPUs (8+ GB): Larger batch sizes for better performance
        /// - Mid-range GPUs (4-8 GB): Balanced settings for stability
        /// - Low-end GPUs (2-4 GB): Conservative settings to prevent memory issues</para>
        /// </summary>
        /// <param name="device">OpenCL device to extract information from</param>
        /// <param name="platform">OpenCL platform containing the device</param>
        /// <param name="enableDefaultSettings">Whether to apply default configuration values</param>
        /// <param name="silentErrors">Whether to suppress individual property extraction errors</param>
        /// <returns>Configured GpuInfo object, or null if device information cannot be extracted</returns>
        private static GpuInfo? CreateGpuInfoFromDevice(Device device, Platform platform, 
                                                       bool enableDefaultSettings, bool silentErrors)
        {
            var gpu = new GpuInfo
            {
                Device = device,
                OpenCLPlatform = platform
            };

            // Extract device name
            try
            {
                var nameBuffer = Cl.GetDeviceInfo(device, DeviceInfo.Name, out ErrorCode error);
                if (error == ErrorCode.Success)
                    gpu.Name = nameBuffer.ToString();
                else if (!silentErrors)
                    throw new OpenClException(error, "Failed to get device name");
            }
            catch (Exception)
            {
                if (!silentErrors) throw;
                gpu.Name = "Unknown GPU"; // Fallback name
            }

            // Extract memory size
            try
            {
                var memoryBuffer = Cl.GetDeviceInfo(device, DeviceInfo.GlobalMemSize, out ErrorCode error);
                if (error == ErrorCode.Success)
                    gpu.MemoryBytes = (long)memoryBuffer.CastTo<ulong>();
                else if (!silentErrors)
                    throw new OpenClException(error, "Failed to get device memory size");
            }
            catch (Exception)
            {
                if (!silentErrors) throw;
                gpu.MemoryBytes = 0; // Unknown memory
            }

            // Extract compute units
            try
            {
                var computeUnitsBuffer = Cl.GetDeviceInfo(device, DeviceInfo.MaxComputeUnits, out ErrorCode error);
                if (error == ErrorCode.Success)
                    gpu.ComputeUnits = (int)computeUnitsBuffer.CastTo<uint>();
                else if (!silentErrors)
                    throw new OpenClException(error, "Failed to get device compute units");
            }
            catch (Exception)
            {
                if (!silentErrors) throw;
                gpu.ComputeUnits = 0; // Unknown compute units
            }

            // Apply default settings if requested
            if (enableDefaultSettings)
            {
                ApplyConservativeDefaults(gpu);
            }

            return gpu;
        }

        /// <summary>
        /// Applies conservative default settings optimized for progress tracking responsiveness.
        /// 
        /// <para><strong>Responsive Progress:</strong></para>
        /// <para>Uses smaller batch sizes for weak GPUs to ensure frequent progress updates.
        /// Strong GPUs get larger batches for efficiency, weak GPUs get smaller batches for responsiveness.</para>
        /// 
        /// <para><strong>GPU Performance Categories:</strong></para>
        /// <para>- Integrated/Low-end: 100K batch for frequent updates</para>
        /// <para>- Mid-range: 500K batch for balanced performance</para>
        /// <para>- High-end: 1M batch for maximum efficiency</para>
        /// </summary>
        /// <param name="gpu">GpuInfo object to configure with responsive default settings</param>
        private static void ApplyConservativeDefaults(GpuInfo gpu)
        {
            string gpuName = gpu.Name?.ToLower() ?? "";
            
            // Apply batch size based on GPU type for responsive progress tracking
            if (gpuName.Contains("gfx") || gpuName.Contains("uhd") || 
                gpuName.Contains("iris") || gpu.ComputeUnits <= 8)
            {
                // Integrated/weak GPU: Small batch for frequent progress updates
                gpu.BatchSize = 100_000; // 100K - updates every few seconds
            }
            else if (gpu.ComputeUnits <= 20 || gpu.MemoryBytes < 4L * KernelConstants.BYTES_PER_GB) // < 4GB
            {
                // Mid-range GPU: Medium batch size
                gpu.BatchSize = 500_000; // 500K - balanced performance/responsiveness
            }
            else
            {
                // High-end GPU: Original batch size for maximum performance
                gpu.BatchSize = KernelConstants.DEFAULT_BATCH_SIZE; // 1,000,000
            }
            
            // Use the original default memory limit
            gpu.MemoryLimitPercent = KernelConstants.DEFAULT_MEMORY_LIMIT_PERCENT; // 80%
            
            // Default to selected for user convenience
            gpu.IsSelected = true;
        }

        /// <summary>
        /// Performs lightweight GPU detection specifically for GUI applications.
        /// 
        /// <para><strong>GUI Optimization:</strong></para>
        /// <para>Uses silent error handling to prevent exception dialogs during GUI initialization.
        /// Applies conservative defaults (1M batch, 80% memory, selected) for immediate usability.</para>
        /// 
        /// <para><strong>Error Resilience:</strong></para>
        /// <para>Designed to never crash the GUI application. If OpenCL detection fails completely,
        /// returns an empty list that can be handled gracefully by the UI.</para>
        /// </summary>
        /// <returns>List of detected GPUs with conservative default settings for immediate use</returns>
        public static List<GpuInfo> DetectGpusForGui()
        {
            return DetectAvailableGpus(enableDefaultSettings: true, silentErrors: true);
        }

        /// <summary>
        /// Performs comprehensive GPU detection for console applications with detailed error reporting.
        /// 
        /// <para><strong>Console Optimization:</strong></para>
        /// <para>Provides detailed error information suitable for console output and debugging.
        /// Applies conservative defaults (1M batch, 80% memory, selected) for immediate use.</para>
        /// 
        /// <para><strong>Error Handling:</strong></para>
        /// <para>Uses exception-based error handling for critical failures while still being
        /// resilient to individual device detection errors.</para>
        /// </summary>
        /// <returns>List of detected GPUs with conservative default settings for immediate use</returns>
        /// <exception cref="OpenClException">If critical OpenCL operations fail</exception>
        public static List<GpuInfo> DetectGpusForConsole()
        {
            return DetectAvailableGpus(enableDefaultSettings: true, silentErrors: false);
        }
    }
} 
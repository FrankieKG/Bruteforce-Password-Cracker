using System.Text;
using System.Linq;
using OpenCL.Net;
using System.Threading.Tasks;

namespace PasswordCracking
{
  /// <summary>
  /// Core OpenCL GPU kernel manager for password cracking operations.
  /// 
  /// <para><strong>OpenCL Architecture Overview:</strong></para>
  /// <para>This class manages the complete OpenCL stack for GPU-accelerated password cracking:</para>
  /// <list type="bullet">
  /// <item><description><strong>Platforms:</strong> OpenCL implementations (e.g., NVIDIA CUDA, AMD ROCm)</description></item>
  /// <item><description><strong>Devices:</strong> Individual GPUs available for computation</description></item>
  /// <item><description><strong>Contexts:</strong> Execution environments that manage GPU memory and resources</description></item>
  /// <item><description><strong>Command Queues:</strong> Task schedulers that sequence operations on each GPU</description></item>
  /// <item><description><strong>Programs:</strong> Compiled GPU kernel code (from .cl files)</description></item>
  /// <item><description><strong>Kernels:</strong> Individual functions that run on the GPU</description></item>
  /// </list>
  /// 
  /// <para><strong>Multi-GPU Coordination:</strong></para>
  /// <para>Supports multiple algorithm strategies for distributing work across different GPU configurations:</para>
  /// <list type="bullet">
  /// <item><description><strong>Simple Distribution:</strong> Equal work division, best for homogeneous GPUs</description></item>
  /// <item><description><strong>Dynamic Load Balancing:</strong> Performance-based work distribution for mixed GPU setups</description></item>
  /// </list>
  /// 
  /// <para><strong>Resource Management:</strong></para>
  /// <para>Uses GpuResourceManager for each GPU to encapsulate OpenCL resource lifecycle and</para>
  /// <para>ensure proper cleanup of unmanaged GPU memory and OpenCL objects.</para>
  /// </summary>
  public class Kernel
  {
    // OpenCL infrastructure arrays - one element per selected GPU
    /// <summary>
    /// OpenCL contexts - one per GPU, manages the GPU's memory space and execution environment.
    /// Context creation requires platform and device selection and establishes the foundation
    /// for all other OpenCL operations on that specific GPU.
    /// </summary>
    private Context[]? contexts;
    
    /// <summary>
    /// OpenCL command queues - one per GPU, schedules operations for execution.
    /// All GPU operations (memory transfers, kernel execution) go through the command queue
    /// which can execute them synchronously or asynchronously based on the operation flags.
    /// </summary>
    private CommandQueue[]? commandQueues;
    
    /// <summary>
    /// OpenCL programs - one per GPU, contains the compiled kernel source code.
    /// Programs are created from .cl source files and compiled for each specific GPU's
    /// architecture to optimize performance for that hardware.
    /// </summary>
    private Program[]? programs;
    
    /// <summary>
    /// OpenCL devices - one per GPU, represents the physical GPU hardware.
    /// Devices are discovered through platform enumeration and provide information
    /// about GPU capabilities, memory, and compute units.
    /// </summary>
    private Device[]? devices;
    
    /// <summary>
    /// Selected GPUs with their configuration and performance metrics.
    /// Contains user-chosen GPUs along with their batch sizes, memory limits,
    /// and runtime performance data for dynamic load balancing.
    /// </summary>
    private List<GpuInfo>? selectedGpus;
    
    /// <summary>
    /// Internal algorithm factory for creating password cracking strategies.
    /// Encapsulates the strategy pattern implementation for different multi-GPU
    /// distribution algorithms while maintaining access to private Kernel methods.
    /// </summary>
    private InternalAlgorithmFactory? internalAlgorithmFactory;
    
    /// <summary>
    /// Performance tracker for monitoring GPU metrics and progress calculation.
    /// Centralized component that tracks combinations per second, progress percentages,
    /// estimated time remaining, and individual GPU performance for load balancing.
    /// </summary>
    private PerformanceTracker? performanceTracker;

    /// <summary>
    /// Current cracking state for cancellation support.
    /// </summary>
    private CrackingState? currentCrackingState;

    /// <summary>
    /// Gets the file path for the OpenCL kernel source code.
    /// 
    /// <para><strong>Kernel File Location:</strong></para>
    /// <para>Searches for the .cl file in the application's base directory. The kernel</para>
    /// <para>contains the GPU-side password generation and SHA-256 hashing logic written in OpenCL C.</para>
    /// </summary>
    /// <returns>Full path to the sha256.cl kernel file</returns>
    /// <exception cref="FileNotFoundException">If the kernel file cannot be found</exception>
    private string GetKernelFilePath()
    {
      // Kernel file should ALWAYS be deployed alongside the executable
      string assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
      string kernelPath = Path.Combine(assemblyDir, KernelConstants.KERNEL_FILENAME);
      
      if (!File.Exists(kernelPath))
      {
        throw new FileNotFoundException(
          $"Kernel file '{KernelConstants.KERNEL_FILENAME}' not found at expected location: {kernelPath}. " +
          "Check that the file is properly deployed with the application.");
      }
      
      return kernelPath;
    }

    /// <summary>
    /// Initializes OpenCL with automatic GPU detection and user selection.
    /// 
    /// <para><strong>Initialization Process:</strong></para>
    /// <list type="number">
    /// <item><description>Detects all available OpenCL-capable GPUs in the system</description></item>
    /// <item><description>Presents user with GPU selection interface</description></item>
    /// <item><description>Configures batch sizes and memory limits per GPU</description></item>
    /// <item><description>Creates OpenCL contexts, command queues, and compiles kernels</description></item>
    /// <item><description>Initializes algorithm factory for strategy pattern implementation</description></item>
    /// </list>
    /// 
    /// <para><strong>Error Handling:</strong></para>
    /// <para>Comprehensive error checking at each step with detailed error messages.</para>
    /// <para>Failed GPU initialization does not crash the application - only working GPUs are used.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">If no working GPUs are found or selected</exception>
    /// <summary>
    /// Initializes the complete OpenCL infrastructure for multi-GPU password cracking with interactive setup.
    /// 
    /// <para><strong>OpenCL Infrastructure Setup:</strong></para>
    /// <para>Performs complete OpenCL stack initialization for each selected GPU:</para>
    /// <list type="number">
    /// <item><description>Platform Detection: Scans all available OpenCL platforms and devices</description></item>
    /// <item><description>GPU Discovery: Identifies compatible GPU devices and their capabilities</description></item>
    /// <item><description>User Selection: Interactive console interface for GPU and settings selection</description></item>
    /// <item><description>Context Creation: Establishes OpenCL contexts with IntPtr.Zero (default properties)</description></item>
    /// <item><description>Command Queue Setup: Creates queues for operation scheduling and execution</description></item>
    /// <item><description>Kernel Compilation: Loads and compiles SHA-256 brute force kernel from .cl file</description></item>
    /// </list>
    /// 
    /// <para><strong>Interactive Configuration:</strong></para>
    /// <para>Provides console-based interface for:
    /// - GPU selection (individual selection or 'all')
    /// - Batch size configuration per GPU (affects memory usage and performance)
    /// - Memory limit settings (percentage of GPU memory to utilize)</para>
    /// 
    /// <para><strong>Resource Management:</strong></para>
    /// <para>Creates isolated OpenCL resources for each GPU to enable true parallel execution.
    /// IntPtr.Zero parameters specify default context properties and no callback functions.
    /// Algorithm factory initialization enables strategy pattern for intelligent algorithm selection.</para>
    /// </summary>
    /// <exception cref="OpenClException">If OpenCL operations fail during initialization</exception>
    /// <exception cref="InvalidOperationException">If no compatible GPUs are found</exception>
    /// <exception cref="FileNotFoundException">If OpenCL kernel source file (.cl) cannot be located</exception>
    public void Initialize()
    { 
      List<GpuInfo> gpus = DetectGpus();
      ErrorCode error;
      DisplayGPUs(gpus);

      List<GpuInfo> selectedGpus = GetUserGpuSelection(gpus);
      GetUserSettings(selectedGpus);

      this.selectedGpus = selectedGpus;
      devices = selectedGpus.Select(g => g.Device).ToArray();
      contexts = new Context[devices.Length];
      commandQueues = new CommandQueue[devices.Length];
      programs = new Program[devices.Length];

      for (int i = 0; i < devices.Length; i++)
      {
        contexts[i] = Cl.CreateContext(null, 1, new[] { devices[i] }, null, IntPtr.Zero, out error);
        CheckError(error);

        commandQueues[i] = Cl.CreateCommandQueue(contexts[i], devices[i], (CommandQueueProperties)0, out error);
        CheckError(error);

        string programSource = File.ReadAllText(GetKernelFilePath());

        programs[i] = Cl.CreateProgramWithSource(contexts[i], 1, new[] { programSource }, null, out error);
        CheckError(error);

        error = Cl.BuildProgram(programs[i], 1, new[] { devices[i] }, string.Empty, null, IntPtr.Zero);
        CheckError(error);
      }
      
      // Initialize internal algorithm factory for strategy pattern
      internalAlgorithmFactory = new InternalAlgorithmFactory(this);
      
      // PerformanceTracker will be initialized when cracking begins
    }

    /// <summary>
    /// Initializes OpenCL infrastructure for the specified GPUs (used by GUI).
    /// 
    /// <para><strong>GPU Infrastructure Setup:</strong></para>
    /// <para>For each selected GPU, this method creates the complete OpenCL stack:</para>
    /// <list type="bullet">
    /// <item><description>Context creation with IntPtr.Zero for default properties</description></item>
    /// <item><description>Command queue creation for operation scheduling</description></item>
    /// <item><description>Program compilation from .cl source with build error handling</description></item>
    /// <item><description>Algorithm factory initialization for strategy selection</description></item>
    /// </list>
    /// 
    /// <para><strong>IntPtr.Zero Usage:</strong></para>
    /// <para>Used in context and program creation to specify default properties and</para>
    /// <para>null notification callbacks, which is the standard pattern for simple OpenCL setup.</para>
    /// </summary>
    /// <param name="selectedGpuList">Pre-configured list of GPUs to initialize</param>
    /// <exception cref="ArgumentException">If the GPU list is null or empty</exception>
    /// <exception cref="OpenClException">If any GPU fails to initialize properly</exception>
    public void InitializeWithSelectedGpus(List<GpuInfo> selectedGpuList)
    {
        if (selectedGpuList == null || selectedGpuList.Count == 0)
            throw new ArgumentException(KernelConstants.ErrorMessages.NO_GPUS_SELECTED);

        this.selectedGpus = selectedGpuList;
        devices = selectedGpuList.Select(gpu => gpu.Device).ToArray();
        contexts = new Context[devices.Length];
        commandQueues = new CommandQueue[devices.Length];
        programs = new Program[devices.Length];

        for (int gpuIndex = 0; gpuIndex < devices.Length; gpuIndex++)
        {
            // Create OpenCL context for this GPU
            // IntPtr.Zero = use default context properties, no notification callback
            contexts[gpuIndex] = Cl.CreateContext(null, 1, new[] { devices[gpuIndex] }, null, IntPtr.Zero, out ErrorCode contextError);
            CheckError(contextError);

            // Create command queue for operation scheduling
            commandQueues[gpuIndex] = Cl.CreateCommandQueue(contexts[gpuIndex], devices[gpuIndex], (CommandQueueProperties)0, out ErrorCode queueError);
            CheckError(queueError);

            // Load and compile OpenCL kernel source code
            string kernelSourceCode = File.ReadAllText(GetKernelFilePath());
            programs[gpuIndex] = Cl.CreateProgramWithSource(contexts[gpuIndex], 1, new[] { kernelSourceCode }, null, out ErrorCode programError);
            CheckError(programError);

            // Build (compile) the program for this specific GPU
            // IntPtr.Zero = no build notification callback needed
            ErrorCode buildError = Cl.BuildProgram(programs[gpuIndex], 1, new[] { devices[gpuIndex] }, string.Empty, null, IntPtr.Zero);
            CheckError(buildError);
        }
        
        // Initialize internal algorithm factory for strategy pattern
        internalAlgorithmFactory = new InternalAlgorithmFactory(this);
        
        // PerformanceTracker will be initialized when cracking begins
    }

    /// <summary>
    /// Detects available OpenCL GPUs using the centralized detection utility.
    /// 
    /// <para><strong>Centralized Detection:</strong></para>
    /// <para>Uses OpenClGpuDetector for consistent GPU discovery across console and GUI applications.
    /// Provides detailed error reporting suitable for console interaction and debugging.</para>
    /// </summary>
    /// <returns>List of detected GPUs without default settings (allows user configuration)</returns>
    private List<GpuInfo> DetectGpus()
    {
      return OpenClGpuDetector.DetectGpusForConsole();
    }


    private List<GpuInfo> GetUserGpuSelection(List<GpuInfo> availableGpus)
    {
      List<GpuInfo> selectedGpus = new List<GpuInfo>();

      while (true)
      {
        Console.WriteLine("Enter GPU numbers to use (1,2,3) or 'all': ");
        string userInput = Console.ReadLine();

        // Handle null, empty, or whitespace input
        if (string.IsNullOrWhiteSpace(userInput))
        {
          Console.WriteLine("Please enter a valid selection. You can type 'all' or specific GPU numbers like '1,2'.");
          continue;
        }

        // Normalize input
        userInput = userInput.Trim().ToLower();

        if (userInput == "all")
        {
          return availableGpus;
        }

        string[] gpuNumbers = userInput.Split(',');
        bool hasValidSelection = false;

        foreach (string num in gpuNumbers)
        {
          string trimmedNum = num.Trim();
          if (string.IsNullOrWhiteSpace(trimmedNum))
          {
            Console.WriteLine("Skipping empty entry");
            continue;
          }

          if (int.TryParse(trimmedNum, out int gpuNumber))
          {
            if (gpuNumber >= 1 && gpuNumber <= availableGpus.Count)
            {
              int index = gpuNumber - 1;
              
              // Avoid adding duplicate GPUs
              if (!selectedGpus.Any(g => g.Device.Equals(availableGpus[index].Device)))
              {
                selectedGpus.Add(availableGpus[index]);
                hasValidSelection = true;
              }
            }
            else
            {
              Console.WriteLine($"Invalid GPU number: {gpuNumber}. Valid range: 1-{availableGpus.Count}");
            }
          }
          else
          {
            Console.WriteLine($"Invalid input: '{trimmedNum}'. Please enter numbers only.");
          }
        }

        if (hasValidSelection && selectedGpus.Count > 0)
        {
          Console.WriteLine($"Selected {selectedGpus.Count} GPU(s): {string.Join(", ", selectedGpus.Select(g => g.Name))}");
          return selectedGpus;
        }
        else
        {
          Console.WriteLine("No valid GPUs selected. Please try again.");
          selectedGpus.Clear(); // Reset for next attempt
        }
      }
    }
    private void DisplayGPUs(List<GpuInfo> gpus)
    {
           Console.WriteLine("Detected GPUs:");
      for (int i = 0; i < gpus.Count; i++)
      {
        GpuInfo gpu = gpus[i];
        Console.WriteLine($"{i + 1}. {gpu.Name}");
        Console.WriteLine($"   Memory: {gpu.MemoryBytes / KernelConstants.BYTES_PER_GB} GB");
        Console.WriteLine($"   Compute Units: {gpu.ComputeUnits}");
        Console.WriteLine();
      }
    }


    /// <summary>
    /// Main entry point for GPU password cracking using Strategy pattern
    /// </summary>
    /// <summary>
    /// Performs intelligent multi-GPU brute force password cracking with automatic algorithm selection.
    /// 
    /// <para><strong>OpenCL Concepts:</strong></para>
    /// <para>This method orchestrates parallel password generation across multiple OpenCL devices (GPUs).
    /// Each GPU gets its own OpenCL context, command queue, and compiled kernel for independent operation.</para>
    /// 
    /// <para><strong>Algorithm Intelligence:</strong></para>
    /// <para>Automatically selects between Simple Distribution (homogeneous GPUs) and Dynamic Load Balancing 
    /// (heterogeneous GPUs) based on performance variance analysis and GPU count optimization.</para>
    /// 
    /// <para><strong>Search Space Distribution:</strong></para>
    /// <para>The total search space (charset^maxLength combinations) is intelligently divided among GPUs.
    /// Work distribution considers individual GPU performance capabilities and memory constraints.</para>
    /// 
    /// <para><strong>Thread Safety:</strong></para>
    /// <para>Uses centralized CrackingState for thread-safe coordination between GPU worker threads.
    /// Password discovery is atomic - first GPU to find the password wins and signals others to stop.</para>
    /// </summary>
    /// <param name="charset">Character set for password generation (e.g., "abcdefghijklmnopqrstuvwxyz0123456789")</param>
    /// <param name="maxLength">Maximum password length to attempt (impacts total search space exponentially)</param>
    /// <param name="targetHash">SHA-256 hash of the target password to crack</param>
    /// <param name="foundPassword">Output parameter containing the discovered password, or empty if not found</param>
    /// <returns>True if password was successfully cracked, false if search completed without finding password</returns>
    /// <exception cref="InvalidOperationException">If kernel not initialized or no GPUs selected</exception>
    /// <exception cref="ArgumentException">If charset is empty, maxLength exceeds limits, or targetHash is invalid</exception>
    /// <exception cref="OpenClException">If OpenCL operations fail during execution</exception>
    public bool CrackWithGPUGeneration(string charset, int maxLength, string targetHash, out string foundPassword)
    {
        if (internalAlgorithmFactory == null)
            throw new InvalidOperationException("Algorithm factory not initialized. Call Initialize() first.");

        if (selectedGpus == null || selectedGpus.Count == 0)
            throw new InvalidOperationException("No GPUs selected. Call Initialize() first.");

        // Use strategy pattern for algorithm selection
        var (algorithm, reason) = internalAlgorithmFactory.GetRecommendationWithReason(selectedGpus);
        
        Console.WriteLine($"\nAlgorithm Selection: {algorithm.Name}");
        Console.WriteLine($"Reason: {reason}");
        Console.WriteLine($"Description: {algorithm.Description}\n");

        // Execute the selected algorithm
        return algorithm.ExecuteCracking(charset, maxLength, targetHash, selectedGpus, out foundPassword);
    }

    /// <summary>
    /// GUI-friendly password cracking with Strategy pattern and progress callbacks
    /// </summary>
    /// <summary>
    /// GUI-optimized multi-GPU password cracking with real-time progress updates and algorithm selection.
    /// 
    /// <para><strong>GUI Integration:</strong></para>
    /// <para>Provides high-frequency progress callbacks optimized for UI responsiveness (500ms intervals).
    /// Progress includes completion percentage, speed metrics, and algorithm-specific status messages.</para>
    /// 
    /// <para><strong>Algorithm Selection:</strong></para>
    /// <para>Supports manual algorithm selection or intelligent auto-selection:
    /// - AUTO_SELECT: Analyzes GPU performance variance and selects optimal algorithm
    /// - SIMPLE: Forces equal work distribution (best for similar GPUs)
    /// - DYNAMIC: Forces adaptive load balancing (best for mixed GPU setups)</para>
    /// 
    /// <para><strong>OpenCL Resource Management:</strong></para>
    /// <para>Each GPU operates with isolated OpenCL resources: Context → Command Queue → Kernel.
    /// Resource cleanup is automatic through RAII pattern with GpuResourceManager.</para>
    /// </summary>
    /// <param name="charset">Character set for brute force generation</param>
    /// <param name="maxLength">Maximum password length (exponentially impacts search space)</param>
    /// <param name="targetHash">Target SHA-256 hash to match</param>
    /// <param name="foundPassword">Discovered password output (empty if not found)</param>
    /// <param name="progressCallback">Optional callback for progress updates (progress: 0.0-1.0, status: string)</param>
    /// <param name="algorithmChoice">Algorithm selection: AUTO_SELECT, SIMPLE, or DYNAMIC</param>
    /// <returns>True if password cracked successfully, false otherwise</returns>
    /// <exception cref="ArgumentException">If parameters are invalid or algorithm choice unrecognized</exception>
    /// <exception cref="OpenClException">If GPU operations fail during execution</exception>
    public bool CrackWithGPUGenerationGui(string charset, int maxLength, string targetHash, 
                                         out string foundPassword, Action<double, string> progressCallback = null, string algorithmChoice = KernelConstants.ALGORITHM_AUTO_SELECT)
    {
        return CrackWithGPUGenerationGui(charset, maxLength, targetHash, out foundPassword, out _, progressCallback, algorithmChoice);
    }

    /// <summary>
    /// Enhanced GUI password cracking with algorithm reporting and comprehensive progress tracking.
    /// 
    /// <para><strong>Algorithm Transparency:</strong></para>
    /// <para>Returns the name of the executed algorithm for UI display and user feedback.
    /// Helps users understand which strategy was selected and why performance varies.</para>
    /// 
    /// <para><strong>Performance Monitoring:</strong></para>
    /// <para>Integrates PerformanceTracker for detailed metrics collection across all GPUs.
    /// Tracks individual GPU speeds, load balancing effectiveness, and estimated completion times.</para>
    /// 
    /// <para><strong>OpenCL Kernel Execution:</strong></para>
    /// <para>Each GPU runs the SHA-256 brute force kernel in parallel work groups.
    /// Kernel arguments include: charset, target hash, work offset, and found flag buffer.</para>
    /// </summary>
    /// <param name="charset">Character set for password generation</param>
    /// <param name="maxLength">Maximum password length to attempt</param>
    /// <param name="targetHash">SHA-256 hash target for comparison</param>
    /// <param name="foundPassword">Output password if successfully cracked</param>
    /// <param name="executedAlgorithmName">Output name of the algorithm that was executed</param>
    /// <param name="progressCallback">Progress update callback (progress, status message)</param>
    /// <param name="algorithmChoice">Algorithm selection preference</param>
    /// <returns>True if password found, false if search space exhausted</returns>
    /// <exception cref="ArgumentException">If input parameters are invalid</exception>
    /// <exception cref="OpenClException">If OpenCL kernel execution fails</exception>
    public bool CrackWithGPUGenerationGui(string charset, int maxLength, string targetHash, 
                                         out string foundPassword, out string executedAlgorithmName, Action<double, string> progressCallback = null, string algorithmChoice = KernelConstants.ALGORITHM_AUTO_SELECT)
    {
        if (internalAlgorithmFactory == null)
            throw new InvalidOperationException("Algorithm factory not initialized. Call Initialize() first.");

        if (selectedGpus == null || selectedGpus.Count == 0)
            throw new InvalidOperationException("No GPUs selected. Call Initialize() first.");

        // Determine algorithm based on choice
        CrackingConfig.AlgorithmType algorithm;
        if (algorithmChoice == KernelConstants.ALGORITHM_AUTO_SELECT)
        {
            // Auto-select based on GPU performance variance
            var selectedAlgorithm = internalAlgorithmFactory.SelectBestAlgorithm(selectedGpus);
            algorithm = selectedAlgorithm.Name.Contains("Dynamic") ? CrackingConfig.AlgorithmType.Dynamic : CrackingConfig.AlgorithmType.Simple;
            executedAlgorithmName = selectedAlgorithm.Name;
        }
        else if (algorithmChoice == KernelConstants.ALGORITHM_SIMPLE)
        {
            algorithm = CrackingConfig.AlgorithmType.Simple;
            executedAlgorithmName = "Simple Distribution";
        }
        else if (algorithmChoice == KernelConstants.ALGORITHM_DYNAMIC)
        {
            algorithm = CrackingConfig.AlgorithmType.Dynamic;
            executedAlgorithmName = "Dynamic Distribution";
        }
        else
        {
            // Fallback to auto-select for unknown choices
            var selectedAlgorithm = internalAlgorithmFactory.SelectBestAlgorithm(selectedGpus);
            algorithm = selectedAlgorithm.Name.Contains("Dynamic") ? CrackingConfig.AlgorithmType.Dynamic : CrackingConfig.AlgorithmType.Simple;
            executedAlgorithmName = selectedAlgorithm.Name;
        }

        var config = CrackingConfig.Create(algorithm, CrackingConfig.ExecutionMode.GUI, progressCallback, algorithmChoice);
        return CrackWithGPUGenerationUnified(charset, maxLength, targetHash, out foundPassword, config);
    }



    /// <summary>
    /// Gets user choice between simple and dynamic multi-GPU algorithms (only for multiple GPUs)
    /// </summary>
    private bool GetUserAlgorithmChoice()
    {
      Console.WriteLine($"\n=== Multi-GPU Algorithm Selection ({selectedGpus.Count} GPUs) ===");
      Console.WriteLine("1. Simple Multi-GPU (Faster, equal work distribution)");
      Console.WriteLine("2. Dynamic Load Balancing (Adaptive, per-GPU monitoring)");
      Console.WriteLine("\nRecommendation: Use Simple for similar GPUs, Dynamic for different GPU performance.");
      
      while (true)
      {
        Console.Write("Choose algorithm (1 or 2): ");
        string userInput = Console.ReadLine();
        
        if (string.IsNullOrWhiteSpace(userInput))
        {
          Console.WriteLine("Please enter 1 or 2.");
          continue;
        }
        
        userInput = userInput.Trim();
        
        if (userInput == "1")
        {
          Console.WriteLine("Selected: Simple Multi-GPU");
          return false;
        }
        else if (userInput == "2")
        {
          Console.WriteLine("Selected: Dynamic Load Balancing");
          return true;
        }
        else
        {
          Console.WriteLine("Invalid choice. Please enter 1 for Simple or 2 for Dynamic.");
        }
      }
    }

    /// <summary>
    /// Unified GPU password cracking implementation that handles all algorithm types and execution modes.
    /// Replaces the previous separate implementations for Simple/Dynamic x Console/GUI combinations.
    /// 
    /// <para><strong>Algorithm Types:</strong></para>
    /// <list type="bullet">
    /// <item><description><strong>Simple:</strong> Equal work distribution across all GPUs</description></item>
    /// <item><description><strong>Dynamic:</strong> Performance-based load balancing with real-time adjustment</description></item>
    /// </list>
    /// 
    /// <para><strong>Execution Modes:</strong></para>
    /// <list type="bullet">
    /// <item><description><strong>Console:</strong> Text-based progress bars and console output</description></item>
    /// <item><description><strong>GUI:</strong> Progress callbacks for real-time UI updates</description></item>
    /// </list>
    /// 
    /// <para><strong>Unified Architecture:</strong></para>
    /// <para>Uses the existing ProcessGpuWorkUnified method for actual GPU work coordination,
    /// eliminating code duplication while preserving all functionality and performance optimizations.</para>
    /// </summary>
    /// <param name="charset">Character set for password generation</param>
    /// <param name="maxLength">Maximum password length to attempt</param>
    /// <param name="targetHash">Target SHA-256 hash to crack</param>
    /// <param name="foundPassword">Output parameter for the discovered password</param>
    /// <param name="config">Configuration specifying algorithm type, execution mode, and callbacks</param>
    /// <returns>True if password found, false if search completed without success</returns>
    private bool CrackWithGPUGenerationUnified(string charset, int maxLength, string targetHash, 
                                               out string foundPassword, CrackingConfig config)
    {
        foundPassword = string.Empty;
        DateTime startTime = DateTime.Now;
        
        // Calculate total search space
        ulong totalCombinations = CalculateTotalCombinations(charset, maxLength);

        // Display algorithm and GPU information
        string algorithmName = config.IsDynamicMode ? "Dynamic Load Balancing" : "Simple Distribution";
        if (!config.IsGuiMode)
        {
            Console.WriteLine($"Using {selectedGpus.Count} GPU(s) for parallel processing ({algorithmName}):");
            for (int i = 0; i < selectedGpus.Count; i++)
            {
                Console.WriteLine($"  GPU {i + 1}: {selectedGpus[i].Name} (Batch: {selectedGpus[i].BatchSize:N0}, Memory: {selectedGpus[i].MemoryLimitPercent}%)");
            }
            Console.WriteLine($"Total combinations to check: {totalCombinations:N0}");
            Console.WriteLine($"Starting {algorithmName.ToLower()}...\n");
        }

        // Create centralized state manager
        using var crackingState = new CrackingState(totalCombinations, startTime, selectedGpus.Count);
        currentCrackingState = crackingState;

        try
        {
            if (config.IsDynamicMode)
            {
                return ExecuteDynamicAlgorithm(charset, maxLength, targetHash, out foundPassword, 
                                             totalCombinations, startTime, crackingState, config);
            }
            else
            {
                return ExecuteSimpleAlgorithm(charset, maxLength, targetHash, out foundPassword, 
                                            totalCombinations, startTime, crackingState, config);
            }
        }
        finally
        {
            currentCrackingState = null;
        }
    }

    /// <summary>
    /// Executes the simple algorithm with equal work distribution.
    /// </summary>
    private bool ExecuteSimpleAlgorithm(string charset, int maxLength, string targetHash, out string foundPassword,
                                       ulong totalCombinations, DateTime startTime, CrackingState crackingState, CrackingConfig config)
    {
        foundPassword = string.Empty;

        // Calculate equal work distribution
        var (workPerGpu, remainder) = CalculateEqualWorkDistribution(totalCombinations, selectedGpus.Count);

        // Create tasks for each GPU
        Task<bool>[] gpuTasks = new Task<bool>[selectedGpus.Count];

        for (int gpuIndex = 0; gpuIndex < selectedGpus.Count; gpuIndex++)
        {
            int currentGpuIndex = gpuIndex;
            ulong startOffset = (ulong)currentGpuIndex * workPerGpu;
            ulong endOffset = startOffset + workPerGpu;
            
            // Last GPU gets the remainder
            if (currentGpuIndex == selectedGpus.Count - 1)
                endOffset += remainder;

            // Update work assignment in centralized state
            crackingState.UpdateGpuWorkAssignment(currentGpuIndex, endOffset - startOffset, endOffset - startOffset);

            gpuTasks[currentGpuIndex] = Task.Run(() =>
            {
                var gpuConfig = config.IsGuiMode 
                    ? GpuProcessingConfig.SimpleGui(endOffset, config.ProgressCallback!)
                    : GpuProcessingConfig.SimpleConsole(endOffset);
                    
                return ProcessGpuWorkUnified(currentGpuIndex, charset, maxLength, targetHash, 
                                           startOffset, startTime, crackingState, gpuConfig);
            });
        }

        return WaitForCompletion(gpuTasks, totalCombinations, startTime, crackingState, config, out foundPassword);
    }

    /// <summary>
    /// Executes the dynamic algorithm with performance-based load balancing.
    /// </summary>
    private bool ExecuteDynamicAlgorithm(string charset, int maxLength, string targetHash, out string foundPassword,
                                        ulong totalCombinations, DateTime startTime, CrackingState crackingState, CrackingConfig config)
    {
        foundPassword = string.Empty;

        // Initialize performance tracking
        performanceTracker = new PerformanceTracker(selectedGpus, totalCombinations, startTime);
        
        // Set initial work assignments for dynamic distribution
        for (int i = 0; i < selectedGpus.Count; i++)
        {
            selectedGpus[i].LastUpdateTime = DateTime.Now;
            selectedGpus[i].WorkRemaining = totalCombinations / (ulong)selectedGpus.Count;
            selectedGpus[i].WorkAssigned = selectedGpus[i].WorkRemaining;
        }

        // Give remainder work to last GPU
        ulong remainder = totalCombinations % (ulong)selectedGpus.Count;
        selectedGpus[selectedGpus.Count - 1].WorkRemaining += remainder;
        selectedGpus[selectedGpus.Count - 1].WorkAssigned += remainder;

        ulong globalOffset = 0;
        Task<bool>[] gpuTasks = new Task<bool>[selectedGpus.Count];

        for (int gpuIndex = 0; gpuIndex < selectedGpus.Count; gpuIndex++)
        {
            int currentGpuIndex = gpuIndex;
            ulong currentGlobalOffset = globalOffset;
            globalOffset += selectedGpus[currentGpuIndex].WorkAssigned;

            gpuTasks[currentGpuIndex] = Task.Run(() => 
            {
                var gpuConfig = config.IsGuiMode 
                    ? GpuProcessingConfig.DynamicGui(config.ProgressCallback!)
                    : GpuProcessingConfig.DynamicConsole();
                    
                return ProcessGpuWorkUnified(currentGpuIndex, charset, maxLength, targetHash, 
                                           currentGlobalOffset, startTime, crackingState, gpuConfig);
            });
        }

        return WaitForCompletionDynamic(gpuTasks, totalCombinations, startTime, crackingState, config, out foundPassword);
    }

    /// <summary>
    /// Waits for simple algorithm completion with appropriate progress display.
    /// </summary>
    private bool WaitForCompletion(Task<bool>[] gpuTasks, ulong totalCombinations, DateTime startTime, 
                                  CrackingState crackingState, CrackingConfig config, out string foundPassword)
    {
        foundPassword = string.Empty;

        // Initial progress update
        if (config.IsGuiMode)
        {
            config.ProgressCallback?.Invoke(0.0, "Starting search...");
        }
        else
        {
            DisplayProgressBar(0.0, 0, totalCombinations, startTime);
        }

        // Wait for completion with progress updates
        while (!Task.WaitAll(gpuTasks, KernelConstants.PROGRESS_UPDATE_INTERVAL_MS))
        {
            if (!crackingState.IsPasswordFound())
            {
                var snapshot = crackingState.CreateSnapshot();
                if (config.IsGuiMode)
                {
                    if (snapshot.OverallProgress > 0.0)
                    {
                        config.ProgressCallback?.Invoke(snapshot.OverallProgress / 100.0, 
                                                       $"Searching... {snapshot.OverallProgress:F1}% complete");
                    }
                }
                else
                {
                    double progressPercent = snapshot.OverallProgress;
                    DisplayProgressBar(progressPercent, snapshot.TotalProcessed, totalCombinations, startTime);
                }
            }
        }

        Task.WaitAll(gpuTasks);
        return HandleFinalResults(totalCombinations, startTime, crackingState, config, out foundPassword);
    }

    /// <summary>
    /// Waits for dynamic algorithm completion with load balancing updates.
    /// </summary>
    private bool WaitForCompletionDynamic(Task<bool>[] gpuTasks, ulong totalCombinations, DateTime startTime, 
                                         CrackingState crackingState, CrackingConfig config, out string foundPassword)
    {
        foundPassword = string.Empty;

        // Initial progress update
        if (config.IsGuiMode)
        {
            config.ProgressCallback?.Invoke(0.0, "Starting dynamic load balancing search...");
        }
        else
        {
            DisplayMultiGpuProgressBar(totalCombinations, startTime);
        }

        // Wait for completion with real-time progress updates and dynamic load balancing
        while (!crackingState.IsPasswordFound() && !Task.WaitAll(gpuTasks, KernelConstants.PROGRESS_UPDATE_INTERVAL_MS))
        {
            if (!crackingState.IsPasswordFound())
            {
                if (config.IsGuiMode)
                {
                    // Update performance metrics
                    performanceTracker.UpdatePerformanceRatios();
                    
                    // Get current state snapshot
                    var snapshot = crackingState.CreateSnapshot();
                    double actualProgress = (double)snapshot.TotalProcessed / totalCombinations;
                    
                    if (actualProgress > 0.0)
                    {
                        // Show dynamic load balancing info
                        var topGpu = selectedGpus.OrderByDescending(g => g.CombinationsPerSecond).FirstOrDefault();
                        string balanceInfo = topGpu != null ? $"Lead GPU: {topGpu.Name}" : "Balancing...";
                        config.ProgressCallback?.Invoke(actualProgress, $"Dynamic balancing... {actualProgress:P1} complete | {balanceInfo}");
                    }
                }
                else
                {
                    // Update performance ratios and redistribute work
                    performanceTracker.UpdatePerformanceRatios();
                    DisplayMultiGpuProgressBar(totalCombinations, startTime);
                }
            }
        }

        Task.WaitAll(gpuTasks);
        return HandleFinalResultsDynamic(totalCombinations, startTime, crackingState, config, out foundPassword);
    }

    /// <summary>
    /// Handles final results for simple algorithm.
    /// </summary>
    private bool HandleFinalResults(ulong totalCombinations, DateTime startTime, CrackingState crackingState, 
                                   CrackingConfig config, out string foundPassword)
    {
        var finalSnapshot = crackingState.CreateSnapshot();
        foundPassword = finalSnapshot.PasswordFound ? finalSnapshot.FoundPassword : string.Empty;

        if (finalSnapshot.PasswordFound)
        {
            if (config.IsGuiMode)
            {
                config.ProgressCallback?.Invoke(finalSnapshot.OverallProgress / 100.0, $"Password found: {foundPassword}");
            }
            else
            {
                double finalProgress = finalSnapshot.OverallProgress;
                DisplayProgressBar(finalProgress, finalSnapshot.TotalProcessed, totalCombinations, startTime);
                Console.WriteLine();
                Console.WriteLine($"✓ Password found at {finalProgress:F1}% progress!");
            }
            return true;
        }
        else
        {
            if (config.IsGuiMode)
            {
                config.ProgressCallback?.Invoke(1.0, "Search completed - password not found");
            }
            else
            {
                DisplayProgressBar(100.0, totalCombinations, totalCombinations, startTime);
                Console.WriteLine();
                Console.WriteLine("✗ Search completed - password not found.");
            }
            return false;
        }
    }

    /// <summary>
    /// Handles final results for dynamic algorithm.
    /// </summary>
    private bool HandleFinalResultsDynamic(ulong totalCombinations, DateTime startTime, CrackingState crackingState, 
                                          CrackingConfig config, out string foundPassword)
    {
        var finalSnapshot = crackingState.CreateSnapshot();
        foundPassword = finalSnapshot.PasswordFound ? finalSnapshot.FoundPassword : string.Empty;

        if (finalSnapshot.PasswordFound)
        {
            if (config.IsGuiMode)
            {
                double finalProgress = (double)finalSnapshot.TotalProcessed / totalCombinations;
                config.ProgressCallback?.Invoke(finalProgress, $"Password found with dynamic balancing: {foundPassword}");
            }
            else
            {
                ulong finalProcessed = (ulong)selectedGpus.Sum(g => (long)g.TotalProcessed);
                double finalProgress = (double)finalProcessed / totalCombinations * 100;
                DisplayFinalResults(finalProgress, finalProcessed, totalCombinations, startTime);
                Console.WriteLine($"✓ Password found at {finalProgress:F1}% progress!");
            }
            return true;
        }
        else
        {
            if (config.IsGuiMode)
            {
                config.ProgressCallback?.Invoke(1.0, "Dynamic search completed - password not found");
            }
            else
            {
                DisplayFinalResults(100.0, totalCombinations, totalCombinations, startTime);
                Console.WriteLine("✗ Search completed - password not found.");
            }
            return false;
        }
    }

    /// <summary>
    /// Simple multi-GPU brute force with equal work distribution across all selected GPUs.
    /// 
    /// <para><strong>Work Distribution Strategy:</strong></para>
    /// <para>Divides the total search space (charset^1 + charset^2 + ... + charset^maxLength) 
    /// equally among all GPUs. Each GPU processes a contiguous range of password combinations.</para>
    /// 
    /// <para><strong>OpenCL Parallel Execution:</strong></para>
    /// <para>Each GPU runs identical OpenCL kernels with different offset parameters.
    /// Global work size is determined by GPU batch size and memory constraints.
    /// Work groups execute SHA-256 hashing in parallel across GPU compute units.</para>
    /// 
    /// <para><strong>Performance Characteristics:</strong></para>
    /// <para>Optimal for homogeneous GPU setups where all devices have similar performance.
    /// Lower coordination overhead compared to dynamic load balancing.
    /// May lead to work imbalance if GPUs have significantly different capabilities.</para>
    /// 
    /// <para><strong>Thread Synchronization:</strong></para>
    /// <para>Uses CrackingState for centralized coordination. Password discovery is atomic -
    /// first GPU to find the password signals all others to terminate gracefully.</para>
    /// </summary>
    /// <param name="charset">Character set for brute force attack (e.g., "abcdefghijklmnopqrstuvwxyz")</param>
    /// <param name="maxLength">Maximum password length (each increment exponentially increases search space)</param>
    /// <param name="targetHash">SHA-256 hash of target password in hexadecimal format</param>
    /// <param name="foundPassword">Output parameter containing discovered password or empty string</param>
    /// <returns>True if password successfully cracked, false if search completed without success</returns>
    /// <exception cref="InvalidOperationException">If no GPUs are selected or initialized</exception>
    /// <exception cref="ArgumentException">If charset is empty or maxLength exceeds supported limits</exception>
    /// <exception cref="OpenClException">If OpenCL kernel execution or memory operations fail</exception>

    /// <summary>
    /// Updates performance metrics using PerformanceTracker
    /// </summary>
    private void UpdatePerformanceMetrics(DateTime startTime)
    {
      performanceTracker?.UpdatePerformanceRatios();
    }

    /// <summary>
    /// Displays multi-GPU progress using PerformanceTracker
    /// </summary>
    private void DisplayMultiGpuProgressBar(ulong totalCombinations, DateTime startTime)
    {
      if (performanceTracker == null) return;
      
      const int barWidth = 30;
      var snapshot = performanceTracker.GetCurrentSnapshot();

      // Clear previous output
      Console.Write("\r" + new string(' ', 120));
      Console.Write("\r");

      // Overall progress bar using PerformanceTracker's progress bar creation
      string progressBar = PerformanceTracker.CreateProgressBar(snapshot.OverallProgress, barWidth);
      TimeSpan elapsed = DateTime.Now - startTime;
      
      Console.WriteLine($"Overall: {progressBar} {snapshot.OverallProgress:F1}% | {PerformanceTracker.FormatSpeed(snapshot.TotalSpeed)} | {elapsed.TotalSeconds:F1}s");

      // Per-GPU progress using PerformanceTracker data
      for (int i = 0; i < snapshot.GpuMetrics.Count; i++)
      {
        var gpuData = snapshot.GpuMetrics[i];
        string gpuBar = PerformanceTracker.CreateProgressBar(gpuData.Progress, KernelConstants.PROGRESS_BAR_WIDTH_SMALL);
        
        Console.WriteLine($"GPU {i + 1}: {gpuBar} {gpuData.Progress:F1}% | {PerformanceTracker.FormatSpeed(gpuData.Speed)} | {gpuData.PerformanceRatio * 100:F0}%");
      }

      // Move cursor back up to overwrite on next update
      Console.SetCursorPosition(0, Console.CursorTop - (selectedGpus.Count + 1));
    }

    /// <summary>
    /// Displays final results using PerformanceTracker
    /// </summary>
    private void DisplayFinalResults(double progressPercent, ulong processed, ulong total, DateTime startTime)
    {
      if (performanceTracker == null) return;
      
      // Clear progress bars
      for (int i = 0; i <= selectedGpus.Count; i++)
      {
        Console.WriteLine(new string(' ', 120));
      }
      Console.SetCursorPosition(0, Console.CursorTop - (selectedGpus.Count + 1));

      // Show final statistics using PerformanceTracker
      var snapshot = performanceTracker.GetCurrentSnapshot();
      TimeSpan elapsed = DateTime.Now - startTime;
      
      Console.WriteLine("=== Final Results ===");
      for (int i = 0; i < snapshot.GpuMetrics.Count; i++)
      {
        var gpuData = snapshot.GpuMetrics[i];
        Console.WriteLine($"GPU {i + 1} ({selectedGpus[i].Name}): {PerformanceTracker.FormatLargeNumber(gpuData.CombinationsProcessed)} combinations | {PerformanceTracker.FormatSpeed(gpuData.Speed)}");
      }
      Console.WriteLine($"Total: {PerformanceTracker.FormatLargeNumber(processed)}/{PerformanceTracker.FormatLargeNumber(total)} combinations | {PerformanceTracker.FormatSpeed(snapshot.TotalSpeed)} | {elapsed.TotalSeconds:F1}s");
    }

    /// <summary>
    /// Displays a clean progress bar that updates in place (for simple algorithm)
    /// </summary>
    private void DisplayProgressBar(double progressPercent, ulong processed, ulong total, DateTime startTime)
    {
      const int barWidth = 50;
      int filledWidth = (int)(progressPercent / 100.0 * barWidth);
      
      // Calculate speed and ETA
      TimeSpan elapsed = DateTime.Now - startTime;
      double combinationsPerSecond = elapsed.TotalSeconds > 0 ? processed / elapsed.TotalSeconds : 0;
      
      string speedText = "";
      if (combinationsPerSecond >= 1000000)
        speedText = $"{combinationsPerSecond / 1000000:F1}M/s";
      else if (combinationsPerSecond >= 1000)
        speedText = $"{combinationsPerSecond / 1000:F1}K/s";
      else
        speedText = $"{combinationsPerSecond:F0}/s";

      string etaText = "";
      if (combinationsPerSecond > 0 && progressPercent < 100)
      {
        double remainingCombinations = total - processed;
        double etaSeconds = remainingCombinations / combinationsPerSecond;
        TimeSpan eta = TimeSpan.FromSeconds(etaSeconds);
        
        if (eta.TotalHours >= 1)
          etaText = $"ETA: {eta.Hours:D2}:{eta.Minutes:D2}:{eta.Seconds:D2}";
        else
          etaText = $"ETA: {eta.Minutes:D2}:{eta.Seconds:D2}";
      }

      // Build progress bar
      string bar = "[" + new string('█', filledWidth) + new string('░', barWidth - filledWidth) + "]";
      
      // Clear the line and write progress
      Console.Write($"\r{bar} {progressPercent:F1}% ({processed:N0}/{total:N0}) {speedText} {etaText}");
    }

    /// <summary>
    /// Checks OpenCL errors and provides detailed error information
    /// </summary>
    private void CheckError(ErrorCode error)
    {
      if (error != ErrorCode.Success)
      {
        string errorMessage = string.Format(KernelConstants.ErrorMessages.OPENCL_ERROR, error);
        
        // Try to get build log for compilation errors, but don't fail if we can't
        if (programs != null && programs.Length > 0 && devices != null && devices.Length > 0)
        {
          try
          {
            InfoBuffer buildLog = Cl.GetProgramBuildInfo(programs[0], devices[0], ProgramBuildInfo.Log, out ErrorCode buildLogError);
            if (buildLogError == ErrorCode.Success)
            {
              string buildLogStr = buildLog.ToString();
              if (!string.IsNullOrWhiteSpace(buildLogStr))
              {
                Console.WriteLine($"OpenCL Build Log: {buildLogStr}");
                errorMessage += $"\nBuild Log: {buildLogStr}";
              }
            }
          }
          catch
          {
            // If we can't get build log, continue with basic error
          }
        }

        throw new OpenClException(error, errorMessage);
      }
    }

    private void GetUserSettings(List<GpuInfo> selectedGpus)
    {
      Console.WriteLine("\n=== GPU Settings Configuration ===");

      for (int i = 0; i < selectedGpus.Count; i++)
      {
        GpuInfo gpu = selectedGpus[i];
        Console.WriteLine($"\nConfiguring settings for GPU {i + 1}: {gpu.Name}");

        // Batch size input with retry
        while (true)
        {
          Console.Write($"Batch size for {gpu.Name} (default: {KernelConstants.DEFAULT_BATCH_SIZE:N0}): ");
          string batchInput = Console.ReadLine();
          
          // Handle null, empty, or whitespace input (use default)
          if (string.IsNullOrWhiteSpace(batchInput))
          {
            gpu.BatchSize = KernelConstants.DEFAULT_BATCH_SIZE;
            Console.WriteLine($"Using default batch size: {KernelConstants.DEFAULT_BATCH_SIZE:N0}");
            break;
          }

          if (ulong.TryParse(batchInput.Trim(), out ulong batchSize) && batchSize > 0)
          {
            // Add reasonable upper limit to prevent memory issues
            if (batchSize > KernelConstants.MAX_BATCH_SIZE)
            {
              Console.WriteLine(string.Format(KernelConstants.ErrorMessages.BATCH_SIZE_TOO_LARGE, KernelConstants.MAX_BATCH_SIZE));
              continue;
            }
            gpu.BatchSize = batchSize;
            Console.WriteLine($"Set batch size: {batchSize:N0}");
            break;
          }
          else
          {
            Console.WriteLine("Invalid input. Please enter a positive number or press Enter for default.");
          }
        }

        // Memory limit input with retry
        while (true)
        {
          Console.Write($"Memory limit % for {gpu.Name} (default: {KernelConstants.DEFAULT_MEMORY_LIMIT_PERCENT}%): ");
          string memoryInput = Console.ReadLine();
          
          // Handle null, empty, or whitespace input (use default)
          if (string.IsNullOrWhiteSpace(memoryInput))
          {
            gpu.MemoryLimitPercent = KernelConstants.DEFAULT_MEMORY_LIMIT_PERCENT;
            Console.WriteLine($"Using default memory limit: {KernelConstants.DEFAULT_MEMORY_LIMIT_PERCENT}%");
            break;
          }

          if (int.TryParse(memoryInput.Trim(), out int memoryPercent) && 
              memoryPercent >= KernelConstants.MIN_MEMORY_LIMIT_PERCENT && 
              memoryPercent <= KernelConstants.MAX_MEMORY_LIMIT_PERCENT)
          {
            gpu.MemoryLimitPercent = memoryPercent;
            Console.WriteLine($"Set memory limit: {memoryPercent}%");
            break;
          }
          else
          {
            Console.WriteLine(string.Format(KernelConstants.ErrorMessages.INVALID_MEMORY_LIMIT, 
              KernelConstants.MIN_MEMORY_LIMIT_PERCENT, KernelConstants.MAX_MEMORY_LIMIT_PERCENT));
          }
        }
        
        gpu.IsSelected = true;
      }
    }

    /// <summary>
    /// Retrieves the currently selected GPUs with their performance data and configuration settings.
    /// 
    /// <para><strong>GPU Information Provided:</strong></para>
    /// <para>Each GpuInfo contains OpenCL device details, performance metrics, and user configuration:
    /// - Device name, memory size, compute units
    /// - Batch size and memory limit settings
    /// - Real-time performance data (speed, processed combinations)
    /// - OpenCL platform and device references</para>
    /// 
    /// <para><strong>Usage Context:</strong></para>
    /// <para>Primary use case is GUI display of GPU status and performance monitoring.
    /// Performance data is updated in real-time during cracking operations.
    /// Configuration settings reflect user preferences from GPU settings dialog.</para>
    /// 
    /// <para><strong>Thread Safety:</strong></para>
    /// <para>Returns a reference to the internal GPU list. Performance data may be updated
    /// by worker threads during active cracking operations. Read-only access recommended
    /// from UI thread to avoid synchronization issues.</para>
    /// </summary>
    /// <returns>List of selected GPUs with current performance data and configuration</returns>
    /// <exception cref="InvalidOperationException">If no GPUs have been selected or initialized</exception>
    public List<GpuInfo> GetSelectedGpus()
    {
      return selectedGpus;
    }

    /// <summary>
    /// Requests cancellation of the current cracking operation.
    /// </summary>
    public void RequestCancellation()
    {
        currentCrackingState?.RequestCancellation();
    }

    #region Nested Strategy Classes

    /// <summary>
    /// Base class for password cracking algorithm strategies.
    /// Provides access to Kernel's private methods while maintaining encapsulation.
    /// </summary>
    private abstract class CrackingAlgorithmBase
    {
        protected readonly Kernel kernel;

        protected CrackingAlgorithmBase(Kernel kernel)
        {
            this.kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        }

        public abstract string Name { get; }
        public abstract string Description { get; }

        public virtual bool IsCompatible(List<GpuInfo> selectedGpus)
        {
            return selectedGpus != null && selectedGpus.Count > 0;
        }

        public abstract bool ExecuteCracking(string charset, int maxLength, string targetHash,
                                           List<GpuInfo> selectedGpus, out string foundPassword,
                                           Action<double, string> progressCallback = null);
    }

    /// <summary>
    /// Simple algorithm strategy that distributes work equally among all GPUs.
    /// Best for homogeneous GPU setups where all GPUs have similar performance.
    /// </summary>
    private class SimpleAlgorithmInternal : CrackingAlgorithmBase
    {
        public SimpleAlgorithmInternal(Kernel kernel) : base(kernel) { }

        public override string Name => "Simple Distribution";

        public override string Description => "Divides work equally among all GPUs. Best for similar GPUs with consistent performance.";

        public override bool ExecuteCracking(string charset, int maxLength, string targetHash,
                                           List<GpuInfo> selectedGpus, out string foundPassword,
                                           Action<double, string> progressCallback = null)
        {
            var mode = progressCallback != null ? CrackingConfig.ExecutionMode.GUI : CrackingConfig.ExecutionMode.Console;
            var config = CrackingConfig.Create(CrackingConfig.AlgorithmType.Simple, mode, progressCallback);
            return kernel.CrackWithGPUGenerationUnified(charset, maxLength, targetHash, out foundPassword, config);
        }
    }

    /// <summary>
    /// Dynamic algorithm strategy that distributes work based on individual GPU performance.
    /// Best for heterogeneous GPU setups with different performance characteristics.
    /// </summary>
    private class DynamicAlgorithmInternal : CrackingAlgorithmBase
    {
        public DynamicAlgorithmInternal(Kernel kernel) : base(kernel) { }

        public override string Name => "Dynamic Distribution";

        public override string Description => "Intelligently distributes work based on each GPU's performance. Best for mixed GPU setups with different capabilities.";

        public override bool ExecuteCracking(string charset, int maxLength, string targetHash,
                                           List<GpuInfo> selectedGpus, out string foundPassword,
                                           Action<double, string> progressCallback = null)
        {
            var mode = progressCallback != null ? CrackingConfig.ExecutionMode.GUI : CrackingConfig.ExecutionMode.Console;
            var config = CrackingConfig.Create(CrackingConfig.AlgorithmType.Dynamic, mode, progressCallback);
            return kernel.CrackWithGPUGenerationUnified(charset, maxLength, targetHash, out foundPassword, config);
        }
    }

    /// <summary>
    /// Internal factory for creating and managing nested algorithm strategies.
    /// Provides intelligent algorithm selection based on GPU configuration.
    /// </summary>
    private class InternalAlgorithmFactory
    {
        private readonly Kernel kernel;

        public InternalAlgorithmFactory(Kernel kernel)
        {
            this.kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        }

        /// <summary>
        /// Creates an algorithm instance by name
        /// </summary>
        public CrackingAlgorithmBase? CreateAlgorithm(string algorithmName)
        {
            if (string.IsNullOrWhiteSpace(algorithmName))
                return null;

            return algorithmName.ToLower() switch
            {
                "simple" or "simple distribution" or "simple multi-gpu" => new SimpleAlgorithmInternal(kernel),
                "dynamic" or "dynamic distribution" or "dynamic load balance" => new DynamicAlgorithmInternal(kernel),
                _ => null
            };
        }

        /// <summary>
        /// Automatically selects the best algorithm for the given GPU configuration
        /// </summary>
        public CrackingAlgorithmBase SelectBestAlgorithm(List<GpuInfo> selectedGpus)
        {
            if (selectedGpus == null || selectedGpus.Count == 0)
            {
                // Default to simple algorithm if no GPUs
                return new SimpleAlgorithmInternal(kernel);
            }

            if (selectedGpus.Count == 1)
            {
                // For single GPU, both algorithms work the same, prefer simple
                return new SimpleAlgorithmInternal(kernel);
            }

            // Analyze GPU performance characteristics
            var performanceVariance = CalculatePerformanceVariance(selectedGpus);

            // If GPUs have similar performance (low variance), use simple algorithm
            // If GPUs have different performance (high variance), use dynamic algorithm
            const double VARIANCE_THRESHOLD = 0.3; // 30% variance threshold

            if (performanceVariance < VARIANCE_THRESHOLD)
            {
                return new SimpleAlgorithmInternal(kernel);
            }
            else
            {
                return new DynamicAlgorithmInternal(kernel);
            }
        }

        /// <summary>
        /// Gets algorithm selection recommendation with explanation
        /// </summary>
        public (CrackingAlgorithmBase algorithm, string reason) GetRecommendationWithReason(List<GpuInfo> selectedGpus)
        {
            if (selectedGpus == null || selectedGpus.Count == 0)
            {
                return (new SimpleAlgorithmInternal(kernel), "No GPUs selected - using default simple algorithm");
            }

            if (selectedGpus.Count == 1)
            {
                return (new SimpleAlgorithmInternal(kernel), $"Single GPU detected ({selectedGpus[0].Name}) - simple algorithm is optimal");
            }

            var performanceVariance = CalculatePerformanceVariance(selectedGpus);
            const double VARIANCE_THRESHOLD = 0.3;

            if (performanceVariance < VARIANCE_THRESHOLD)
            {
                return (new SimpleAlgorithmInternal(kernel),
                       $"GPUs have similar performance (variance: {performanceVariance:P1}) - simple equal distribution is optimal");
            }
            else
            {
                return (new DynamicAlgorithmInternal(kernel),
                       $"GPUs have different performance levels (variance: {performanceVariance:P1}) - dynamic distribution will optimize efficiency");
            }
        }

        /// <summary>
        /// Calculates performance variance among GPUs to determine if they're similar or different
        /// </summary>
        private double CalculatePerformanceVariance(List<GpuInfo> gpus)
        {
            if (gpus.Count <= 1)
                return 0.0;

            // Use compute units as a proxy for performance
            var computeUnits = gpus.Select(g => (double)g.ComputeUnits).ToArray();

            if (computeUnits.All(cu => cu == computeUnits[0]))
                return 0.0; // All identical

            var mean = computeUnits.Average();
            if (mean == 0)
                return 0.0;

            var variance = computeUnits.Select(cu => Math.Pow((cu - mean) / mean, 2)).Average();
            return Math.Sqrt(variance); // Return coefficient of variation
        }
    }

    #endregion

    #region Combination Calculation Utilities

    /// <summary>
    /// Calculates the total number of password combinations for the given character set and maximum length.
    /// 
    /// <para><strong>Mathematical Formula:</strong></para>
    /// <para>Total = charset^1 + charset^2 + ... + charset^maxLength</para>
    /// <para>For each length L: combinations = charset.Length^L</para>
    /// 
    /// <para><strong>Performance Note:</strong></para>
    /// <para>This calculation is performed once and cached for the cracking session.
    /// The result grows exponentially with maxLength and charset size.</para>
    /// </summary>
    /// <param name="charset">Character set used for password generation</param>
    /// <param name="maxLength">Maximum password length to consider</param>
    /// <returns>Total number of combinations across all password lengths from 1 to maxLength</returns>
    /// <exception cref="ArgumentException">If charset is empty or maxLength is invalid</exception>
    /// <exception cref="OverflowException">If total combinations exceed ulong capacity</exception>
    private static ulong CalculateTotalCombinations(string charset, int maxLength)
    {
        if (string.IsNullOrEmpty(charset))
            throw new ArgumentException("Character set cannot be null or empty", nameof(charset));
        
        if (maxLength <= 0 || maxLength > KernelConstants.MAX_PASSWORD_LENGTH_SUPPORTED)
            throw new ArgumentException($"Max length must be between 1 and {KernelConstants.MAX_PASSWORD_LENGTH_SUPPORTED}", nameof(maxLength));

        ulong totalCombinations = 0;
        ulong charsetLength = (ulong)charset.Length;
        
        for (int length = 1; length <= maxLength; length++)
        {
            ulong combinationsForLength = 1;
            for (int i = 0; i < length; i++)
            {
                // Check for overflow before multiplication
                if (combinationsForLength > ulong.MaxValue / charsetLength)
                    throw new OverflowException($"Password combinations exceed maximum supported value for length {length}");
                    
                combinationsForLength *= charsetLength;
            }
            
            // Check for overflow before addition
            if (totalCombinations > ulong.MaxValue - combinationsForLength)
                throw new OverflowException("Total password combinations exceed maximum supported value");
                
            totalCombinations += combinationsForLength;
        }

        return totalCombinations;
    }

    #endregion

    #region Work Distribution Utilities

    /// <summary>
    /// Calculates work distribution for simple equal distribution algorithm.
    /// Divides total combinations equally among GPUs with remainder handling.
    /// </summary>
    /// <param name="totalCombinations">Total password combinations to distribute</param>
    /// <param name="gpuCount">Number of GPUs to distribute work among</param>
    /// <returns>Tuple containing (workPerGpu, remainder) for distribution</returns>
    private static (ulong workPerGpu, ulong remainder) CalculateEqualWorkDistribution(ulong totalCombinations, int gpuCount)
    {
        if (gpuCount <= 0)
            throw new ArgumentException("GPU count must be positive", nameof(gpuCount));
            
        ulong workPerGpu = totalCombinations / (ulong)gpuCount;
        ulong remainder = totalCombinations % (ulong)gpuCount;
        
        return (workPerGpu, remainder);
    }

    #endregion

    #region GPU Processing Methods

    /// <summary>
    /// Configuration for GPU processing operations to unify different processing modes
    /// </summary>
    private struct GpuProcessingConfig
    {
        public bool IsDynamicMode { get; init; }
        public bool IsGuiMode { get; init; }
        public ulong? EndOffset { get; init; } // null for dynamic mode
        public Action<double, string>? ProgressCallback { get; init; } // null for console mode
        
        public static GpuProcessingConfig SimpleConsole(ulong endOffset) 
            => new() { IsDynamicMode = false, IsGuiMode = false, EndOffset = endOffset };
            
        public static GpuProcessingConfig SimpleGui(ulong endOffset, Action<double, string> progressCallback) 
            => new() { IsDynamicMode = false, IsGuiMode = true, EndOffset = endOffset, ProgressCallback = progressCallback };
            
        public static GpuProcessingConfig DynamicConsole() 
            => new() { IsDynamicMode = true, IsGuiMode = false };
            
        public static GpuProcessingConfig DynamicGui(Action<double, string> progressCallback) 
            => new() { IsDynamicMode = true, IsGuiMode = true, ProgressCallback = progressCallback };
    }

    /// <summary>
    /// Unified configuration for all password cracking operations.
    /// Consolidates algorithm type, execution mode, and parameters into a single structure.
    /// </summary>
    private struct CrackingConfig
    {
        public AlgorithmType Algorithm { get; init; }
        public ExecutionMode Mode { get; init; }
        public Action<double, string>? ProgressCallback { get; init; }
        public string AlgorithmChoice { get; init; }

        public enum AlgorithmType
        {
            Simple,    // Equal work distribution
            Dynamic    // Performance-based load balancing
        }

        public enum ExecutionMode
        {
            Console,   // Console output with progress bars
            GUI        // GUI with progress callbacks
        }

        public static CrackingConfig Create(AlgorithmType algorithm, ExecutionMode mode, 
                                          Action<double, string>? progressCallback = null, 
                                          string algorithmChoice = KernelConstants.ALGORITHM_AUTO_SELECT)
        {
            return new CrackingConfig
            {
                Algorithm = algorithm,
                Mode = mode,
                ProgressCallback = progressCallback,
                AlgorithmChoice = algorithmChoice
            };
        }

        public bool IsGuiMode => Mode == ExecutionMode.GUI;
        public bool IsDynamicMode => Algorithm == AlgorithmType.Dynamic;
    }

    /// <summary>
    /// Unified GPU processing method that handles all processing modes through configuration.
    /// Replaces the multiple specialized ProcessGpuWork methods with a single, configurable implementation.
    /// 
    /// <para><strong>Processing Modes:</strong></para>
    /// <list type="bullet">
    /// <item><description>Simple Console: Equal work distribution for console output</description></item>
    /// <item><description>Simple GUI: Equal work distribution with progress callbacks</description></item>
    /// <item><description>Dynamic Console: Load balancing for console output</description></item>
    /// <item><description>Dynamic GUI: Load balancing with progress callbacks</description></item>
    /// </list>
    /// 
    /// <para><strong>State Management:</strong></para>
    /// <para>Uses centralized CrackingState for thread-safe coordination across all GPUs.
    /// Automatically handles performance tracking, progress updates, and password discovery.</para>
    /// </summary>
    /// <param name="gpuIndex">Index of the GPU performing work</param>
    /// <param name="charset">Character set for password generation</param>
    /// <param name="maxLength">Maximum password length to attempt</param>
    /// <param name="targetHash">Target SHA-256 hash to match</param>
    /// <param name="startOffset">Starting offset in the global search space</param>
    /// <param name="startTime">Time when cracking operation began</param>
    /// <param name="crackingState">Centralized state manager for coordination</param>
    /// <param name="config">Configuration specifying the processing mode and parameters</param>
    /// <returns>True if password found by this GPU, false otherwise</returns>
    private bool ProcessGpuWorkUnified(int gpuIndex, string charset, int maxLength, string targetHash, 
                                     ulong startOffset, DateTime startTime, CrackingState crackingState, 
                                     GpuProcessingConfig config)
    {
        // Use resource manager for automatic cleanup and simplified resource management
        using var resourceManager = new GpuResourceManager(gpuIndex, contexts[gpuIndex], commandQueues[gpuIndex], programs[gpuIndex]);
        
        try
        {
            GpuInfo gpu = selectedGpus[gpuIndex];
            
            // Calculate work range based on mode
            ulong totalWork, workRemaining;
            if (config.IsDynamicMode)
            {
                totalWork = gpu.WorkAssigned;
                workRemaining = gpu.WorkAssigned;
            }
            else
            {
                if (!config.EndOffset.HasValue)
                    throw new ArgumentException("EndOffset required for Simple mode", nameof(config));
                totalWork = config.EndOffset.Value - startOffset;
                workRemaining = totalWork;
            }
            
            // Initialize all OpenCL resources at once
            resourceManager.InitializeResources(charset, targetHash, totalWork);
            
            // Process this GPU's work range with performance tracking
            ulong processedCombinations = 0;
            DateTime lastPerformanceUpdate = DateTime.Now;
            double updateInterval = KernelConstants.PERFORMANCE_UPDATE_INTERVAL_SECONDS;

            while (processedCombinations < workRemaining)
            {
                // Check if another GPU found the password
                if (crackingState.IsPasswordFound()) break;
                
                // Check if cancellation was requested
                if (crackingState.IsCancellationRequested()) break;

                ulong currentBatchSize = Math.Min(gpu.BatchSize, workRemaining - processedCombinations);
                
                // Update batch arguments and reset found flag
                resourceManager.UpdateBatchArguments(maxLength, 1, startOffset + processedCombinations);
                resourceManager.ResetFoundFlag();

                // Execute kernel
                resourceManager.ExecuteKernel(currentBatchSize);

                // Check if password was found
                if (resourceManager.CheckPasswordFound())
                {
                    string foundPasswordStr = resourceManager.GetFoundPassword();
                    
                    if (crackingState.TrySetFoundPassword(foundPasswordStr, gpuIndex))
                    {
                        // This GPU was first to find the password
                        // Use REAL data from GPU kernel - exact found index
                        ulong foundIndex = resourceManager.GetFoundIndex();
                        ulong exactProcessed = processedCombinations + foundIndex + 1; // +1 because index is 0-based
                        
                        // Use exact data for both performance tracking AND progress display
                        UpdateGpuPerformanceOnPasswordFound(gpuIndex, exactProcessed, startTime, config);
                        
                        // Update centralized state for GUI modes with exact progress
                        if (config.IsGuiMode)
                        {
                            crackingState.UpdateGpuProcessed(gpuIndex, exactProcessed);
                        }
                    }

                    return true;
                }

                processedCombinations += currentBatchSize;
                
                // Update performance metrics periodically
                DateTime now = DateTime.Now;
                if ((now - lastPerformanceUpdate).TotalSeconds >= updateInterval)
                {
                    UpdateGpuPerformanceMetrics(gpuIndex, processedCombinations, workRemaining, startTime, config, crackingState);
                    lastPerformanceUpdate = now;
                }
            }

            // Final performance update
            UpdateFinalGpuPerformance(gpuIndex, processedCombinations, startTime, config, crackingState);

            return false;
        }
        catch (Exception ex)
        {
            HandleGpuProcessingError(gpuIndex, ex, crackingState);
            return false;
        }
    }

    /// <summary>
    /// Updates GPU performance metrics when password is found
    /// </summary>
    private void UpdateGpuPerformanceOnPasswordFound(int gpuIndex, ulong processedCombinations, DateTime startTime, GpuProcessingConfig config)
    {
        if (!config.IsDynamicMode) return; // Simple mode doesn't need this complex update
        
        GpuInfo gpu = selectedGpus[gpuIndex];
        DateTime passwordFoundTime = DateTime.Now;
        
        // Update performance for the GPU that found the password
        gpu.TotalProcessed = processedCombinations;
        TimeSpan elapsed = passwordFoundTime - startTime;
        if (elapsed.TotalSeconds > 0)
        {
            gpu.CombinationsPerSecond = gpu.TotalProcessed / elapsed.TotalSeconds;
        }
        
        // Update performance for all OTHER GPUs based on their actual work done
        for (int i = 0; i < selectedGpus.Count; i++)
        {
            if (i != gpuIndex) // Don't update the GPU that found the password again
            {
                var otherGpu = selectedGpus[i];
                // Use their current processed amount (actual work done when password found)
                if (elapsed.TotalSeconds > 0 && otherGpu.TotalProcessed > 0)
                {
                    otherGpu.CombinationsPerSecond = otherGpu.TotalProcessed / elapsed.TotalSeconds;
                }
            }
        }
    }

    /// <summary>
    /// Updates GPU performance metrics during processing
    /// </summary>
    private void UpdateGpuPerformanceMetrics(int gpuIndex, ulong processedCombinations, ulong workRemaining, 
                                           DateTime startTime, GpuProcessingConfig config, CrackingState crackingState)
    {
        GpuInfo gpu = selectedGpus[gpuIndex];
        DateTime now = DateTime.Now;
        TimeSpan elapsed = now - startTime;
        
        if (config.IsDynamicMode)
        {
            // Dynamic mode: update GPU object directly
            gpu.TotalProcessed = processedCombinations;
            gpu.WorkRemaining = workRemaining - processedCombinations;
            
            if (elapsed.TotalSeconds > 0)
            {
                gpu.CombinationsPerSecond = processedCombinations / elapsed.TotalSeconds;
            }
            
            // Update centralized state for thread safety
            crackingState.UpdateGpuProgress(gpuIndex, processedCombinations, gpu.CombinationsPerSecond);
        }
        else
        {
            // Simple mode: use centralized state only
            double currentSpeed = elapsed.TotalSeconds > 0 ? processedCombinations / elapsed.TotalSeconds : 0.0;
            crackingState.UpdateGpuProgress(gpuIndex, processedCombinations, currentSpeed);
        }
    }

    /// <summary>
    /// Performs final GPU performance update when processing completes
    /// </summary>
    private void UpdateFinalGpuPerformance(int gpuIndex, ulong processedCombinations, DateTime startTime, 
                                         GpuProcessingConfig config, CrackingState crackingState)
    {
        DateTime now = DateTime.Now;
        TimeSpan finalElapsed = now - startTime;
        double finalSpeed = finalElapsed.TotalSeconds > 0 ? processedCombinations / finalElapsed.TotalSeconds : 0.0;
        
        if (config.IsDynamicMode)
        {
            // Dynamic mode: update GPU object
            GpuInfo gpu = selectedGpus[gpuIndex];
            gpu.TotalProcessed = processedCombinations;
            gpu.WorkRemaining = 0;
            gpu.CombinationsPerSecond = finalSpeed;
            
            // Also update centralized state
            crackingState.UpdateGpuProgress(gpuIndex, processedCombinations, finalSpeed);
        }
        else
        {
            // Simple mode: centralized state only
            crackingState.UpdateGpuProgress(gpuIndex, processedCombinations, finalSpeed);
        }
    }



    /// <summary>
    /// Handles errors during GPU processing
    /// </summary>
    private void HandleGpuProcessingError(int gpuIndex, Exception ex, CrackingState crackingState)
    {
        string gpuName = gpuIndex < selectedGpus.Count ? selectedGpus[gpuIndex].Name : $"GPU {gpuIndex + 1}";
        string errorMessage = string.Format(KernelConstants.ErrorMessages.GPU_OPERATION_FAILED, gpuName, ex.Message);
        Console.WriteLine($"\n{errorMessage}");
        
        // Mark GPU as inactive in state
        crackingState.DeactivateGpu(gpuIndex);
    }

    #endregion
  }
}

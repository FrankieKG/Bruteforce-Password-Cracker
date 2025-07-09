using OpenCL.Net;
using System;
using System.Text;

namespace PasswordCracking
{
    /// <summary>
    /// Manages OpenCL resources for GPU password cracking operations with automatic cleanup.
    /// 
    /// <para><strong>OpenCL Resource Management:</strong></para>
    /// <para>This class encapsulates the complex OpenCL resource lifecycle, including:</para>
    /// <list type="bullet">
    /// <item><description>GPU memory buffers for data transfer between CPU and GPU</description></item>
    /// <item><description>Kernel compilation and argument binding</description></item>
    /// <item><description>Command queue operations for asynchronous GPU execution</description></item>
    /// <item><description>Automatic resource cleanup to prevent memory leaks</description></item>
    /// </list>
    /// 
    /// <para><strong>IntPtr Usage in OpenCL:</strong></para>
    /// <para>IntPtr.Zero is frequently used in OpenCL operations to represent:</para>
    /// <list type="bullet">
    /// <item><description>NULL pointers when no offset is needed (buffer operations start at beginning)</description></item>
    /// <item><description>Null event lists when not tracking completion events</description></item>
    /// <item><description>Default work group sizes (let OpenCL decide optimal size)</description></item>
    /// </list>
    /// </summary>
    public class GpuResourceManager : IDisposable
    {
        private readonly int gpuIndex;
        private readonly Context context;
        private readonly CommandQueue commandQueue;
        private readonly Program program;
        private bool disposed = false;

        // OpenCL Resources - will be automatically cleaned up
        /// <summary>
        /// The compiled OpenCL kernel for password generation and hashing.
        /// Contains the GPU code that generates password candidates and computes SHA-256 hashes.
        /// </summary>
        public OpenCL.Net.Kernel Kernel { get; private set; }
        
        /// <summary>
        /// GPU memory buffer containing the character set used for password generation.
        /// Read-only buffer that stores the allowed characters (e.g., "abcdefghijklmnopqrstuvwxyz").
        /// </summary>
        public IMem CharsetBuffer { get; private set; }
        
        /// <summary>
        /// GPU memory buffer containing the target hash to crack.
        /// Read-only buffer storing the SHA-256 hash we're trying to match.
        /// </summary>
        public IMem TargetHashBuffer { get; private set; }
        
        /// <summary>
        /// GPU memory buffer for the found flag (0 = not found, 1 = found).
        /// Read-write buffer that allows the GPU to signal when it finds a matching password.
        /// </summary>
        public IMem FoundFlagBuffer { get; private set; }
        
        /// <summary>
        /// GPU memory buffer to store the found password when discovered.
        /// Write-only buffer where the GPU writes the matching password string.
        /// Size: KernelConstants.PASSWORD_BUFFER_SIZE bytes.
        /// </summary>
        public IMem FoundPasswordBuffer { get; private set; }
        
        /// <summary>
        /// GPU memory buffer containing the total number of combinations to check.
        /// Read-only buffer used for progress calculation and work distribution.
        /// </summary>
        public IMem TotalCombinationsBuffer { get; private set; }
        
        /// <summary>
        /// GPU memory buffer containing the batch offset for this work unit.
        /// Read-only buffer that tells the GPU which combination to start from.
        /// Updated for each batch to ensure unique work distribution across GPUs.
        /// </summary>
        public IMem BatchOffsetBuffer { get; private set; }
        
        /// <summary>
        /// GPU memory buffer to store the exact global work item index where password was found.
        /// Write-only buffer that stores the precise thread ID that discovered the matching password.
        /// Used for accurate progress calculation when password is found mid-batch.
        /// </summary>
        public IMem FoundIndexBuffer { get; private set; }

        // Data arrays for buffer operations
        /// <summary>
        /// CPU-side byte array containing the character set.
        /// Used to transfer charset data to GPU memory via CharsetBuffer.
        /// </summary>
        public byte[] CharsetBytes { get; private set; }
        
        /// <summary>
        /// CPU-side byte array containing the target hash.
        /// Used to transfer hash data to GPU memory via TargetHashBuffer.
        /// </summary>
        public byte[] TargetHashBytes { get; private set; }
        
        /// <summary>
        /// CPU-side array for the found flag (single integer).
        /// Used to read back the found status from GPU memory.
        /// </summary>
        public int[] FoundFlag { get; private set; }
        
        /// <summary>
        /// CPU-side array containing total combinations count.
        /// Used to transfer combination count to GPU memory.
        /// </summary>
        public ulong[] TotalCombinationsArray { get; private set; }
        
        /// <summary>
        /// CPU-side array containing the current batch offset.
        /// Updated for each batch and transferred to GPU memory.
        /// </summary>
        public ulong[] BatchOffsetArray { get; private set; }
        
        /// <summary>
        /// CPU-side array to receive the exact found index from GPU memory.
        /// Used to read back the precise work item ID where password was discovered.
        /// </summary>
        public ulong[] FoundIndexArray { get; private set; }

        /// <summary>
        /// Creates a new GPU resource manager for the specified GPU.
        /// 
        /// <para><strong>OpenCL Context Hierarchy:</strong></para>
        /// <para>Context → CommandQueue → Program → Kernel → Buffers</para>
        /// <para>This constructor receives the higher-level OpenCL objects and will create the dependent resources.</para>
        /// </summary>
        /// <param name="gpuIndex">Index of the GPU to manage resources for (used for error reporting)</param>
        /// <param name="context">OpenCL context representing the GPU device and its memory space</param>
        /// <param name="commandQueue">OpenCL command queue for scheduling operations on this GPU</param>
        /// <param name="program">OpenCL program containing the compiled kernel code</param>
        public GpuResourceManager(int gpuIndex, Context context, CommandQueue commandQueue, Program program)
        {
            this.gpuIndex = gpuIndex;
            this.context = context;
            this.commandQueue = commandQueue;
            this.program = program;

            // Initialize data arrays
            FoundFlag = new int[] { 0 };
            TotalCombinationsArray = new ulong[] { 0 };
            BatchOffsetArray = new ulong[] { 0 };
            FoundIndexArray = new ulong[] { 0 };
        }

        /// <summary>
        /// Initializes all OpenCL resources for password cracking.
        /// 
        /// <para><strong>Resource Initialization Order:</strong></para>
        /// <list type="number">
        /// <item><description>Convert input data to byte arrays (CPU side)</description></item>
        /// <item><description>Create OpenCL kernel from compiled program</description></item>
        /// <item><description>Allocate GPU memory buffers</description></item>
        /// <item><description>Transfer initial data to GPU memory</description></item>
        /// <item><description>Bind buffers to kernel arguments</description></item>
        /// </list>
        /// 
        /// <para><strong>Error Handling:</strong></para>
        /// <para>If any step fails, all partially created resources are cleaned up automatically.</para>
        /// </summary>
        /// <param name="charset">Character set for password generation (e.g., "abcdefghijklmnopqrstuvwxyz0123456789")</param>
        /// <param name="targetHash">Target SHA-256 hash to crack (hex string)</param>
        /// <param name="totalCombinations">Total number of password combinations to process across all GPUs</param>
        /// <exception cref="ObjectDisposedException">If this instance has been disposed</exception>
        /// <exception cref="OpenClException">If any OpenCL operation fails during initialization</exception>
        public void InitializeResources(string charset, string targetHash, ulong totalCombinations)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GpuResourceManager));

            try
            {
                // Prepare data arrays
                CharsetBytes = Encoding.UTF8.GetBytes(charset);
                TargetHashBytes = Encoding.UTF8.GetBytes(targetHash);
                TotalCombinationsArray[0] = totalCombinations;

                // Create kernel
                Kernel = Cl.CreateKernel(program, KernelConstants.KERNEL_FUNCTION_NAME, out ErrorCode error);
                CheckError(error, "creating kernel");

                // Create buffers
                CharsetBuffer = Cl.CreateBuffer(context, MemFlags.ReadOnly, CharsetBytes.Length, out error);
                CheckError(error, "creating charset buffer");

                TargetHashBuffer = Cl.CreateBuffer(context, MemFlags.ReadOnly, TargetHashBytes.Length, out error);
                CheckError(error, "creating target hash buffer");

                FoundFlagBuffer = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(int), out error);
                CheckError(error, "creating found flag buffer");

                FoundPasswordBuffer = Cl.CreateBuffer(context, MemFlags.WriteOnly, KernelConstants.PASSWORD_BUFFER_SIZE, out error);
                CheckError(error, "creating found password buffer");

                TotalCombinationsBuffer = Cl.CreateBuffer(context, MemFlags.ReadOnly, sizeof(ulong), out error);
                CheckError(error, "creating total combinations buffer");

                BatchOffsetBuffer = Cl.CreateBuffer(context, MemFlags.ReadOnly, sizeof(ulong), out error);
                CheckError(error, "creating batch offset buffer");
                
                FoundIndexBuffer = Cl.CreateBuffer(context, MemFlags.WriteOnly, sizeof(ulong), out error);
                CheckError(error, "creating found index buffer");

                // Initialize static buffers with initial data
                WriteInitialBufferData();

                // Set kernel arguments
                SetKernelArguments(charset.Length);
            }
            catch (Exception ex)
            {
                // Clean up any partially created resources
                Dispose();
                throw new OpenClException(ErrorCode.OutOfResources, 
                    $"Failed to initialize GPU {gpuIndex} resources: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Writes initial data to static buffers (charset, target hash, total combinations, found flag).
        /// 
        /// <para><strong>IntPtr.Zero Usage:</strong></para>
        /// <para>IntPtr.Zero is used as the buffer offset parameter, meaning we write data starting</para>
        /// <para>from the beginning (offset 0) of each GPU memory buffer. This is the most common</para>
        /// <para>pattern when transferring complete data arrays to GPU memory.</para>
        /// 
        /// <para><strong>Synchronous Operations:</strong></para>
        /// <para>Bool.True makes these operations blocking - the CPU waits until the GPU memory</para>
        /// <para>transfer completes before continuing. This ensures data is ready before kernel execution.</para>
        /// </summary>
        private void WriteInitialBufferData()
        {
            // Write charset data
            // IntPtr.Zero = start from beginning of buffer, no offset needed
            Cl.EnqueueWriteBuffer(commandQueue, CharsetBuffer, Bool.True, IntPtr.Zero, 
                CharsetBytes.Length, CharsetBytes, 0, null, out Event _);

            // Write target hash data
            // IntPtr.Zero = start from beginning of buffer, no offset needed
            Cl.EnqueueWriteBuffer(commandQueue, TargetHashBuffer, Bool.True, IntPtr.Zero, 
                TargetHashBytes.Length, TargetHashBytes, 0, null, out Event _);

            // Write total combinations
            // IntPtr.Zero = start from beginning of buffer, no offset needed
            Cl.EnqueueWriteBuffer(commandQueue, TotalCombinationsBuffer, Bool.True, IntPtr.Zero, 
                sizeof(ulong), TotalCombinationsArray, 0, null, out Event _);

            // Initialize found flag to 0
            // IntPtr.Zero = start from beginning of buffer, no offset needed
            Cl.EnqueueWriteBuffer(commandQueue, FoundFlagBuffer, Bool.True, IntPtr.Zero, 
                sizeof(int), FoundFlag, 0, null, out Event _);
        }

        /// <summary>
        /// Sets all kernel arguments for password cracking.
        /// 
        /// <para><strong>Kernel Arguments Explained:</strong></para>
        /// <list type="bullet">
        /// <item><description>Arguments 0-1: Character set and its length</description></item>
        /// <item><description>Arguments 2-3: Password length bounds (set later per batch)</description></item>
        /// <item><description>Argument 4: Target hash to match</description></item>
        /// <item><description>Arguments 5-6: Output buffers (found flag and password)</description></item>
        /// <item><description>Arguments 7-8: Work distribution parameters</description></item>
        /// </list>
        /// </summary>
        /// <param name="charsetLength">Length of the character set (number of allowed characters)</param>
        private void SetKernelArguments(int charsetLength)
        {
            Cl.SetKernelArg(Kernel, KernelConstants.KERNEL_ARG_CHARSET, CharsetBuffer);
            Cl.SetKernelArg(Kernel, KernelConstants.KERNEL_ARG_CHARSET_LENGTH, (uint)charsetLength);
            Cl.SetKernelArg(Kernel, KernelConstants.KERNEL_ARG_TARGET_HASH, TargetHashBuffer);
            Cl.SetKernelArg(Kernel, KernelConstants.KERNEL_ARG_FOUND_FLAG, FoundFlagBuffer);
            Cl.SetKernelArg(Kernel, KernelConstants.KERNEL_ARG_FOUND_PASSWORD, FoundPasswordBuffer);
            Cl.SetKernelArg(Kernel, KernelConstants.KERNEL_ARG_TOTAL_COMBINATIONS, TotalCombinationsBuffer);
            Cl.SetKernelArg(Kernel, KernelConstants.KERNEL_ARG_BATCH_OFFSET, BatchOffsetBuffer);
            Cl.SetKernelArg(Kernel, KernelConstants.KERNEL_ARG_FOUND_INDEX, FoundIndexBuffer);
        }

        /// <summary>
        /// Updates kernel arguments that change between batches (max length, start length, batch offset).
        /// 
        /// <para><strong>Dynamic Work Distribution:</strong></para>
        /// <para>Each batch processes a different range of password combinations. The batch offset</para>
        /// <para>tells the GPU where to start in the overall search space, enabling work distribution</para>
        /// <para>across multiple GPUs without overlap.</para>
        /// 
        /// <para><strong>Performance Optimization:</strong></para>
        /// <para>Only updates the arguments that change per batch rather than rebinding all arguments.</para>
        /// </summary>
        /// <param name="maxLength">Maximum password length to try in this batch</param>
        /// <param name="startLength">Starting password length (typically 1)</param>
        /// <param name="batchOffset">Global offset for this batch to ensure unique work distribution</param>
        /// <exception cref="ObjectDisposedException">If this instance has been disposed</exception>
        public void UpdateBatchArguments(int maxLength, int startLength, ulong batchOffset)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GpuResourceManager));

            // Update kernel arguments that change per batch
            Cl.SetKernelArg(Kernel, KernelConstants.KERNEL_ARG_MAX_LENGTH, (uint)maxLength);
            Cl.SetKernelArg(Kernel, KernelConstants.KERNEL_ARG_START_LENGTH, (uint)startLength);

            // Update batch offset buffer
            // IntPtr.Zero = write to beginning of buffer (single ulong value)
            BatchOffsetArray[0] = batchOffset;
            Cl.EnqueueWriteBuffer(commandQueue, BatchOffsetBuffer, Bool.True, IntPtr.Zero, 
                sizeof(ulong), BatchOffsetArray, 0, null, out Event _);
        }

        /// <summary>
        /// Resets the found flag and found index for a new batch.
        /// 
        /// <para><strong>Inter-batch Communication:</strong></para>
        /// <para>The found flag allows the GPU to signal the CPU when a password is found.</para>
        /// <para>The found index stores the exact work item where the password was discovered.</para>
        /// <para>Both must be reset to 0 before each batch to ensure clean state.</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">If this instance has been disposed</exception>
        public void ResetFoundFlag()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GpuResourceManager));

            FoundFlag[0] = 0;
            FoundIndexArray[0] = 0;
            
            // Reset found flag buffer
            // IntPtr.Zero = write to beginning of buffer (single int value)
            Cl.EnqueueWriteBuffer(commandQueue, FoundFlagBuffer, Bool.True, IntPtr.Zero, 
                sizeof(int), FoundFlag, 0, null, out Event _);
                
            // Reset found index buffer
            // IntPtr.Zero = write to beginning of buffer (single ulong value)
            Cl.EnqueueWriteBuffer(commandQueue, FoundIndexBuffer, Bool.True, IntPtr.Zero, 
                sizeof(ulong), FoundIndexArray, 0, null, out Event _);
        }

        /// <summary>
        /// Executes the kernel for the specified number of work items.
        /// 
        /// <para><strong>OpenCL Work Distribution:</strong></para>
        /// <para>Each work item (GPU thread) processes one password combination. The globalWorkSize</para>
        /// <para>determines how many GPU threads run in parallel. OpenCL automatically distributes</para>
        /// <para>these across the GPU's compute units.</para>
        /// 
        /// <para><strong>IntPtr Usage in Kernel Execution:</strong></para>
        /// <list type="bullet">
        /// <item><description>globalWorkSize cast to IntPtr: Total number of work items to execute</description></item>
        /// <item><description>null for global offset: Start from work item 0 (no offset)</description></item>
        /// <item><description>null for local work size: Let OpenCL choose optimal work group size</description></item>
        /// </list>
        /// </summary>
        /// <param name="globalWorkSize">Number of password combinations to process in parallel</param>
        /// <exception cref="ObjectDisposedException">If this instance has been disposed</exception>
        public void ExecuteKernel(ulong globalWorkSize)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GpuResourceManager));

            // Execute kernel with specified work size
            // IntPtr.Zero equivalent parameters: null = let OpenCL decide optimal values
            // (IntPtr)globalWorkSize = number of work items to execute
            Cl.EnqueueNDRangeKernel(commandQueue, Kernel, 1, null, new[] { (IntPtr)globalWorkSize }, 
                null, 0, null, out Event _);
        }

        /// <summary>
        /// Checks if a password was found in the current batch.
        /// 
        /// <para><strong>GPU-CPU Communication:</strong></para>
        /// <para>Reads the found flag from GPU memory back to CPU memory. The GPU sets this</para>
        /// <para>flag to 1 when it finds a matching password, allowing the CPU to detect success.</para>
        /// </summary>
        /// <returns>True if password was found (flag = 1), false otherwise (flag = 0)</returns>
        /// <exception cref="ObjectDisposedException">If this instance has been disposed</exception>
        public bool CheckPasswordFound()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GpuResourceManager));

            // Read found flag from GPU memory
            // IntPtr.Zero = read from beginning of buffer (single int value)
            Cl.EnqueueReadBuffer(commandQueue, FoundFlagBuffer, Bool.True, IntPtr.Zero, 
                sizeof(int), FoundFlag, 0, null, out Event _);
            
            return FoundFlag[0] != 0;
        }

        /// <summary>
        /// Retrieves the found password from GPU memory.
        /// 
        /// <para><strong>Memory Layout:</strong></para>
        /// <para>The GPU stores the found password as a null-terminated C-style string in the</para>
        /// <para>FoundPasswordBuffer. This method reads the entire buffer and converts it back</para>
        /// <para>to a managed .NET string by finding the null terminator.</para>
        /// 
        /// <para><strong>Character Encoding:</strong></para>
        /// <para>Uses UTF-8 encoding to handle international characters in passwords properly.</para>
        /// </summary>
        /// <returns>The found password as a managed string, or empty if no password found</returns>
        /// <exception cref="ObjectDisposedException">If this instance has been disposed</exception>
        public string GetFoundPassword()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GpuResourceManager));

            byte[] foundPasswordBytes = new byte[KernelConstants.PASSWORD_BUFFER_SIZE];
            // Read entire password buffer from GPU memory
            // IntPtr.Zero = read from beginning of buffer
            Cl.EnqueueReadBuffer(commandQueue, FoundPasswordBuffer, Bool.True, IntPtr.Zero, 
                KernelConstants.PASSWORD_BUFFER_SIZE, foundPasswordBytes, 0, null, out Event _);

            // Find null terminator (GPU uses C-style strings)
            int passwordLength = 0;
            for (int i = 0; i < foundPasswordBytes.Length; i++)
            {
                if (foundPasswordBytes[i] == 0) break;
                passwordLength++;
            }

            return Encoding.UTF8.GetString(foundPasswordBytes, 0, passwordLength);
        }

        /// <summary>
        /// Retrieves the exact global work item index where the password was found.
        /// 
        /// <para><strong>Precise Progress Tracking:</strong></para>
        /// <para>This provides the exact GPU thread ID (global work item index) that discovered</para>
        /// <para>the matching password. This enables accurate progress calculation when passwords</para>
        /// <para>are found mid-batch, eliminating the need for progress estimation.</para>
        /// </summary>
        /// <returns>The exact global work item index where password was found (0-based within current batch)</returns>
        /// <exception cref="ObjectDisposedException">If this instance has been disposed</exception>
        public ulong GetFoundIndex()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GpuResourceManager));

            // Read found index from GPU memory
            // IntPtr.Zero = read from beginning of buffer (single ulong value)
            Cl.EnqueueReadBuffer(commandQueue, FoundIndexBuffer, Bool.True, IntPtr.Zero, 
                sizeof(ulong), FoundIndexArray, 0, null, out Event _);
            
            return FoundIndexArray[0];
        }

        /// <summary>
        /// Checks OpenCL error codes and throws appropriate exceptions with context.
        /// 
        /// <para><strong>Error Handling Strategy:</strong></para>
        /// <para>Converts OpenCL error codes into meaningful .NET exceptions with operation context.</para>
        /// <para>This provides better debugging information when GPU operations fail.</para>
        /// </summary>
        /// <param name="errorCode">The OpenCL error code to check</param>
        /// <param name="operationDescription">Description of the operation that failed (for error context)</param>
        /// <exception cref="OpenClException">If the error code indicates a failure</exception>
        private void CheckError(ErrorCode errorCode, string operationDescription)
        {
            if (errorCode != ErrorCode.Success)
            {
                string errorMessage = string.Format(KernelConstants.ErrorMessages.GPU_OPERATION_FAILED, 
                    gpuIndex, $"{operationDescription}: {errorCode}");
                throw new OpenClException(errorCode, errorMessage);
            }
        }

        /// <summary>
        /// Disposes of all OpenCL resources to prevent memory leaks.
        /// 
        /// <para><strong>Resource Cleanup Order:</strong></para>
        /// <list type="number">
        /// <item><description>Mark as disposed to prevent further operations</description></item>
        /// <item><description>Release GPU memory buffers</description></item>
        /// <item><description>Release OpenCL kernel</description></item>
        /// </list>
        /// 
        /// <para><strong>Safe Disposal:</strong></para>
        /// <para>Uses try-catch blocks for each resource to ensure partial cleanup doesn't</para>
        /// <para>prevent other resources from being cleaned up. Multiple calls are safe.</para>
        /// 
        /// <para><strong>Finalizer Note:</strong></para>
        /// <para>OpenCL resources are unmanaged and won't be cleaned up by the GC automatically.</para>
        /// <para>Always call Dispose() or use 'using' statements to ensure proper cleanup.</para>
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;

            try
            {
                // Release GPU memory buffers
                CharsetBuffer?.Dispose();
                TargetHashBuffer?.Dispose();
                FoundFlagBuffer?.Dispose();
                FoundPasswordBuffer?.Dispose();
                TotalCombinationsBuffer?.Dispose();
                BatchOffsetBuffer?.Dispose();
                FoundIndexBuffer?.Dispose();

                // Release OpenCL kernel (if it was successfully created)
                try
                {
                    Cl.ReleaseKernel(Kernel);
                }
                catch
                {
                    // Ignore errors if kernel was never properly initialized
                }
            }
            catch (Exception ex)
            {
                // Log cleanup errors but don't throw to avoid masking original exceptions
                Console.WriteLine(string.Format(KernelConstants.ErrorMessages.RESOURCE_CLEANUP_FAILED, ex.Message));
            }
            finally
            {
                disposed = true;
            }
        }
    }
} 
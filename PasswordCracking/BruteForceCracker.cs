using System.Text;

namespace PasswordCracking
{
  /// <summary>
  /// GPU-accelerated brute force password cracker with multiple optimization strategies
  /// </summary>
  class BruteForceCracker
  {
    private Kernel kernel;
    private string characterSet;
    private int maxLength;

    public BruteForceCracker(string characterSet, int maxLength)
    {
      this.characterSet = characterSet;
      this.maxLength = maxLength;
      kernel = new Kernel();
      kernel.Initialize();
    }


    /// <summary>
    /// Ultimate GPU optimization: generates combinations directly on GPU, eliminating CPU-GPU data transfer bottleneck
    /// </summary>
    public void CrackPasswordGPUGeneration(string hashedPassword, int maxLength)
    {
      Console.WriteLine("Starting GPU brute force (GPU Generation)...");
      DateTime startTime = DateTime.Now;
      
      if (kernel.CrackWithGPUGeneration(characterSet, maxLength, hashedPassword, out string foundPassword))
      {
        DateTime endTime = DateTime.Now;
        Console.WriteLine($"Password found by GPU bruteforcecracker (GPU Generation): {foundPassword}");
        Console.WriteLine($"Hash: {hashedPassword}");
        Console.WriteLine($"GPU Time taken (GPU Generation): {(endTime - startTime).TotalMilliseconds}ms");
        return;
      }
      
      DateTime endTime2 = DateTime.Now;
      Console.WriteLine($"Password not found by GPU (GPU Generation). Time taken: {(endTime2 - startTime).TotalMilliseconds}ms");
    }
  }
}
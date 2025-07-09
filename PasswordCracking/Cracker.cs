using System.Text;

namespace PasswordCracking
{
  /// <summary>
  /// Main test class for comparing password cracking methods and validating hash consistency
  /// </summary>
  class Cracker
  {
    static void Main(string[] args) 
    {
  
      List<string> hashedPasswords = new List<string>();

      // Test configuration
      string password = "abcdefgh"; // Early in search - starts with 'a'
      int numberOfPasswords = 1;
      int passWordLength = password.Length;
      string testCharSet = "abcdefghijklmnopqrstuvwxyz"; // 26 lowercase letters

      // Generate target password hash
      for (int i = 0; i < numberOfPasswords; i++)
      {
        Console.WriteLine($"Target password: {password}");
        string hashed = PasswordHasher.HashPassword(password);
        Console.WriteLine($"Target hash: {hashed}");
        hashedPasswords.Add(hashed);
      }

      int maxLength = passWordLength;
      
      BruteForceCracker bruteForceCracker = new BruteForceCracker(characterSet: testCharSet, maxLength);

      // Performance comparison tests
      foreach (var hashedPassword in hashedPasswords)
      {
        // Ultimate GPU generation method
        Console.WriteLine("--- GPU Generation Implementation (Ultimate) ---");
        bruteForceCracker.CrackPasswordGPUGeneration(hashedPassword, maxLength);
      }
    }
  }
}


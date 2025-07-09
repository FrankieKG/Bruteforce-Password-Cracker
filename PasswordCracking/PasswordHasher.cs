using System.Text;
using System.Security.Cryptography;

namespace PasswordCracking
{
  /// <summary>
  /// SHA-256 password hashing utility for generating secure password hashes
  /// </summary>
  class PasswordHasher
  {
    /// <summary>
    /// Computes SHA-256 hash of a password string
    /// </summary>
    /// <param name="password">The password to hash</param>
    /// <returns>64-character lowercase hexadecimal hash string</returns>
    public static string HashPassword(string password)
    {
      using (SHA256 sha256 = SHA256.Create())
      {
        byte[] hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return BitConverter.ToString(hashedBytes).Replace("-", "").ToLower();
      }
    }

  }
}

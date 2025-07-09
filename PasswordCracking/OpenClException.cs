using OpenCL.Net;

namespace PasswordCracking
{
    /// <summary>
    /// Custom exception for OpenCL-related errors with enhanced debugging information
    /// </summary>
    public class OpenClException : Exception
    {
        /// <summary>
        /// The OpenCL error code that caused this exception
        /// </summary>
        public ErrorCode ErrorCode { get; }

        /// <summary>
        /// Creates a new OpenCL exception with the specified error code and message
        /// </summary>
        /// <param name="errorCode">The OpenCL error code</param>
        /// <param name="message">Detailed error message</param>
        public OpenClException(ErrorCode errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Creates a new OpenCL exception with the specified error code, message, and inner exception
        /// </summary>
        /// <param name="errorCode">The OpenCL error code</param>
        /// <param name="message">Detailed error message</param>
        /// <param name="innerException">The inner exception that caused this error</param>
        public OpenClException(ErrorCode errorCode, string message, Exception innerException) : base(message, innerException)
        {
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Returns a string representation of the exception including the OpenCL error code
        /// </summary>
        public override string ToString()
        {
            return $"OpenCL Error {ErrorCode}: {Message}\n{StackTrace}";
        }
    }
} 
using System;

namespace StoveLibrary.Services
{
    /// <summary>
    /// Exception thrown when authentication fails or expires.
    /// Used to distinguish auth errors from other API errors so the plugin
    /// can prompt the user to re-authenticate.
    /// </summary>
    public class StoveAuthenticationException : Exception
    {
        public int StatusCode { get; }

        public StoveAuthenticationException(string message) : base(message)
        {
        }

        public StoveAuthenticationException(string message, int statusCode) : base(message)
        {
            StatusCode = statusCode;
        }

        public StoveAuthenticationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}

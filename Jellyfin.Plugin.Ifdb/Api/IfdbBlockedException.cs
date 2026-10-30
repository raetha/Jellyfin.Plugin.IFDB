using System;

namespace Jellyfin.Plugin.Ifdb.Api;

/// <summary>
/// Thrown when fanedit.org answers with a bot-protection challenge instead of the requested page.
/// </summary>
public class IfdbBlockedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IfdbBlockedException"/> class.
    /// </summary>
    public IfdbBlockedException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IfdbBlockedException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public IfdbBlockedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IfdbBlockedException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public IfdbBlockedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

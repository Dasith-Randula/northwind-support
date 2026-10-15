namespace SupportDesk.Application.Exceptions;

/// <summary>
/// Thrown when an authenticated identity cannot be used for the requested operation.
/// </summary>
public sealed class UnauthorizedException(string message) : Exception(message)
{
}

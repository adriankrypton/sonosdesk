namespace SonosDesk.Core.Models;

public enum SonosErrorKind
{
    Unreachable,

    Timeout,

    Protocol,

    DeviceRejected,

    NotSupported,

    NotFound,

    Unauthorized,

    Rejected,
}

public sealed class SonosException : Exception
{
    public SonosException(SonosErrorKind kind, string message, Exception? innerException = null, int? errorCode = null)
        : base(message, innerException)
    {
        Kind = kind;
        ErrorCode = errorCode;
    }

    public SonosErrorKind Kind { get; }

    public int? ErrorCode { get; }
}

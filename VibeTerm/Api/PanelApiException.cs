using System;

namespace VibeTerm.Api;

public sealed class PanelApiException : Exception
{
    public int StatusCode { get; }

    public PanelApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

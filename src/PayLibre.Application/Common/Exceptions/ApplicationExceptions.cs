namespace PayLibre.Application.Common.Exceptions;

/// <summary>Input failed validation. Mapped to HTTP 400.</summary>
public sealed class ValidationException(string message) : Exception(message);

/// <summary>A resource was not found (scoped to the current tenant). Mapped to HTTP 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>A conflicting state (e.g. duplicate). Mapped to HTTP 409.</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>Authentication/authorization failure. Mapped to HTTP 401.</summary>
public sealed class AuthenticationException(string message) : Exception(message);

/// <summary>An upstream dependency (Xental) failed. Mapped to HTTP 502.</summary>
public sealed class UpstreamException(string message) : Exception(message);

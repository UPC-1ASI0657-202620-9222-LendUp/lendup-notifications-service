namespace LendUp.Notifications.Api.Application;

public sealed class NotFoundException(string message) : Exception(message);

public sealed class ForbiddenException(string message) : Exception(message);

public sealed class BusinessConflictException(string message) : Exception(message);

public sealed class DependencyUnavailableException : Exception
{
    public DependencyUnavailableException(string message) : base(message) { }
    public DependencyUnavailableException(string message, Exception inner) : base(message, inner) { }
}

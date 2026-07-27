using MediatR;

namespace Retail25.Application.Behaviors;

/// <summary>
/// Marker interface for commands (requests that mutate state).
/// </summary>
public interface ICommand { }

public interface ICommand<TResponse> : IRequest<TResponse> { }

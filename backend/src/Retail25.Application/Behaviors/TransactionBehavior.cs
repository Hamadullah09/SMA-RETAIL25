using MediatR;

namespace Retail25.Application.Behaviors;

/// <summary>
/// Transaction behavior is registered in Infrastructure where IDbContextTransactionFactory is available.
/// This file only contains the marker interface.
/// </summary>
public interface ITransactionalCommand { }

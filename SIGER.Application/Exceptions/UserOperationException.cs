namespace SIGER.Application.Exceptions;

public sealed class UserOperationException(Guid operationId, bool compensationFailed)
    : Exception("The user operation could not be completed safely.")
{
    public Guid OperationId { get; } = operationId;
    public bool CompensationFailed { get; } = compensationFailed;
}

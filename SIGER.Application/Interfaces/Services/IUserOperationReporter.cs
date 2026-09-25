namespace SIGER.Application.Interfaces.Services;

public interface IUserOperationReporter
{
    void ReportFailure(Guid operationId, string operation, long? localUserId, Guid authUserId,
        string failureType, string? compensationFailureType);
}

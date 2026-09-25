using Microsoft.Extensions.Logging;
using SIGER.Application.Interfaces.Services;

namespace SIGER.Infrastructure.Authentication;

public sealed class UserOperationReporter(ILogger<UserOperationReporter> logger) : IUserOperationReporter
{
    public void ReportFailure(Guid operationId, string operation, long? localUserId, Guid authUserId,
        string failureType, string? compensationFailureType)
    {
        // Technical identifiers and type names only: no exception bodies, email, passwords or tokens.
        logger.Log(compensationFailureType is null ? LogLevel.Error : LogLevel.Critical, new EventId(6201, "UserOperationFailed"),
            "User operation {OperationId} {Operation} failed; local {LocalUserId}, auth {AuthUserId}, type {FailureType}, compensation failure {CompensationFailureType}",
            operationId, operation, localUserId, authUserId, failureType, compensationFailureType);
    }
}

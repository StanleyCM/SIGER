using Microsoft.AspNetCore.Mvc;
using SIGER.Application.Base;

namespace SIGER.API.Extensions;

public static class ResultHttpExtensions
{
    // Existing Application contracts expose messages, not typed error codes.
    // Keep this compatibility mapping explicit and centralized until those contracts evolve.
    private static readonly HashSet<string> NotFoundErrors =
    [
        "User not found.", "Role not found.", "Category not found.", "Product not found.",
        "Table not found.", "Order not found.", "Order item not found.", "Payment not found.",
        "Reservation not found.", "Promotion not found.", "Audit record not found.",
        "Client not found.", "The table associated with the order was not found."
    ];
    private static readonly HashSet<string> ConflictErrors =
    [
        "A user with this email already exists.", "The order is already paid.",
        "A cancelled order cannot be paid.", "A payment already exists for this order.",
        "The selected table is not available.", "Paid or cancelled orders cannot be modified.",
        "Paid or cancelled orders cannot be updated by kitchen.",
        "A paid or cancelled order cannot request an account.",
        "Product is already associated with the promotion.",
        "Product is not associated with the promotion."
    ];

    public static IActionResult ToHttp<T>(this Result<T> result, ControllerBase controller,
        string? createdAction = null, Func<T, object>? routeValues = null)
    {
        if (result.IsFailure) return Failure(result, controller);
        return createdAction is not null && result.Value is not null && routeValues is not null
            ? controller.CreatedAtAction(createdAction, routeValues(result.Value), result.Value)
            : controller.Ok(result.Value);
    }

    public static IActionResult ToHttp(this Result result, ControllerBase controller) =>
        result.IsSuccess ? controller.NoContent() : Failure(result, controller);

    private static IActionResult Failure(Result result, ControllerBase controller)
    {
        var error = result.Error ?? "The request could not be completed.";
        if (error is "Invalid credentials." or "The user profile is unavailable or inactive.")
            return controller.Problem(statusCode: 401, title: "Authentication failed.", detail: "Invalid credentials or inactive profile.");
        if (NotFoundErrors.Contains(error) ||
            (error.StartsWith("Product ", StringComparison.Ordinal) && error.EndsWith(" not found.", StringComparison.Ordinal)))
            return controller.Problem(statusCode: 404, title: "Resource not found.", detail: error);
        if (ConflictErrors.Contains(error))
            return controller.Problem(statusCode: 409, title: "Request conflicts with the current state.", detail: error);
        return controller.ValidationProblem(new ValidationProblemDetails(
            new Dictionary<string, string[]> { ["request"] = result.Errors.ToArray() })
        {
            Status = 400,
            Title = "Request validation failed.",
            Instance = controller.Request.Path,
            Extensions = { ["traceId"] = controller.HttpContext.TraceIdentifier }
        });
    }
}

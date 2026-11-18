using SIGER.Application.Base;
using SIGER.Application.DTOs.Payments;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Application.Services;

public class PaymentService : IPaymentService
{
    public const string TableOrderNotServed = "A table order must be served before payment.";
    public const string AccountNotRequested = "Request the account before paying a table order.";

    public static string? GetPaymentStateError(OrderType type, OrderStatus status, bool accountRequested)
        => status switch
        {
            OrderStatus.PreOrdered => "A preorder is not operational.",
            OrderStatus.Paid => "The order is already paid.",
            OrderStatus.Cancelled => "A cancelled order cannot be paid.",
            OrderStatus.Pending or OrderStatus.InPreparation or OrderStatus.Ready when type == OrderType.Table => TableOrderNotServed,
            OrderStatus.Served when type == OrderType.Table && !accountRequested => AccountNotRequested,
            OrderStatus.Served when type == OrderType.Table => null,
            OrderStatus.Pending or OrderStatus.InPreparation or OrderStatus.Ready or OrderStatus.Served when type == OrderType.TakeAway => null,
            _ => "The order state does not allow payment."
        };
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly ITableRepository _tableRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public PaymentService(
        IPaymentRepository paymentRepository,
        IOrderRepository orderRepository,
        ITableRepository tableRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        TimeProvider? clock = null)
    {
        _paymentRepository = paymentRepository;
        _orderRepository = orderRepository;
        _tableRepository = tableRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _clock = clock ?? TimeProvider.System;
    }

    public Task<Result<PaymentDto>> ProcessPaymentAsync(ProcessPaymentRequestDto request, CancellationToken cancellationToken = default)
        => _unitOfWork.ExecuteInTransactionAsync(token => ProcessPaymentInTransactionAsync(request, token), cancellationToken);

    private async Task<Result<PaymentDto>> ProcessPaymentInTransactionAsync(ProcessPaymentRequestDto request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetWithDetailsAsync(request.OrderId, cancellationToken);
        if (order is null) return Result<PaymentDto>.Failure("Order not found.");
        if (GetPaymentStateError(order.Type, order.Status, order.AccountRequested) is { } stateError)
            return Result<PaymentDto>.Failure(stateError);
        if (request.Amount <= 0 || request.Amount != order.Total) return Result<PaymentDto>.Failure("Payment amount must match the order total.");
        if (await _paymentRepository.GetByOrderIdAsync(order.Id, cancellationToken) is not null) return Result<PaymentDto>.Failure("A payment already exists for this order.");
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null || !user.IsActive) return Result<PaymentDto>.Failure("The responsible user does not exist or is inactive.");

        Table? table = null;
        if (order.TableId.HasValue)
        {
            // Match create/cancel lock order: table first, then order/payment writes.
            table = await _tableRepository.GetByIdForUpdateAsync(order.TableId.Value, cancellationToken);
            if (table is null) return Result<PaymentDto>.Failure("The table associated with the order was not found.");
        }

        var now = _clock.GetUtcNow();
        var payment = new Payment
        {
            OrderId = order.Id, Order = order, UserId = user.Id, User = user, Amount = request.Amount,
            Method = request.Method, Status = PaymentStatus.Completed, Reference = Normalize(request.Reference),
            PaymentDate = now, UpdatedAt = now
        };

        await _paymentRepository.AddAsync(payment, cancellationToken);
        order.Status = OrderStatus.Paid;
        order.UpdatedAt = now;
        _orderRepository.Update(order);
        if (table is { Status: TableStatus.Occupied } && !await _orderRepository.HasActiveOrdersAsync(table.Id, order.Id, cancellationToken))
        {
            table.Status = TableStatus.Available;
            table.UpdatedAt = now;
            _tableRepository.Update(table);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<PaymentDto>.Success(Map(payment));
    }

    public async Task<Result<PaymentDto>> GetByOrderIdAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.GetByOrderIdAsync(orderId, cancellationToken);
        return payment is null ? Result<PaymentDto>.Failure("Payment not found.") : Result<PaymentDto>.Success(Map(payment));
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static PaymentDto Map(Payment payment) => new()
    {
        Id = payment.Id, OrderId = payment.OrderId, UserId = payment.UserId, Amount = payment.Amount,
        Method = payment.Method, Status = payment.Status, Reference = payment.Reference,
        PaymentDate = payment.PaymentDate, UpdatedAt = payment.UpdatedAt
    };
}

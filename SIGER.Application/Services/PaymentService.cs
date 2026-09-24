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
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly ITableRepository _tableRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public PaymentService(
        IPaymentRepository paymentRepository,
        IOrderRepository orderRepository,
        ITableRepository tableRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _orderRepository = orderRepository;
        _tableRepository = tableRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PaymentDto>> ProcessPaymentAsync(ProcessPaymentRequestDto request, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetWithDetailsAsync(request.OrderId, cancellationToken);
        if (order is null) return Result<PaymentDto>.Failure("Order not found.");
        if (order.Status == OrderStatus.Paid) return Result<PaymentDto>.Failure("The order is already paid.");
        if (order.Status == OrderStatus.Cancelled) return Result<PaymentDto>.Failure("A cancelled order cannot be paid.");
        if (request.Amount <= 0 || request.Amount != order.Total) return Result<PaymentDto>.Failure("Payment amount must match the order total.");
        if (await _paymentRepository.GetByOrderIdAsync(order.Id, cancellationToken) is not null) return Result<PaymentDto>.Failure("A payment already exists for this order.");
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null || !user.IsActive) return Result<PaymentDto>.Failure("The responsible user does not exist or is inactive.");

        Table? table = null;
        if (order.TableId.HasValue)
        {
            table = order.Table ?? await _tableRepository.GetByIdAsync(order.TableId.Value, cancellationToken);
            if (table is null) return Result<PaymentDto>.Failure("The table associated with the order was not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var payment = new Payment
        {
            OrderId = order.Id, Order = order, UserId = user.Id, User = user, Amount = request.Amount,
            Method = request.Method, Status = PaymentStatus.Completed, Reference = Normalize(request.Reference),
            PaymentDate = now, UpdatedAt = now
        };

        await _unitOfWork.ExecuteInTransactionAsync(async transactionToken =>
        {
            await _paymentRepository.AddAsync(payment, transactionToken);
            order.Status = OrderStatus.Paid;
            order.UpdatedAt = now;
            _orderRepository.Update(order);
            if (table is not null)
            {
                table.Status = TableStatus.Available;
                table.UpdatedAt = now;
                _tableRepository.Update(table);
            }
            await _unitOfWork.SaveChangesAsync(transactionToken);
        }, cancellationToken);

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

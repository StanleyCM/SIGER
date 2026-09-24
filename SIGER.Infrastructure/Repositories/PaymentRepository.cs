using Microsoft.EntityFrameworkCore;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Domain.Entities;
using SIGER.Infrastructure.Persistence;

namespace SIGER.Infrastructure.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly SIGERDbContext _context;

    public PaymentRepository(SIGERDbContext context) => _context = context;

    public Task<Payment?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => _context.Payments.FirstOrDefaultAsync(payment => payment.Id == id, cancellationToken);

    public Task<Payment?> GetByOrderIdAsync(long orderId, CancellationToken cancellationToken = default)
        => _context.Payments.AsNoTracking().FirstOrDefaultAsync(payment => payment.OrderId == orderId, cancellationToken);

    public async Task AddAsync(Payment payment, CancellationToken cancellationToken = default)
        => await _context.Payments.AddAsync(payment, cancellationToken);

    public void Update(Payment payment) => _context.Payments.Update(payment);
}

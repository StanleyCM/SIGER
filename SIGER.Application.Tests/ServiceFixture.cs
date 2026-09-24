using Moq;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Application.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Application.Tests;

internal sealed class ServiceFixture
{
    public Mock<IUserRepository> Users { get; } = new();
    public Mock<IRoleRepository> Roles { get; } = new();
    public Mock<ICategoryRepository> Categories { get; } = new();
    public Mock<IProductRepository> Products { get; } = new();
    public Mock<ITableRepository> Tables { get; } = new();
    public Mock<IOrderRepository> Orders { get; } = new();
    public Mock<IPaymentRepository> Payments { get; } = new();
    public Mock<IReservationRepository> Reservations { get; } = new();
    public Mock<IPromotionRepository> Promotions { get; } = new();
    public Mock<IAuditRepository> Audits { get; } = new();
    public Mock<IReportRepository> Reports { get; } = new();
    public Mock<IAuthProvider> Provider { get; } = new();
    public Mock<IUnitOfWork> Work { get; } = new();
    public Role Role { get; } = new() { Id = 1, Name = "Waiter", IsActive = true };
    public User User { get; }
    public Category Category { get; } = new() { Id = 2, Name = "Food", IsActive = true };
    public Product Product { get; }
    public Table Table { get; } = new() { Id = 4, Number = 7, Capacity = 4, Status = TableStatus.Available, Version = 5 };
    public Order Order { get; }
    public bool InTransaction { get; private set; }
    public CancellationToken Token { get; } = new CancellationTokenSource().Token;

    public ServiceFixture()
    {
        User = new() { Id = 1, RoleId = 1, Role = Role, AuthUserId = Guid.NewGuid(),
            FirstName = "Ana", LastName = "Diaz", Email = "ana@example.test", IsActive = true };
        Product = new() { Id = 3, CategoryId = 2, Category = Category, Name = "Soup", Price = 12.50m, IsAvailable = true, Version = 5 };
        Order = new() { Id = 5, UserId = 1, User = User, TableId = 4, Table = Table, Total = 25m, Version = 5 };
        Order.Details.Add(new() { Id = 6, OrderId = 5, Order = Order, ProductId = 3, Product = Product,
            Quantity = 2, UnitPrice = 12.50m, Subtotal = 25m });
        Users.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(User);
        Users.Setup(x => x.GetByAuthUserIdAsync(User.AuthUserId, It.IsAny<CancellationToken>())).ReturnsAsync(User);
        Roles.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Role);
        Categories.Setup(x => x.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(Category);
        Products.Setup(x => x.GetByIdAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(Product);
        Tables.Setup(x => x.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(Table);
        Orders.Setup(x => x.GetWithDetailsAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(Order);
        Work.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        Work.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task> operation, CancellationToken token) =>
            {
                Assert.False(InTransaction);
                InTransaction = true;
                try { await operation(token); } finally { InTransaction = false; }
            });
        Provider.Setup(x => x.CreateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(User.AuthUserId);
    }

    public UserService UserService => new(Users.Object, Roles.Object, Provider.Object, Work.Object);
    public AuthService AuthService => new(Provider.Object, Users.Object);
    public CategoryService CategoryService => new(Categories.Object, Work.Object);
    public ProductService ProductService => new(Products.Object, Categories.Object, Work.Object);
    public TableService TableService => new(Tables.Object, Work.Object);
    public OrderService OrderService => new(Orders.Object, Tables.Object, Products.Object, Users.Object, Work.Object);
    public KitchenService KitchenService => new(Orders.Object, Work.Object);
    public PaymentService PaymentService => new(Payments.Object, Orders.Object, Tables.Object, Users.Object, Work.Object);
    public ReservationService ReservationService => new(Reservations.Object, Users.Object, Tables.Object, Work.Object);
    public PromotionService PromotionService => new(Promotions.Object, Products.Object, Work.Object);
    public ReportService ReportService => new(Reports.Object);
    public AuditService AuditService => new(Audits.Object);
    public void NoSave() => Work.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
}


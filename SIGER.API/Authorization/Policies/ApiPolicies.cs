namespace SIGER.API.Authorization.Policies;

public static class ApiPolicies
{
    public const string AdministratorOnly = nameof(AdministratorOnly);
    public const string StaffOnly = nameof(StaffOnly);
    public const string WaiterOrAdministrator = nameof(WaiterOrAdministrator);
    public const string KitchenOrAdministrator = nameof(KitchenOrAdministrator);
    public const string CashierOrAdministrator = nameof(CashierOrAdministrator);
    public const string Reservations = nameof(Reservations);
}

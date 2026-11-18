namespace SIGER.Desktop.Models;

internal sealed record NavigationModule(string Title, string Description);

internal sealed record RoleNavigation(string RoleLabel, IReadOnlyList<NavigationModule> Modules)
{
    private static readonly NavigationModule Tables = new("Mesas", "Disponibilidad y estado de las mesas del restaurante.");
    private static readonly NavigationModule Orders = new("Órdenes", "Seguimiento de las órdenes de atención.");
    private static readonly NavigationModule Reservations = new("Reservaciones", "Gestión de las próximas visitas.");
    private static readonly NavigationModule Payments = new("Pagos", "Consulta de los pagos registrados.");

    // These keys are the names returned by SIGER.API, not names entered in Desktop.
    private static readonly Dictionary<string, RoleNavigation> Profiles = new(StringComparer.Ordinal)
    {
        ["Administrador"] = Create("Administrador",
            new("Dashboard", "Vista general de la operación del restaurante."),
            new("Usuarios", "Administración de usuarios y roles."),
            new("Categorías", "Organización del catálogo."),
            new("Productos", "Productos, precios y disponibilidad."),
            Tables, Reservations, Orders, Payments,
            new("Promociones", "Administración de promociones."),
            new("Reportes", "Consultas de ventas y productos."),
            new("Auditoría", "Consulta del historial de cambios.")),
        ["Mesero"] = Create("Mesero", Tables, Orders, Reservations),
        ["Cocinero"] = Create("Cocina", new NavigationModule("Órdenes de cocina", "Seguimiento de la preparación de las órdenes.")),
        ["Cajero"] = Create("Cajero", new("Cobros", "Órdenes listas para registrar el pago."), Payments)
    };

    internal static RoleNavigation? For(string? roleName) =>
        roleName is null ? null : Profiles.GetValueOrDefault(roleName);

    internal bool Allows(NavigationModule module) => Modules.Contains(module);

    private static RoleNavigation Create(string label, params NavigationModule[] modules) =>
        new(label, Array.AsReadOnly(modules));
}

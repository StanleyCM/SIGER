using Microsoft.EntityFrameworkCore;
using SIGER.Domain.Entities;

namespace SIGER.Infrastructure.Persistence.Configurations;

// PostgreSQL expressions mirrored by the manually reviewed 003 script; never applied automatically.
internal static class GuestPreOrderConstraints
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<Reservation>().ToTable("reserva", t =>
        {
            t.HasCheckConstraint("ck_reserva_contacto_invitado",
                "id_usuario IS NOT NULL OR (COALESCE(length(btrim(nombre_contacto)), 0) > 0 AND COALESCE(length(btrim(telefono_contacto)), 0) > 0)");
            t.HasCheckConstraint("ck_reserva_credencial",
                "(token_acceso_hash IS NULL) = (token_acceso_expira IS NULL) AND (token_acceso_hash IS NULL OR octet_length(token_acceso_hash) = 32) AND (id_usuario IS NOT NULL OR token_acceso_hash IS NOT NULL)");
        });
        model.Entity<Order>().ToTable("orden", t =>
        {
            t.HasCheckConstraint("ck_orden_responsable",
                "id_usuario IS NOT NULL OR (origen::text = 'Web' AND tipo::text = 'Mesa' AND id_reserva IS NOT NULL AND id_mesa IS NOT NULL AND id_cliente IS NULL AND estado::text IN ('Preordenada', 'Cancelada') AND NOT cuenta_solicitada)");
            t.HasCheckConstraint("ck_orden_preorden",
                "estado::text <> 'Preordenada' OR (origen::text = 'Web' AND tipo::text = 'Mesa' AND id_reserva IS NOT NULL AND id_mesa IS NOT NULL AND id_usuario IS NULL AND id_cliente IS NULL AND NOT cuenta_solicitada)");
        });
    }
}

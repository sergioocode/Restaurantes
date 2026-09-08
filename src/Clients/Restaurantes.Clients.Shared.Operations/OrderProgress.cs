using Restaurantes.Orders.Contracts;

namespace Restaurantes.Clients.Shared.Operations;

public sealed record OrderStatusText(string Title, string Detail);

public static class OrderProgress
{
    public static OrderLifecycleStatus Parse(string? status)
    {
        return Enum.TryParse(status, ignoreCase: true, out OrderLifecycleStatus value)
            ? value
            : OrderLifecycleStatus.Unknown;
    }

    public static bool Is(string? status, OrderLifecycleStatus expected)
    {
        return Parse(status) == expected;
    }

    public static bool IsReady(string? status)
    {
        return Is(status, OrderLifecycleStatus.Ready);
    }

    public static bool CanCancelBeforePreparation(string? status)
    {
        return Parse(status)
            is OrderLifecycleStatus.Draft
                or OrderLifecycleStatus.Submitted
                or OrderLifecycleStatus.InPreparation;
    }

    public static string PickupLabel(string? status)
    {
        return Parse(status) switch
        {
            OrderLifecycleStatus.Draft => "Pendiente de envío",
            OrderLifecycleStatus.Submitted => "En cola",
            OrderLifecycleStatus.InPreparation => "En preparación",
            OrderLifecycleStatus.Ready => "Listo para entregar",
            OrderLifecycleStatus.Delivered => "Entregado",
            OrderLifecycleStatus.Cancelled => "Cancelado",
            _ => status ?? "Sin estado",
        };
    }

    public static OrderStatusText CustomerText(string? status)
    {
        return Parse(status) switch
        {
            OrderLifecycleStatus.Draft => new(
                "Estamos recibiendo tu pedido",
                "Un momento, estamos confirmando los datos."
            ),
            OrderLifecycleStatus.Submitted => new(
                "Pedido recibido",
                "La cocina ya tiene tu comanda."
            ),
            OrderLifecycleStatus.InPreparation => new(
                "Lo estamos preparando",
                "El equipo está trabajando en ella."
            ),
            OrderLifecycleStatus.Ready => new(
                "Tu pedido está listo",
                "Enseguida te lo llevamos a la mesa."
            ),
            OrderLifecycleStatus.Delivered => new("Pedido entregado", "¡Que aproveche!"),
            OrderLifecycleStatus.Cancelled => new(
                "Pedido cancelado",
                "Consulta con el personal si necesitas ayuda."
            ),
            _ => new("Pedido actualizado", "Te avisaremos cuando cambie."),
        };
    }

    public static string CssClass(string? status)
    {
        return Parse(status) switch
        {
            OrderLifecycleStatus.Draft => "draft",
            OrderLifecycleStatus.Submitted => "submitted",
            OrderLifecycleStatus.InPreparation => "inpreparation",
            OrderLifecycleStatus.Ready => "ready",
            OrderLifecycleStatus.Delivered => "delivered",
            OrderLifecycleStatus.Cancelled => "cancelled",
            _ => "unknown",
        };
    }

    public static string Summarize(IEnumerable<string?> statuses)
    {
        OrderLifecycleStatus[] values = statuses.Select(Parse).ToArray();
        if (values.Length == 0)
        {
            return "Sin comandas activas";
        }

        int queued = values.Count(value => value == OrderLifecycleStatus.Submitted);
        int preparing = values.Count(value => value == OrderLifecycleStatus.InPreparation);
        int ready = values.Count(value => value == OrderLifecycleStatus.Ready);
        List<string> parts = [];
        if (queued > 0)
        {
            parts.Add($"{queued} en cola");
        }

        if (preparing > 0)
        {
            parts.Add($"{preparing} preparando");
        }

        if (ready > 0)
        {
            parts.Add($"{ready} lista{(ready == 1 ? "" : "s")}");
        }

        return parts.Count == 0 ? "Sin comandas activas" : string.Join(" · ", parts);
    }
}

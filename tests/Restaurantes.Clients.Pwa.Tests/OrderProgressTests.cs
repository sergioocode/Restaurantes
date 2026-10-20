using Restaurantes.Clients.Shared.Operations;
using Restaurantes.Orders.Contracts;
using Xunit;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class OrderProgressTests
{
    [Theory]
    [InlineData("draft", OrderLifecycleStatus.Draft)]
    [InlineData("SUBMITTED", OrderLifecycleStatus.Submitted)]
    [InlineData("InPreparation", OrderLifecycleStatus.InPreparation)]
    [InlineData("Ready", OrderLifecycleStatus.Ready)]
    [InlineData("Delivered", OrderLifecycleStatus.Delivered)]
    [InlineData("Cancelled", OrderLifecycleStatus.Cancelled)]
    public void Parse_accepts_known_statuses_without_case_sensitivity(
        string value,
        OrderLifecycleStatus expected
    ) => Assert.Equal(expected, OrderProgress.Parse(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-status")]
    public void Parse_returns_unknown_for_missing_or_unrecognized_status(string? value) =>
        Assert.Equal(OrderLifecycleStatus.Unknown, OrderProgress.Parse(value));

    [Theory]
    [InlineData("Draft", true)]
    [InlineData("Submitted", true)]
    [InlineData("InPreparation", true)]
    [InlineData("Ready", false)]
    [InlineData("Delivered", false)]
    [InlineData("Cancelled", false)]
    [InlineData(null, false)]
    public void Status_checks_match_the_expected_lifecycle(string? value, bool canCancel) =>
        Assert.Equal(canCancel, OrderProgress.CanCancelBeforePreparation(value));

    [Fact]
    public void Ready_helpers_match_only_ready_status()
    {
        Assert.True(OrderProgress.IsReady("ready"));
        Assert.False(OrderProgress.IsReady("submitted"));
        Assert.True(OrderProgress.Is("draft", OrderLifecycleStatus.Draft));
        Assert.False(OrderProgress.Is("draft", OrderLifecycleStatus.Ready));
    }

    [Theory]
    [InlineData("Draft", "Pendiente de envío")]
    [InlineData("Submitted", "En cola")]
    [InlineData("InPreparation", "En preparación")]
    [InlineData("Ready", "Listo para entregar")]
    [InlineData("Delivered", "Entregado")]
    [InlineData("Cancelled", "Cancelado")]
    public void Pickup_label_maps_each_lifecycle_status(string value, string expected) =>
        Assert.Equal(expected, OrderProgress.PickupLabel(value));

    [Fact]
    public void Pickup_label_preserves_unknown_status_and_handles_null()
    {
        Assert.Equal("Paused", OrderProgress.PickupLabel("Paused"));
        Assert.Equal("Sin estado", OrderProgress.PickupLabel(null));
    }

    [Theory]
    [InlineData(
        "Draft",
        "Estamos recibiendo tu pedido",
        "Un momento, estamos confirmando los datos."
    )]
    [InlineData("Submitted", "Pedido recibido", "La cocina ya tiene tu comanda.")]
    [InlineData("InPreparation", "Lo estamos preparando", "El equipo está trabajando en ella.")]
    [InlineData("Ready", "Tu pedido está listo", "Enseguida te lo llevamos a la mesa.")]
    [InlineData("Delivered", "Pedido entregado", "¡Que aproveche!")]
    [InlineData("Cancelled", "Pedido cancelado", "Consulta con el personal si necesitas ayuda.")]
    public void Customer_text_maps_each_lifecycle_status(
        string value,
        string expectedTitle,
        string expectedDetail
    ) =>
        Assert.Equal(
            new OrderStatusText(expectedTitle, expectedDetail),
            OrderProgress.CustomerText(value)
        );

    [Fact]
    public void Customer_text_uses_fallback_for_unknown_status() =>
        Assert.Equal(
            new OrderStatusText("Pedido actualizado", "Te avisaremos cuando cambie."),
            OrderProgress.CustomerText("Paused")
        );

    [Theory]
    [InlineData("Draft", "draft")]
    [InlineData("Submitted", "submitted")]
    [InlineData("InPreparation", "inpreparation")]
    [InlineData("Ready", "ready")]
    [InlineData("Delivered", "delivered")]
    [InlineData("Cancelled", "cancelled")]
    [InlineData("Paused", "unknown")]
    public void Css_class_matches_lifecycle_status(string value, string expected) =>
        Assert.Equal(expected, OrderProgress.CssClass(value));

    [Fact]
    public void Summarize_handles_empty_unrecognized_and_active_statuses()
    {
        Assert.Equal("Sin comandas activas", OrderProgress.Summarize([]));
        Assert.Equal("Sin comandas activas", OrderProgress.Summarize(["Draft", "Cancelled"]));
        Assert.Equal(
            "1 en cola · 2 preparando · 1 lista",
            OrderProgress.Summarize([
                "Submitted",
                "InPreparation",
                "inpreparation",
                "Ready",
                "Delivered",
            ])
        );
        Assert.Equal("2 listas", OrderProgress.Summarize(["Ready", "Ready"]));
    }
}

using LendUp.Notifications.Api.Domain;

namespace LendUp.Notifications.Tests.Unit.Domain;

public class ReminderPolicyTests
{
    private readonly ReminderPolicy _policy = new(TimeSpan.FromHours(24), TimeSpan.FromHours(12));
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void HoraDeLimaSeConvierteAUtc() =>
        Assert.Equal(new DateTime(2026, 11, 10, 14, 0, 0, DateTimeKind.Utc), ReminderPolicy.LimaToUtc(new DateTime(2026, 11, 10, 9, 0, 0)));

    [Fact]
    public void RecordatoriosSeProgramanConLaAnticipacionConfigurada()
    {
        Assert.Equal(new DateTime(2026, 11, 9, 14, 0, 0, DateTimeKind.Utc), _policy.DeliveryReminderAt(new DateTime(2026, 11, 10, 9, 0, 0), Now));
        Assert.Equal(new DateTime(2026, 11, 12, 2, 0, 0, DateTimeKind.Utc), _policy.ReturnReminderAt(new DateTime(2026, 11, 12, 9, 0, 0), Now));
    }

    [Fact]
    public void SiLaAnticipacionYaPasoSaleDeInmediato() =>
        Assert.Equal(Now, _policy.DeliveryReminderAt(new DateTime(2026, 11, 8, 10, 0, 0), Now));

    [Fact]
    public void AnticipacionNegativaNoEsValida() =>
        Assert.Throws<ArgumentException>(() => new ReminderPolicy(TimeSpan.FromHours(-1), TimeSpan.Zero));
}

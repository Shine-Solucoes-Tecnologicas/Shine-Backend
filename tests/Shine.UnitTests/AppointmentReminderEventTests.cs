using Scheduling.Application;

namespace Shine.UnitTests;

public sealed class AppointmentReminderEventTests
{
    [Fact]
    public void Reminder_processing_event_keeps_tenant_and_schedule_context()
    {
        var eventItem = new AppointmentReminderProcessingRequestedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 14, 11, 0, 0, DateTimeKind.Utc),
            "rescheduled", DateTime.UtcNow);

        Assert.Equal("rescheduled", eventItem.Trigger);
        Assert.NotEqual(Guid.Empty, eventItem.TenantId);
        Assert.Equal(DateTimeKind.Utc, eventItem.StartsAtUtc.Kind);
    }
}

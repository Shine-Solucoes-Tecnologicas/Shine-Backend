using Microsoft.EntityFrameworkCore;
using Shine.Application;

namespace Scheduling.Infrastructure;

/// <summary>Reads customer history from Scheduling without projecting appointments into the customer store.</summary>
public sealed class CustomerAppointmentHistoryReader(SchedulingDbContext db) : ICustomerHistoryReader
{
    public async Task<PagedResponse<CustomerHistoryItem>> ListAsync(
        Guid tenantId,
        Guid customerId,
        CustomerHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = query.Page.ValidatedPage;
        var pageSize = query.Page.ValidatedPageSize;
        var appointments = db.Appointments.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.CustomerId == customerId);
        var total = await appointments.CountAsync(cancellationToken);
        var rows = await appointments
            .OrderByDescending(x => x.StartsAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.ProfessionalId,
                x.ServiceId,
                x.StartsAtUtc,
                x.EndsAtUtc,
                x.Status
            })
            .ToArrayAsync(cancellationToken);
        var items = rows.Select(x => new CustomerHistoryItem(
            "scheduling.appointment",
            "scheduling",
            x.Id,
            x.StartsAtUtc,
            new AppointmentCustomerHistoryDetails(
                x.ProfessionalId,
                x.ServiceId,
                x.StartsAtUtc,
                x.EndsAtUtc,
                x.Status.ToString())))
            .ToArray();
        return PagedResponse<CustomerHistoryItem>.Create(items, page, pageSize, total);
    }
}

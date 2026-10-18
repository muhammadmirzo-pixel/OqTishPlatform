extern alias WorkerHost;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OqTish.Application.Booking;
using OqTish.Domain.Booking;
using OqTish.IntegrationTests.Infrastructure;
using WorkerHost::OqTish.Worker;

namespace OqTish.IntegrationTests.Booking;

/// <summary>The Worker's hosted sweeper really runs the expiry job on its timer against the database.</summary>
public class ExpirySweeperServiceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Worker_sweeper_expires_overdue_requests_on_its_timer()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 2);
        var overdue = (await host.BookAsync(w, w.Patients[0], w.At(10))).Value;
        var fresh = (await host.BookAsync(w, w.Patients[1], w.At(11))).Value;
        await host.SetDeadlineAsync(overdue.Id, TimeSpan.FromSeconds(-1));

        using var service = new ExpirySweeperService(
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BookingOptions { SweeperIntervalSeconds = 1, SweeperBatchSize = 1 }),
            NullLogger<ExpirySweeperService>.Instance,
            TimeProvider.System);
        await service.StartAsync(ct);
        try
        {
            for (var i = 0; i < 100 && (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == overdue.Id, ct)).Status == AppointmentStatus.Pending; i++)
            {
                await Task.Delay(100, ct);
            }
        }
        finally
        {
            await service.StopAsync(ct);
        }

        Assert.Equal(AppointmentStatus.Expired, (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == overdue.Id, ct)).Status);
        Assert.Equal(AppointmentStatus.Pending, (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == fresh.Id, ct)).Status);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }
}

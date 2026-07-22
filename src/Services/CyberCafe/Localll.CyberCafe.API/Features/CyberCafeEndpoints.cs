using System.Security.Claims;
using Localll.Common.Auth;
using Localll.Contracts;
using Localll.CyberCafe.API.Data;
using Localll.CyberCafe.API.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Localll.CyberCafe.API.Features;

public record BookAppointmentRequest(string ServiceType, DateTime ScheduledAtUtc, string? Notes);
public record AssignOperatorRequest(Guid OperatorId);
public record AttachFileRequest(string FileName, string StorageUrl, string ContentType, long SizeBytes);

public static class CyberCafeEndpoints
{
    private static readonly string[] AllowedContentTypes =
        ["application/pdf", "image/jpeg", "image/png"];

    public static void MapCyberCafeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/appointments").WithTags("Cyber Cafe").RequireAuthorization();

        group.MapPost("/", async (
            BookAppointmentRequest request,
            ClaimsPrincipal principal,
            CyberCafeDbContext db,
            IPublishEndpoint publisher) =>
        {
            if (request.ScheduledAtUtc <= DateTime.UtcNow)
                return Results.BadRequest(new { error = "Appointment must be scheduled in the future." });

            var appointment = new Appointment
            {
                CustomerId = principal.GetUserId(),
                ServiceType = request.ServiceType,
                ScheduledAtUtc = request.ScheduledAtUtc,
                Notes = request.Notes
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();

            await publisher.Publish(new AppointmentBookedEvent(
                appointment.Id, appointment.CustomerId, appointment.ServiceType,
                appointment.ScheduledAtUtc, DateTime.UtcNow));

            return Results.Created($"/api/v1/appointments/{appointment.Id}", appointment);
        });

        group.MapGet("/mine", async (ClaimsPrincipal principal, CyberCafeDbContext db) =>
            Results.Ok(await db.Appointments.Include(a => a.Files).AsNoTracking()
                .Where(a => a.CustomerId == principal.GetUserId())
                .OrderByDescending(a => a.ScheduledAtUtc)
                .Take(50).ToListAsync()));

        group.MapPost("/{appointmentId:guid}/assign", async (
            Guid appointmentId, AssignOperatorRequest request, CyberCafeDbContext db) =>
        {
            var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
            if (appointment is null) return Results.NotFound();
            if (appointment.Status is not (AppointmentStatus.Booked or AppointmentStatus.OperatorAssigned))
                return Results.Conflict(new { error = $"Cannot assign an operator while status is {appointment.Status}." });

            appointment.OperatorId = request.OperatorId;
            appointment.Status = AppointmentStatus.OperatorAssigned;
            appointment.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(appointment);
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "CyberCafeOperator"));

        group.MapPost("/{appointmentId:guid}/start", async (Guid appointmentId, CyberCafeDbContext db) =>
        {
            var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
            if (appointment is null) return Results.NotFound();
            if (appointment.Status != AppointmentStatus.OperatorAssigned)
                return Results.Conflict(new { error = "An operator must be assigned before the session starts." });

            appointment.Status = AppointmentStatus.InProgress;
            appointment.VideoSessionId = $"rtc-{Guid.NewGuid():N}";
            appointment.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { appointment.Id, appointment.VideoSessionId });
        }).RequireAuthorization(policy => policy.RequireRole("CyberCafeOperator"));

        group.MapPost("/{appointmentId:guid}/complete", async (Guid appointmentId, CyberCafeDbContext db) =>
        {
            var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
            if (appointment is null) return Results.NotFound();

            appointment.Status = AppointmentStatus.Completed;
            appointment.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(appointment);
        }).RequireAuthorization(policy => policy.RequireRole("CyberCafeOperator"));

        // File metadata only — the binary lives in object storage (upload via presigned URL).
        group.MapPost("/{appointmentId:guid}/files", async (
            Guid appointmentId, AttachFileRequest request, CyberCafeDbContext db) =>
        {
            if (!AllowedContentTypes.Contains(request.ContentType))
                return Results.BadRequest(new { error = "Only PDF, JPEG and PNG files are allowed." });
            if (request.SizeBytes > 25 * 1024 * 1024)
                return Results.BadRequest(new { error = "Files must be smaller than 25 MB." });

            if (!await db.Appointments.AnyAsync(a => a.Id == appointmentId))
                return Results.NotFound();

            var file = new SessionFile
            {
                AppointmentId = appointmentId,
                FileName = request.FileName,
                StorageUrl = request.StorageUrl,
                ContentType = request.ContentType,
                SizeBytes = request.SizeBytes
            };
            db.Files.Add(file);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/appointments/{appointmentId}/files/{file.Id}", file);
        });
    }
}

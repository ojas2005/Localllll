using Localll.Contracts;
using Localll.User.API.Data;
using Localll.User.API.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Localll.User.API.Consumers;

/// <summary>Creates the customer profile when the Identity service registers a user.</summary>
public class UserRegisteredConsumer(UserDbContext db, ILogger<UserRegisteredConsumer> logger)
    : IConsumer<UserRegisteredEvent>
{
    public async Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        var message = context.Message;
        var exists = await db.Profiles.AnyAsync(p => p.Id == message.UserId);
        if (exists) return; // idempotent — RabbitMQ redeliveries are safe

        db.Profiles.Add(new CustomerProfile
        {
            Id = message.UserId,
            Email = message.Email,
            FullName = message.FullName
        });
        await db.SaveChangesAsync();
        logger.LogInformation("Profile created for user {UserId}", message.UserId);
    }
}

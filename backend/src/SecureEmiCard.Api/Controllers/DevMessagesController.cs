using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureEmiCard.Infrastructure.Notifications;

namespace SecureEmiCard.Api.Controllers;

/// <summary>
/// DEVELOPMENT ONLY - the "phone" of the message simulator: the SMS and e-mails the app sent, including
/// one-time codes. It is anonymous because the sign-in code is needed before you are signed in, so it
/// answers 404 in every other environment (and Program.cs refuses to start there with the simulator anyway).
/// </summary>
[ApiController]
[Route("api/dev/messages")]
[AllowAnonymous]
public class DevMessagesController : ControllerBase
{
    private readonly DevMessageOutbox _outbox;
    private readonly IWebHostEnvironment _environment;

    public DevMessagesController(DevMessageOutbox outbox, IWebHostEnvironment environment)
    {
        _outbox = outbox;
        _environment = environment;
    }

    /// <summary>Newest first.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<DevMessage>> Get([FromQuery] int take = 30)
        => _environment.IsDevelopment() ? Ok(_outbox.Latest(take)) : NotFound();
}

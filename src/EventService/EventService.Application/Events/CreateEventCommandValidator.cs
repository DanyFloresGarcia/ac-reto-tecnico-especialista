using FluentValidation;

namespace EventService.Application.Events;

// Mirrors Event.Create's domain invariants as a fail-fast, user-friendly second barrier
// (Constitucion §7.5) — spec FR-002.
public class CreateEventCommandValidator : AbstractValidator<CreateEventCommand>
{
    public CreateEventCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Location).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Date).NotEmpty();
        RuleFor(x => x.Zones).NotEmpty().WithMessage("El evento debe tener al menos una zona.");

        RuleForEach(x => x.Zones).ChildRules(zone =>
        {
            zone.RuleFor(z => z.Name).NotEmpty();
            zone.RuleFor(z => z.Capacity).GreaterThan(0);
            zone.RuleFor(z => z.Price).GreaterThanOrEqualTo(0);
        });
    }
}

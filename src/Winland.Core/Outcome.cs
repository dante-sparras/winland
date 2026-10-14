namespace Winland;

public readonly record struct ClientId(long Value);

public sealed record Client(ClientId Id, WindowId Window);

public readonly record struct Placement(ClientId Client, Rectangle Bounds);

public sealed record State(IReadOnlyList<Client> Clients);

public sealed record Outcome(IReadOnlyList<Placement> Placements, State State);

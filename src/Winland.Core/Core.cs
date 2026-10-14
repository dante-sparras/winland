namespace Winland;

public sealed class Core
{
    public Outcome Handle(Input input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input switch
        {
            WindowAppeared appeared => Place(appeared),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "No handler for this input."),
        };
    }

    private static Outcome Place(WindowAppeared appeared)
    {
        var client = new Client(new ClientId(appeared.Window.Value), appeared.Window);
        return new Outcome(
            [new Placement(client.Id, appeared.WorkArea)],
            new State([client]));
    }
}

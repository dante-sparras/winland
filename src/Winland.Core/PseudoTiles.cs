namespace Winland;

/// <summary>
/// Pseudotiling, kept once for every tiled Layout. Winland keeps it on the window target and applies it
/// after that Layout's tile, its gaps and its aspect padding, and before the border. A pseudo Client is centered in
/// the tile at its natural size, or scaled down to fit while keeping its aspect. The natural size is the size the
/// window asked for, and 1280 by 720 when it has none.
/// </summary>
internal sealed class PseudoTiles
{
    private readonly HashSet<ClientId> _on = [];

    public bool IsOn(ClientId id) => _on.Contains(id);

    public void Toggle(ClientId id)
    {
        if (!_on.Add(id))
            _on.Remove(id);
    }

    public void Forget(ClientId id) => _on.Remove(id);

    public void Hand(ClientId from, ClientId to) => SlotHandover.Move(_on, from, to);

    /// <summary>
    /// The tile a Client is shown in. A pseudo Client is centered at its natural size first, then the border is taken
    /// off. Any other Client keeps <paramref name="tile"/> and only loses the border.
    /// </summary>
    public Rectangle Frame(ClientId? id, Rectangle tile, Rectangle natural, int border)
    {
        var framed = id is ClientId client && IsOn(client) ? Fit(tile, Size(natural)) : tile;
        var width = Math.Max(0, framed.Width - border - border);
        var height = Math.Max(0, framed.Height - border - border);
        return new Rectangle(framed.X + border, framed.Y + border, width, height);
    }

    private static (int Width, int Height) Size(Rectangle natural) =>
        natural.Width > 0 && natural.Height > 0 ? (natural.Width, natural.Height) : (1280, 720);

    // The same scale Winland uses: shrink the size to the tile on the axis that overflows, then center it.
    private static Rectangle Fit(Rectangle tile, (int Width, int Height) size)
    {
        var width = (double)size.Width;
        var height = (double)size.Height;
        if (width > tile.Width || height > tile.Height)
        {
            var scale = 1.0;
            if (width > tile.Width)
                scale = tile.Width / width;
            if (height * scale > tile.Height)
                scale = tile.Height / height;

            width = Math.Round(width * scale, MidpointRounding.AwayFromZero);
            height = Math.Round(height * scale, MidpointRounding.AwayFromZero);
        }

        var placedWidth = Math.Max(0, (int)width);
        var placedHeight = Math.Max(0, (int)height);
        return new Rectangle(
            tile.X + ((tile.Width - placedWidth) / 2),
            tile.Y + ((tile.Height - placedHeight) / 2),
            placedWidth,
            placedHeight);
    }
}

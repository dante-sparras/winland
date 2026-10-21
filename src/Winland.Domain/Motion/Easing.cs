namespace Winland.Domain;

/// <summary>
/// How an animation travels: <see cref="Ease"/> takes the fraction of its time passed, from 0 to 1, and gives the
/// fraction of the way travelled, which can leave that range before it lands on 1. A <see cref="Bezier"/> or a
/// <see cref="SpringCurve"/>.
/// </summary>
public abstract record Easing
{
    public abstract double Ease(double x);
}

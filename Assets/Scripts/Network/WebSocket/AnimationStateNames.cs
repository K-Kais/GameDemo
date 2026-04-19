using System;

namespace GameDemo.Network
{
public static class AnimationStateNames
{
    public const string Idle = "Idle";
    public const string Dead = "Dead";
    public const string Walk = "Walk";
    public const string Attack = "Attack";

    public static bool IsDead(string? state)
    {
        return string.Equals(state, Dead, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
            {
                return Idle;
            }

        if (string.Equals(state, Walk, StringComparison.OrdinalIgnoreCase))
        {
            return Walk;
        }

            if (string.Equals(state, Attack, StringComparison.OrdinalIgnoreCase))
            {
                return Attack;
            }

            if (string.Equals(state, Idle, StringComparison.OrdinalIgnoreCase))
            {
                return Idle;
            }

        if (string.Equals(state, Dead, StringComparison.OrdinalIgnoreCase))
        {
            return Dead;
        }

        return Idle;
    }
}
}

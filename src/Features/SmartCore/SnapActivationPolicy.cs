internal static class SnapActivationPolicy
{
    public static bool IsActive(bool isAimSnapOverrideWeapon, SnapMode snapMode, bool firePressed, bool aimPressed)
    {
        if (isAimSnapOverrideWeapon)
        {
            return firePressed || aimPressed;
        }

        return snapMode switch
        {
            SnapMode.Fire => firePressed,
            SnapMode.AimAndFire => aimPressed || firePressed,
            _ => throw new InvalidOperationException($"Unhandled snap mode: {snapMode}")
        };
    }
}

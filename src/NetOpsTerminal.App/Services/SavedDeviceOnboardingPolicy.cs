namespace NetOpsTerminal.App.Services;

internal enum SavedDeviceOnboardingStep
{
    CreateCredential,
    CreateDevice,
}

internal static class SavedDeviceOnboardingPolicy
{
    public static SavedDeviceOnboardingStep DetermineNextStep(int credentialCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(credentialCount);
        return credentialCount == 0
            ? SavedDeviceOnboardingStep.CreateCredential
            : SavedDeviceOnboardingStep.CreateDevice;
    }
}

using NetOpsTerminal.App.Services;

namespace NetOpsTerminal.Ui.Tests;

public sealed class SavedDeviceOnboardingPolicyTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void DetermineNextStepRoutesTheSavedDevicePrerequisite(
        int credentialCount,
        bool expectsCredentialCreation)
    {
        SavedDeviceOnboardingStep actual = SavedDeviceOnboardingPolicy.DetermineNextStep(
            credentialCount);
        Assert.Equal(
            expectsCredentialCreation,
            actual == SavedDeviceOnboardingStep.CreateCredential);
    }

    [Fact]
    public void DetermineNextStepRejectsInvalidCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SavedDeviceOnboardingPolicy.DetermineNextStep(-1));
    }
}

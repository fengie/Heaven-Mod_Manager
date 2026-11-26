using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Automation;
using MhwModManager.IntegrationTests;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>Pure failure-classification tests run even without hosted UI automation.</summary>
public sealed class InstalledWpfAutomationRetryTests
{
    [Theory]
    [InlineData("Could not open the process token")]
    [InlineData("The target element corresponds to UI that is no longer available (for example, the parent window has closed).")]
    public void Retry_only_observed_transient_provider_failures(string message)
    {
        Assert.True(InstalledWpfModLifecycleE2ETests.IsTransientAutomationQueryFailure(
            new Win32Exception(message)));
        Assert.True(InstalledWpfModLifecycleE2ETests.IsTransientAutomationQueryFailure(
            new InvalidOperationException(message)));
    }

    [Fact]
    public void Security_exception_from_denied_transient_token_query_is_retriable()
    {
        Assert.True(InstalledWpfModLifecycleE2ETests.IsTransientAutomationQueryFailure(
            new SecurityException("Could not open the process token")));
    }

    [Fact]
    public void Unrelated_application_security_cancel_and_io_failures_are_not_hidden()
    {
        foreach (var failure in new Exception[]
        {
            new Win32Exception("Unrelated process failure"),
            new SecurityException("Access denied to game directory"),
            new InvalidOperationException("Unsupported game root"),
            new OperationCanceledException(),
            new IOException("The game file changed externally")
        })
            Assert.False(InstalledWpfModLifecycleE2ETests.IsTransientAutomationQueryFailure(failure));
    }

    [Fact]
    public void Workflow_tab_disabled_during_selection_requires_fresh_successful_attempt()
    {
        var attempts = 0;
        bool SelectAndVerify()
        {
            attempts++;
            if (attempts == 1)
                throw new ElementNotEnabledException("Tab disabled during selection");
            return attempts == 3;
        }

        Assert.False(InstalledWpfModLifecycleE2ETests.TrySelectWorkflowTab(SelectAndVerify));
        Assert.False(InstalledWpfModLifecycleE2ETests.TrySelectWorkflowTab(SelectAndVerify));
        Assert.True(InstalledWpfModLifecycleE2ETests.TrySelectWorkflowTab(SelectAndVerify));
        Assert.Equal(3, attempts);
    }

    [Fact]
    public void Workflow_tab_selection_does_not_hide_unrelated_failures()
    {
        Assert.Throws<InvalidOperationException>(() =>
            InstalledWpfModLifecycleE2ETests.TrySelectWorkflowTab(() =>
                throw new InvalidOperationException("Unexpected selection failure")));
        Assert.Throws<OperationCanceledException>(() =>
            InstalledWpfModLifecycleE2ETests.TrySelectWorkflowTab(() =>
                throw new OperationCanceledException()));
        Assert.False(InstalledWpfModLifecycleE2ETests.IsTransientAutomationQueryFailure(
            new ElementNotEnabledException("Unrelated disabled control")));
    }

    [Fact]
    public void PreInputFocusReacquiresAReboundControlWithoutReplayingAnAction()
    {
        var generation = 0;
        var focusedGeneration = -1;
        var focusAttempts = 0;
        var inputCount = 0;
        int? FindReady() => generation;
        bool IsFocused(int candidate) => focusedGeneration == candidate && generation == candidate;
        void Focus(int candidate)
        {
            focusAttempts++;
            focusedGeneration = candidate;
            if (focusAttempts == 1) generation++; // WPF replaced the original control.
        }

        Assert.False(InstalledWpfModLifecycleE2ETests.TryAcquireCurrentFocus<int>(
            FindReady, IsFocused, Focus));
        Assert.Equal(0, inputCount);
        Assert.True(InstalledWpfModLifecycleE2ETests.TryAcquireCurrentFocus<int>(
            FindReady, IsFocused, Focus));
        inputCount++; // The caller sends its single activation only after verification.
        Assert.True(InstalledWpfModLifecycleE2ETests.TryAcquireCurrentFocus<int>(
            FindReady, IsFocused, Focus));
        Assert.Equal(2, focusAttempts);
        Assert.Equal(1, inputCount);
    }

    [Fact]
    public void PreInputFocusRetriesUnavailableAndDisabledControlsButNotApplicationFailures()
    {
        int? Missing() => null;
        int? Ready() => 7;
        Assert.False(InstalledWpfModLifecycleE2ETests.TryAcquireCurrentFocus<int>(
            Missing, _ => false, _ => throw new InvalidOperationException("No target may be focused")));
        Assert.False(InstalledWpfModLifecycleE2ETests.TryAcquireCurrentFocus<int>(
            Ready, _ => false, _ => throw new ElementNotEnabledException("Rebinding")));
        Assert.False(InstalledWpfModLifecycleE2ETests.TryAcquireCurrentFocus<int>(
            Ready, _ => false, _ => throw new ElementNotAvailableException("Rebinding")));
        Assert.Throws<InvalidOperationException>(() =>
            InstalledWpfModLifecycleE2ETests.TryAcquireCurrentFocus<int>(
                Ready, _ => false, _ => throw new InvalidOperationException("Unexpected failure")));
        Assert.Throws<OperationCanceledException>(() =>
            InstalledWpfModLifecycleE2ETests.TryAcquireCurrentFocus<int>(
                Ready, _ => false, _ => throw new OperationCanceledException()));
    }

    [Fact]
    public void Com_unavailable_elements_can_be_retried_while_hosted_ui_rebinds()
    {
        var providerFailure = Marshal.GetExceptionForHR(unchecked((int)0x80004005))
            ?? throw new InvalidOperationException("Expected a COM provider failure for the test.");
        Assert.IsType<COMException>(providerFailure);
        Assert.True(InstalledWpfModLifecycleE2ETests.IsTransientAutomationQueryFailure(providerFailure));
    }
}

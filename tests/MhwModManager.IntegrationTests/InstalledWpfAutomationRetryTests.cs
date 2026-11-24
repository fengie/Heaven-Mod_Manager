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
    public void Com_unavailable_elements_can_be_retried_while_hosted_ui_rebinds()
    {
        var providerFailure = Marshal.GetExceptionForHR(unchecked((int)0x80004005))
            ?? throw new InvalidOperationException("Expected a COM provider failure for the test.");
        Assert.IsType<COMException>(providerFailure);
        Assert.True(InstalledWpfModLifecycleE2ETests.IsTransientAutomationQueryFailure(providerFailure));
    }
}

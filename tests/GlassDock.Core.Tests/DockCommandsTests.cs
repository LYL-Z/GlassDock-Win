using GlassDock.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GlassDock.Core.Tests;

[TestClass]
public sealed class DockCommandsTests
{
    [TestMethod]
    [DataRow(DockCommandKind.Launch)]
    [DataRow(DockCommandKind.Activate)]
    [DataRow(DockCommandKind.Open)]
    public void CommandContractRetainsTargetIdentityAndItemId(DockCommandKind kind)
    {
        var item = new DockProjectItem(
            DockItemId.FromGuid(Guid.Parse("66666666-6666-6666-6666-666666666666")),
            DockProjectItemKind.Application,
            DockSection.PinnedApplications,
            "Editor",
            DockTargetIdentity.Create(DockIdentityScheme.WindowsAppUserModelId, "Contoso.Editor"));

        var command = DockCommand.For(kind, item);

        Assert.AreEqual(kind, command.Kind);
        Assert.AreEqual(item.Id, command.ItemId);
        Assert.AreEqual(item.TargetIdentity, command.TargetIdentity);
    }

    [TestMethod]
    public void ResultDistinguishesSuccessAndSystemPolicyFailures()
    {
        Assert.IsTrue(new DockCommandResult(DockCommandOutcome.Completed).IsCompleted);
        Assert.IsFalse(new DockCommandResult(DockCommandOutcome.ForegroundActivationDenied, "ForegroundPolicy").IsCompleted);
        Assert.IsFalse(new DockCommandResult(DockCommandOutcome.AccessDenied, "AccessDenied").IsCompleted);
        Assert.IsFalse(new DockCommandResult(DockCommandOutcome.Unsupported, "NoProvider").IsCompleted);
    }

    [TestMethod]
    public void InvalidCommandCannotBeConstructed()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DockCommand(
            (DockCommandKind)99,
            DockItemId.FromGuid(Guid.NewGuid()),
            DockTargetIdentity.Create(DockIdentityScheme.WindowsAppUserModelId, "Contoso.Editor")));
    }

    [TestMethod]
    public void InvalidCommandOutcomeCannotBeReported()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DockCommandResult((DockCommandOutcome)99));
    }
}

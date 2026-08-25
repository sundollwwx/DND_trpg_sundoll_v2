using NUnit.Framework;
using Sundoll.Application;
using Sundoll.Core;

namespace Sundoll.Tests.EditMode
{
    public sealed class M6RuleSeamTests
    {
        [Test]
        public void RuleSeamSupportsAllowDenyReplaceAndAppend()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var pipeline = new M6RulePipeline();
            pipeline.Add(new M6DelegateRule((state, command) =>
                command.CommandType == "M5.SetFog"
                    ? M6RuleDecision.Deny("主持人规则禁止修改迷雾")
                    : M6RuleDecision.Allow("基础规则通过")));

            var denied = pipeline.Evaluate(bus.State, new M5SetFogCommand("m6-deny", bus.State.revision, "map-m1", 1, 1, false));
            Assert.That(denied.Allowed, Is.False);
            Assert.That(denied.Diagnostic, Does.Contain("禁止"));

            var replacement = new M5RenameMapCommand("m6-replacement", bus.State.revision, "map-m1", "规则重命名");
            var replacePipeline = new M6RulePipeline();
            replacePipeline.Add(new M6DelegateRule((state, command) => M6RuleDecision.Replace(replacement, "替换为主持人命名")));
            var replaced = replacePipeline.Evaluate(bus.State, new M5RenameMapCommand("m6-original", bus.State.revision, "map-m1", "原名称"));
            Assert.That(replaced.Allowed, Is.True);
            Assert.That(replaced.EffectiveCommand.CommandId, Is.EqualTo("m6-replacement"));

            var appendPipeline = new M6RulePipeline();
            appendPipeline.Add(new M6DelegateRule((state, command) => M6RuleDecision.Append(new[]
            {
                new M5SetFogCommand("m6-appended", state.revision, "map-m1", 1, 1, true)
            })));
            var appended = appendPipeline.Evaluate(bus.State, replacement);
            Assert.That(appended.Allowed, Is.True);
            Assert.That(appended.AppendedCommands, Has.Count.EqualTo(1));
        }
    }
}

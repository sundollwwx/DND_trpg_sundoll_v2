using System;
using System.Collections.Generic;
using Sundoll.Core;

namespace Sundoll.Application
{
    /// <summary>
    /// M6A is deliberately an independent seam. Rules can be proven and
    /// composed without changing the M1 command model, persistence envelope,
    /// or the Workbench's main command path.
    /// </summary>
    public enum M6RuleDecisionKind
    {
        Allow = 0,
        Deny = 1,
        Replace = 2,
        Append = 3
    }

    public sealed class M6RuleDecision
    {
        private M6RuleDecision(M6RuleDecisionKind kind, string reason, M1Command replacement, IEnumerable<M1Command> appended)
        {
            Kind = kind;
            Reason = reason ?? string.Empty;
            Replacement = replacement;
            AppendedCommands = appended == null ? new List<M1Command>() : new List<M1Command>(appended);
        }

        public M6RuleDecisionKind Kind { get; }
        public string Reason { get; }
        public M1Command Replacement { get; }
        public IReadOnlyList<M1Command> AppendedCommands { get; }

        public static M6RuleDecision Allow(string reason = null)
        {
            return new M6RuleDecision(M6RuleDecisionKind.Allow, reason, null, null);
        }

        public static M6RuleDecision Deny(string reason)
        {
            return new M6RuleDecision(M6RuleDecisionKind.Deny, reason, null, null);
        }

        public static M6RuleDecision Replace(M1Command replacement, string reason = null)
        {
            if (replacement == null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            return new M6RuleDecision(M6RuleDecisionKind.Replace, reason, replacement, null);
        }

        public static M6RuleDecision Append(IEnumerable<M1Command> commands, string reason = null)
        {
            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            return new M6RuleDecision(M6RuleDecisionKind.Append, reason, null, commands);
        }
    }

    public interface IM6Rule
    {
        M6RuleDecision Evaluate(M1WorldState state, M1Command command);
    }

    public sealed class M6DelegateRule : IM6Rule
    {
        private readonly Func<M1WorldState, M1Command, M6RuleDecision> evaluator;

        public M6DelegateRule(Func<M1WorldState, M1Command, M6RuleDecision> evaluator)
        {
            this.evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        }

        public M6RuleDecision Evaluate(M1WorldState state, M1Command command)
        {
            return evaluator(state, command) ?? M6RuleDecision.Allow();
        }
    }

    public sealed class M6RuleEvaluation
    {
        public bool Allowed { get; internal set; }
        public string Diagnostic { get; internal set; }
        public M1Command EffectiveCommand { get; internal set; }
        public List<M1Command> AppendedCommands { get; } = new List<M1Command>();
        public List<string> Trace { get; } = new List<string>();
    }

    public sealed class M6RulePipeline
    {
        private readonly List<IM6Rule> rules = new List<IM6Rule>();

        public IReadOnlyList<IM6Rule> Rules => rules;

        public void Add(IM6Rule rule)
        {
            if (rule == null)
            {
                throw new ArgumentNullException(nameof(rule));
            }

            rules.Add(rule);
        }

        public M6RuleEvaluation Evaluate(M1WorldState state, M1Command command)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            var result = new M6RuleEvaluation
            {
                Allowed = true,
                EffectiveCommand = command,
                Diagnostic = string.Empty
            };

            foreach (var rule in rules)
            {
                var decision = rule.Evaluate(state, result.EffectiveCommand) ?? M6RuleDecision.Allow();
                result.Trace.Add(decision.Kind + (string.IsNullOrWhiteSpace(decision.Reason) ? string.Empty : ": " + decision.Reason));
                switch (decision.Kind)
                {
                    case M6RuleDecisionKind.Deny:
                        result.Allowed = false;
                        result.Diagnostic = string.IsNullOrWhiteSpace(decision.Reason) ? "Rule denied the command." : decision.Reason;
                        return result;
                    case M6RuleDecisionKind.Replace:
                        result.EffectiveCommand = decision.Replacement;
                        break;
                    case M6RuleDecisionKind.Append:
                        result.AppendedCommands.AddRange(decision.AppendedCommands);
                        break;
                }
            }

            return result;
        }
    }
}

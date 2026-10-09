using System.Collections.Generic;
using System.Linq;
using BuildAR.Data;

namespace BuildAR.Assembly
{
    /// <summary>Green / yellow / red in the UI.</summary>
    public enum CompatibilityStatus { Compatible, Warning, Incompatible }

    public class CompatibilityResult
    {
        public ComponentDefinitionSO componentA;
        public ComponentDefinitionSO componentB;
        public CompatibilityStatus status;
        public string message;
        /// <summary>False when the two parts share no rules (e.g. RAM vs PSU) — usually not worth showing.</summary>
        public bool hasSharedRules;
    }

    public static class CompatibilityChecker
    {
        /// <summary>A PSU within this fraction above a part's minimum is flagged "check required".</summary>
        const float WattageHeadroom = 0.10f;

        public static List<CompatibilityResult> CheckBuild(IEnumerable<ComponentDefinitionSO> selectedComponents)
        {
            var list = selectedComponents.Where(c => c != null).ToList();
            var results = new List<CompatibilityResult>();
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    results.Add(CheckPair(list[i], list[j]));
            return results;
        }

        public static CompatibilityResult CheckPair(ComponentDefinitionSO a, ComponentDefinitionSO b)
        {
            bool anyChecked = false, allSatisfied = true, tight = false;
            var failed = new List<string>();
            Evaluate(a, b, ref anyChecked, ref allSatisfied, ref tight, failed);
            Evaluate(b, a, ref anyChecked, ref allSatisfied, ref tight, failed);

            var result = new CompatibilityResult { componentA = a, componentB = b, hasSharedRules = anyChecked };
            if (!anyChecked)
            {
                result.status = CompatibilityStatus.Warning;
                result.message = $"No shared specs between {a.displayName} and {b.displayName} — check manually.";
            }
            else if (!allSatisfied)
            {
                result.status = CompatibilityStatus.Incompatible;
                result.message = $"{a.displayName} and {b.displayName} don't match: {string.Join(", ", failed.Distinct())}.";
            }
            else if (tight)
            {
                result.status = CompatibilityStatus.Warning;
                result.message = "Power supply is close to the minimum — check the total system wattage.";
            }
            else
            {
                result.status = CompatibilityStatus.Compatible;
                result.message = $"{a.displayName} and {b.displayName} are compatible.";
            }
            return result;
        }

        static void Evaluate(ComponentDefinitionSO requirer, ComponentDefinitionSO provider,
                             ref bool anyChecked, ref bool allSatisfied, ref bool tight, List<string> failed)
        {
            foreach (var rule in requirer.compatibilityRules)
            {
                if (rule.ruleType == CompatibilityRule.RuleType.WattageProvided) continue; // checked from the other side

                if (rule.ruleType == CompatibilityRule.RuleType.InterfaceType)
                {
                    // An interface is what a part plugs into, and only the motherboard offers them — possibly several
                    // (M.2 NVMe and SATA). Two parts that plug into different things, like an M.2 and a SATA drive,
                    // aren't a mismatch, so they're never compared with each other.
                    if (requirer.category == ComponentCategory.Motherboard || provider.category != ComponentCategory.Motherboard) continue;
                    var offered = provider.compatibilityRules.Where(r => r.ruleType == CompatibilityRule.RuleType.InterfaceType).ToList();
                    if (offered.Count == 0) continue;
                    anyChecked = true;
                    if (!offered.Any(rule.IsSatisfiedBy)) { allSatisfied = false; failed.Add(rule.Label.ToLowerInvariant()); }
                    continue;
                }

                var other = provider.compatibilityRules.FirstOrDefault(r => r.ruleType == rule.CounterpartType);
                if (other == null) continue;

                anyChecked = true;
                if (!rule.IsSatisfiedBy(other)) { allSatisfied = false; failed.Add(rule.Label.ToLowerInvariant()); }
                else if (rule.ruleType == CompatibilityRule.RuleType.WattageMinimum && other.intValue < rule.intValue * (1f + WattageHeadroom))
                    tight = true;
            }
        }
    }
}

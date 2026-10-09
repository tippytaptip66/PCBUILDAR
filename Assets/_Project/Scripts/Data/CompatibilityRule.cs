namespace BuildAR.Data
{
    [System.Serializable]
    public class CompatibilityRule
    {
        // Append new values at the end so existing assets keep their serialized ints.
        public enum RuleType { SocketType, RamGeneration, WattageMinimum, FormFactor, InterfaceType, WattageProvided }

        public RuleType ruleType;
        public string stringValue;
        public int intValue;

        /// <summary>
        /// WattageMinimum (what a part needs) is checked against WattageProvided (what a PSU delivers).
        /// Every other rule type is matched against the same type on the other component.
        /// </summary>
        public RuleType CounterpartType => ruleType == RuleType.WattageMinimum ? RuleType.WattageProvided : ruleType;

        public bool IsSatisfiedBy(CompatibilityRule other)
        {
            if (other == null) return true;
            switch (ruleType)
            {
                case RuleType.WattageMinimum: return other.intValue >= intValue;
                default: return string.Equals(stringValue, other.stringValue, System.StringComparison.OrdinalIgnoreCase);
            }
        }

        public string Label
        {
            get
            {
                switch (ruleType)
                {
                    case RuleType.SocketType: return "Socket";
                    case RuleType.RamGeneration: return "Memory type";
                    case RuleType.WattageMinimum: return "Needs PSU of at least";
                    case RuleType.WattageProvided: return "Power output";
                    case RuleType.FormFactor: return "Form factor";
                    default: return "Interface";
                }
            }
        }

        public string Value => ruleType == RuleType.WattageMinimum || ruleType == RuleType.WattageProvided ? $"{intValue} W" : stringValue;
    }
}

namespace BuildAR.Data
{
    public static class BadgeCatalog
    {
        public const string FirstScan = "first_scan";
        public const string CableExpert = "cable_expert";
        public const string RamInstaller = "ram_installer";
        public const string CompatibilityChecker = "compatibility_checker";
        public const string SafetyFirst = "safety_first";
        public const string FullBuild = "full_build";

        public struct Badge
        {
            public string id, title, description, icon;
            public Badge(string id, string title, string description, string icon)
            { this.id = id; this.title = title; this.description = description; this.icon = icon; }
        }

        public static readonly Badge[] All =
        {
            new Badge(FirstScan, "First Component Scanned", "Recognize a part with the AR scanner", "scan"),
            new Badge(CableExpert, "Cable Connector Expert", "Connect every power and front-panel cable", "cable"),
            new Badge(RamInstaller, "RAM Installer", "Install both memory sticks in the right slots", "ram"),
            new Badge(CompatibilityChecker, "Compatibility Checker", "Check compatibility for 3 components", "compat"),
            new Badge(SafetyFirst, "Safety First", "Review a safety tip before building", "shield"),
            new Badge(FullBuild, "Full Build Completed", "Finish every step of a virtual build", "trophy"),
        };
    }
}

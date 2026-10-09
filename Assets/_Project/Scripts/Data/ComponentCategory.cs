namespace BuildAR.Data
{
    public enum ComponentCategory
    {
        CPU, Motherboard, RAM, Storage, GraphicsCard,
        PowerSupply, Cooling, Case, CableConnector, InputOutput
    }

    public static class ComponentCategoryExtensions
    {
        public static string DisplayName(this ComponentCategory c)
        {
            switch (c)
            {
                case ComponentCategory.GraphicsCard: return "Graphics Card";
                case ComponentCategory.PowerSupply: return "Power Supply";
                case ComponentCategory.Cooling: return "Cooling";
                case ComponentCategory.CableConnector: return "Cables";
                case ComponentCategory.InputOutput: return "Input / Output";
                default: return c.ToString();
            }
        }

        public static string LongName(this ComponentCategory c)
        {
            switch (c)
            {
                case ComponentCategory.CPU: return "Central Processing Unit";
                case ComponentCategory.Motherboard: return "Main Circuit Board";
                case ComponentCategory.RAM: return "Random Access Memory";
                case ComponentCategory.Storage: return "Storage Drive";
                case ComponentCategory.GraphicsCard: return "Graphics Processing Unit";
                case ComponentCategory.PowerSupply: return "Power Supply Unit";
                case ComponentCategory.Cooling: return "Cooling System";
                case ComponentCategory.Case: return "Computer Case";
                case ComponentCategory.CableConnector: return "Cables & Connectors";
                default: return "Input / Output Devices";
            }
        }

        /// <summary>Icon name understood by BuildAR.UI.LineIcon.</summary>
        public static string IconName(this ComponentCategory c)
        {
            switch (c)
            {
                case ComponentCategory.CPU: return "cpu";
                case ComponentCategory.Motherboard: return "motherboard";
                case ComponentCategory.RAM: return "ram";
                case ComponentCategory.Storage: return "storage";
                case ComponentCategory.GraphicsCard: return "gpu";
                case ComponentCategory.PowerSupply: return "psu";
                case ComponentCategory.Cooling: return "fan";
                case ComponentCategory.Case: return "case";
                case ComponentCategory.CableConnector: return "cable";
                default: return "keyboard";
            }
        }
    }
}

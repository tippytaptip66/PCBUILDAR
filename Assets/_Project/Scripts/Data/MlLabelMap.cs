using System.Collections.Generic;
using System.Text;

namespace BuildAR.Data
{
    /// <summary>
    /// Maps the class names used by outside models and datasets onto this app's component labels, so a model
    /// trained on someone else's data works without renaming anything.
    ///
    /// A name is normalised first (case, spaces, hyphens, plurals), then looked up here. The table covers the
    /// Roboflow Universe "PC Parts Detection" classes and the usual spellings; anything else can be added on the
    /// component itself with ComponentDefinitionSO.mlAliases.
    ///
    /// Parts the app has no lesson for (optical drives, for example) are deliberately absent — an unmapped
    /// detection is simply ignored by the scanner.
    /// </summary>
    public static class MlLabelMap
    {
        static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            // CPU
            { "processor", "cpu" }, { "cpu_chip", "cpu" }, { "central_processing_unit", "cpu" },
            // Cooler
            { "cpu_cooler", "cooler" }, { "cpu_fan", "cooler" }, { "heatsink", "cooler" },
            { "heat_sink", "cooler" }, { "air_cooler", "cooler" }, { "aio", "cooler" }, { "radiator", "cooler" },
            { "cooling", "cooler" }, { "fan", "cooler" },
            // Motherboard (slots and sockets are parts of the board)
            { "mainboard", "motherboard" }, { "main_board", "motherboard" }, { "mobo", "motherboard" },
            { "gpu_slot", "motherboard" }, { "pcie_slot", "motherboard" }, { "pci_slot", "motherboard" },
            { "ram_slot", "motherboard" }, { "dimm_slot", "motherboard" }, { "cpu_socket", "motherboard" },
            // Memory
            { "ram_stick", "ram" }, { "ram_module", "ram" }, { "memory", "ram" }, { "memory_stick", "ram" },
            { "dimm", "ram" }, { "ddr", "ram" }, { "ddr3", "ram" }, { "ddr4", "ram" }, { "ddr5", "ram" },
            // Storage
            { "disk", "ssd" }, { "disk_drive", "ssd" }, { "hard_drive", "ssd" }, { "hard_disk", "ssd" }, { "hdd", "ssd" },
            { "storage", "ssd" }, { "nvme", "ssd" }, { "m2", "ssd" }, { "m2_ssd", "ssd" }, { "sata_ssd", "ssd" },
            // Graphics
            { "graphics_card", "gpu" }, { "graphic_card", "gpu" }, { "video_card", "gpu" }, { "vga", "gpu" },
            // Power
            { "power_supply", "psu" }, { "power_supply_unit", "psu" }, { "smps", "psu" },
            // Case
            { "front_panel", "case" }, { "pc_case", "case" }, { "computer_case", "case" },
            { "chassis", "case" }, { "tower", "case" },
            // I/O
            { "rear_io", "io_panel" }, { "io_shield", "io_panel" }, { "io", "io_panel" },
            { "back_panel", "io_panel" }, { "port", "io_panel" }, { "ports", "io_panel" },
            // Cables
            { "power_cable", "cable" }, { "cable_24pin", "cable" }, { "atx_cable", "cable" }, { "wire", "cable" },
        };

        /// <summary>
        /// Lower case, single underscores between words, no trailing plural: "Graphics Card", "graphics-cards"
        /// and the file name "GraphicsCard.fbx" all come out as "graphics_card".
        /// </summary>
        public static string Normalise(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            raw = raw.Trim();
            var sb = new StringBuilder(raw.Length + 4);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsLetterOrDigit(c))
                {
                    // CamelCase is a word break too, so model file names match component names.
                    bool camel = i > 0 && char.IsUpper(c) && (char.IsLower(raw[i - 1]) || char.IsDigit(raw[i - 1]));
                    if (camel && sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            string s = sb.ToString().TrimEnd('_');
            if (s.Length > 3 && s[s.Length - 1] == 's' && s[s.Length - 2] != 's') s = s.Substring(0, s.Length - 1);
            return s;
        }

        /// <summary>The app label an outside class name means, or the normalised name when nothing maps it.</summary>
        public static string Resolve(string raw)
        {
            string key = Normalise(raw);
            return key.Length > 0 && Aliases.TryGetValue(key, out string mapped) ? mapped : key;
        }
    }
}

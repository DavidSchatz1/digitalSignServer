namespace DigitalSignServer.Utils
{
    public class TemplateServiceHelpers
    {
        private static bool IsSignTag(string? tag)
    => !string.IsNullOrWhiteSpace(tag) && tag.Trim().StartsWith("SIGN", StringComparison.OrdinalIgnoreCase);

        private sealed record SignTagInfo(string Group, double? W, double? H);
        private static SignTagInfo ParseSignTag(string raw)
        {
            // SIGN[:group][|w=0.25][|h=0.08]
            string group = "default";
            double? w = null, h = null;

            var s = (raw ?? "").Trim();
            var parts = s.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
            {
                var head = parts[0]; // "SIGN" or "SIGN:group"
                var idx = head.IndexOf(':');
                if (idx >= 0 && idx + 1 < head.Length)
                    group = head[(idx + 1)..].Trim();
            }
            foreach (var p in parts.Skip(1))
            {
                var kv = p.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length == 2)
                {
                    if (kv[0].Equals("w", StringComparison.OrdinalIgnoreCase) && double.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vw))
                        w = vw;
                    if (kv[0].Equals("h", StringComparison.OrdinalIgnoreCase) && double.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vh))
                        h = vh;
                }
            }
            return new SignTagInfo(group, w, h);
        }
    }
}

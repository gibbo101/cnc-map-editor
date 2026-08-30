namespace MobiusEditor.Model
{
    /// <summary>
    /// A map-file entry the active profile could not interpret (an object type it does not know).
    /// Kept verbatim so saving never silently drops content authored for another mod or game.
    /// </summary>
    public sealed class UnknownEntry
    {
        public string Section { get; }
        public string Key { get; }
        public string Value { get; }
        public string Reason { get; }
        public UnknownEntry(string section, string key, string value, string reason) { Section = section; Key = key; Value = value; Reason = reason; }
        public override string ToString() => $"[{Section}] {Key}={Value} ({Reason})";
    }
}

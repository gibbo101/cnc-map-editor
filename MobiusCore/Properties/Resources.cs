using System.Drawing;
using System.IO;
using System.Reflection;

namespace MobiusEditor.Properties
{
    /// <summary>Embedded images and data the core ships with.</summary>
    public static class Resources
    {
        private static Stream Open(string name) => typeof(Resources).Assembly.GetManifestResourceStream("MobiusCore.Resources." + name) ?? throw new FileNotFoundException("Embedded resource missing: " + name);
        private static byte[] Bytes(string name) { using (Stream s = Open(name)) using (MemoryStream ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); } }
        private static Bitmap Image(string name) { using (Stream s = Open(name)) return new Bitmap(s); }

        public static Bitmap Mobius => Image("Mobius.png");
        public static Bitmap RA_Head => Image("ra-head.png");
        public static Bitmap TD_Head => Image("td-head.png");
        public static Bitmap UI_CustomMissionPreviewDefault => Image("UI_CustomMissionPreviewDefault.png");
        public static byte[] n64_th_desert => Bytes("n64_th_desert.nms");
        public static byte[] n64_th_temperate => Bytes("n64_th_temperate.nms");
    }
}

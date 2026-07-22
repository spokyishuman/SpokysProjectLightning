using System;
using System.IO;

namespace SpokysProjectVercel.Services
{
    public static class ManifestPaths
    {
        private static readonly string BaseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpokysPL", "SteamDaddy", "data");

        public static string LuaDir => Path.Combine(BaseDir, "lua");
        public static string ManifestDir => Path.Combine(BaseDir, "manifests");
        public static string KeysDir => Path.Combine(BaseDir, "keys");

        public static void EnsureDirs()
        {
            Directory.CreateDirectory(LuaDir);
            Directory.CreateDirectory(ManifestDir);
            Directory.CreateDirectory(KeysDir);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace KighmuVpnWindows.Utils
{
    public static class ResourceManager
    {
        private const string TAG = "ResourceManager";

        private static readonly List<EmbeddedFile> _manifests = new()
        {
            new EmbeddedFile("xray.exe"),
            new EmbeddedFile("dnstt-client.exe"),
            new EmbeddedFile("plink.exe"),
            new EmbeddedFile("hysteria.exe"),
            new EmbeddedFile("tun2socks.exe"),
            new EmbeddedFile("wintun.dll"),
            new EmbeddedFile("msys-2.0.dll"),
        };

        private static bool _extracted;

        public static void EnsureResources()
        {
            if (_extracted) return;
            AppPaths.EnsureDirectories();
            foreach (var file in _manifests)
            {
                ExtractIfNeeded(file);
            }
            _extracted = true;
        }

        private static void ExtractIfNeeded(EmbeddedFile file)
        {
            string destPath = AppPaths.Bin(file.Name);
            byte[]? embedded = ReadEmbedded(file.Name);
            if (embedded == null)
            {
                KighmuLogger.Warn(TAG, $"Ressource embarquee introuvable: {file.Name} - certains modes peuvent ne pas fonctionner");
                return;
            }

            string embeddedHash = ComputeSha256(embedded);

            if (File.Exists(destPath))
            {
                try
                {
                    byte[] existing = File.ReadAllBytes(destPath);
                    string existingHash = ComputeSha256(existing);
                    if (existingHash == embeddedHash && existing.Length == embedded.Length)
                    {
                        KighmuLogger.Info(TAG, $"Ressource identique (hash OK): {file.Name}");
                        return;
                    }
                    KighmuLogger.Info(TAG, $"Ressource differente, mise a jour: {file.Name}");
                }
                catch (Exception ex)
                {
                    KighmuLogger.Warn(TAG, $"Impossible de verifier {file.Name}: {ex.Message} - remplacement");
                }
            }
            else
            {
                KighmuLogger.Info(TAG, $"Extraction: {file.Name} -> {destPath}");
            }

            try
            {
                File.WriteAllBytes(destPath, embedded);
                KighmuLogger.Info(TAG, $"Extraction reussie: {file.Name} ({embedded.Length} octets, SHA-256={embeddedHash.Substring(0, 16)}...)");
            }
            catch (Exception ex)
            {
                KighmuLogger.Error(TAG, $"Echec extraction {file.Name}: {ex.Message}");
                throw;
            }
        }

        private static byte[]? ReadEmbedded(string name)
        {
            try
            {
                string resourceName = AppPaths.RESOURCE_PREFIX + name;
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) return null;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public static void Cleanup()
        {
            try
            {
                if (Directory.Exists(AppPaths.BinPath))
                {
                    foreach (var f in Directory.GetFiles(AppPaths.BinPath))
                    {
                        try { File.Delete(f); } catch { }
                    }
                    try { Directory.Delete(AppPaths.BinPath); } catch { }
                }
                TryDelete(AppPaths.ConfigPath);
                TryDelete(AppPaths.LogsPath);
                TryDelete(AppPaths.CachePath);
                TryDelete(AppPaths.DataPath);
                TryDelete(AppPaths.PrefsPath);
                TryDelete(AppPaths.BasePath);
                KighmuLogger.Info(TAG, "Nettoyage KingOMVPN termine");
            }
            catch (Exception ex)
            {
                KighmuLogger.Warn(TAG, $"Erreur nettoyage: {ex.Message}");
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch { }
        }

        private class EmbeddedFile
        {
            public string Name { get; }
            public EmbeddedFile(string name) { Name = name; }
        }
    }
}

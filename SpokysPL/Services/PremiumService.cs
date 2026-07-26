using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SpokysProjectVercel.Services
{
    public static class PremiumService
    {
        private const string Prefix = "SPVL-";
        private const int SegmentLen = 4;
        private const int Segments = 3;

        private static readonly byte[] PublicKeyXml = Convert.FromBase64String(
            "PFJTQUtleVZhbHVlPjxNb2R1bHVzPm1RY25ZcTFNWkQxOVhlMkNjZXZsdytkRzZUdW9rTm9JRTNFbGVsdW8zNitpUEtXdU5iQS9od1FMdjJxcGlvUG8wZ0NzWGxCaW41RXUvSmFQaTgzcmRiMGdEeTFMa3YzRHhiYzZ6SXZWTlUyWG12K05SS2txYjdFYWV1eGNwM1NmcnZncEFOL0pmTmM0L2lJNWhTN0dRL2luM3Z2emtuUHg4YkQ4c3lWSVVBQUtuNmJLMW1WU09KVGFsUk04TnRsa0xEdFNCN2I0bWcrTmdjek03cDhXV2NSSmZ6R3gzNVpEdS9QMlBoYys2anRMM0g3NlVISmlPNFdHTDU2VGI4T0dUNk9ub2hXQXc2b1h2LzVMSHF6RXFaM3hvbEFxTzA1bFh6K1VQbzl0Zm8zVi8xb1JCVXozN0RKVDdzdHYrT0NQZzRyaU1CRGpIVXFRdXh6aFlHV0djUT09PC9Nb2R1bHVzPjxFeHBvbmVudD5BUUFCPC9FeHBvbmVudD48L1JTQUtleVZhbHVlPg==");

        private static DataService CreateData() => new();

        public static bool IsPremium => CreateData().LoadSettings()?.IsPremium ?? false;

        public static string CurrentKey => CreateData().LoadSettings()?.PremiumKey ?? string.Empty;

        public static (bool valid, string msg) Activate(string key)
        {
            var cleaned = key?.Trim().ToUpperInvariant() ?? string.Empty;
            if (string.IsNullOrEmpty(cleaned))
                return (false, "Enter a license key.");

            if (!cleaned.StartsWith(Prefix))
                return (false, "Key must start with SPVL-");

            var code = cleaned[Prefix.Length..];
            var parts = code.Split('-');
            if (parts.Length != Segments || parts.Any(p => p.Length != SegmentLen))
                return (false, "Invalid key format. Expected SPVL-XXXX-XXXX-XXXX");

            var joined = string.Join("", parts);
            if (!joined.All(c => "0123456789ABCDEF".Contains(c)))
                return (false, "Key contains invalid characters.");

            if (!ValidateChecksum(joined))
                return (false, "Invalid license key.");

            var svc = CreateData();
            var settings = svc.LoadSettings();
            if (settings != null)
            {
                settings.IsPremium = true;
                settings.PremiumKey = cleaned;
                svc.SaveSettings(settings);
            }

            return (true, "Premium activated! Ads will be hidden.");
        }

        public static (bool valid, string msg) ImportLicenseFile(string filePath)
        {
            try
            {
                var lines = File.ReadAllLines(filePath);
                if (lines.Length < 2)
                    return (false, "Invalid license file.");

                var keyLine = lines[0].Trim();
                var sigLine = lines[1].Trim();

                if (!keyLine.StartsWith(Prefix))
                    return (false, "Invalid license key in file.");

                var code = keyLine[Prefix.Length..];
                var parts = code.Split('-');
                if (parts.Length != Segments || parts.Any(p => p.Length != SegmentLen))
                    return (false, "Invalid key format.");

                var joined = string.Join("", parts);
                if (!joined.All(c => "0123456789ABCDEF".Contains(c)))
                    return (false, "Key contains invalid characters.");

                var sig = Convert.FromBase64String(sigLine);

                using var rsa = RSA.Create();
                rsa.FromXmlString(Encoding.UTF8.GetString(PublicKeyXml));
                var data = Encoding.UTF8.GetBytes(joined);
                if (!rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    return (false, "License signature is invalid.");

                var svc = CreateData();
                var settings = svc.LoadSettings();
                if (settings != null)
                {
                    settings.IsPremium = true;
                    settings.PremiumKey = keyLine;
                    svc.SaveSettings(settings);
                }

                return (true, "License imported! Premium activated.");
            }
            catch (FormatException)
            {
                return (false, "License file is corrupted.");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        public static void Deactivate()
        {
            var svc = CreateData();
            var settings = svc.LoadSettings();
            if (settings != null)
            {
                settings.IsPremium = false;
                settings.PremiumKey = string.Empty;
                svc.SaveSettings(settings);
            }
        }

        private static bool ValidateChecksum(string hex)
        {
            var data = Encoding.UTF8.GetBytes(hex);
            var hash = SHA256.HashData(data);
            var first = hash[0] & 0x0F;
            var check = Convert.ToInt32(hex[^1].ToString(), 16);
            return first == check;
        }

        public static string GenerateKey()
        {
            var randomBytes = new byte[6];
            RandomNumberGenerator.Fill(randomBytes);
            var hex = Convert.ToHexString(randomBytes).ToUpperInvariant();

            var baseHex = hex[..11];
            for (int i = 0; i < 16; i++)
            {
                var last = i.ToString("X");
                var candidate = baseHex + last;
                var data = Encoding.UTF8.GetBytes(candidate);
                var hash = SHA256.HashData(data);
                var check = hash[0] & 0x0F;
                if (check == i)
                    return $"{Prefix}{candidate[..4]}-{candidate[4..8]}-{candidate[8..12]}";
            }
            return $"{Prefix}{hex[..4]}-{hex[4..8]}-{hex[8..12]}";
        }
    }
}

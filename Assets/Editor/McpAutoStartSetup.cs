using System;
using System.IO;
using UnityEditor;

// MCP for Unity köprüsünün editör açılınca kendiliğinden başlaması için varsayılanları ayarlar.
// Claude Code bu köprüye http://127.0.0.1:8080/mcp adresinden bağlanır.
// Ayarı sonradan Window > MCP for Unity > Advanced'den kapatırsan bu script tekrar açmaz.
[InitializeOnLoad]
internal static class McpAutoStartSetup
{
    private const string AppliedKey = "RescueTeam.McpDefaultsApplied";
    private const string UvxPathKey = "MCPForUnity.UvxPath";

    static McpAutoStartSetup()
    {
        if (!EditorPrefs.GetBool(AppliedKey, false))
        {
            EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", true);
            EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", true);
            EditorPrefs.SetBool(AppliedKey, true);
        }

        EnsureUvxPath();
    }

    // Unity, uv kurulmadan önce açılmış bir süreçten başlatılırsa PATH'i eskidir ve uvx'i bulamaz.
    // Kayıtlı yol yoksa veya silinmişse uvx'i Windows'un güncel PATH kaydından bulup yazar.
    private static void EnsureUvxPath()
    {
        string current = EditorPrefs.GetString(UvxPathKey, string.Empty);
        if (!string.IsNullOrEmpty(current) && File.Exists(current))
            return;

        string found = FindInRegistryPath("uvx.exe");
        if (found != null)
            EditorPrefs.SetString(UvxPathKey, found);
    }

    private static string FindInRegistryPath(string exeName)
    {
        // Process PATH'i değil, kayıt defterindeki kullanıcı + sistem PATH'ini okur (her zaman güncel)
        string combined =
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) + ";" +
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine);

        foreach (string dir in combined.Split(';'))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            try
            {
                string candidate = Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim()), exeName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // PATH'te geçersiz karakterli bir girdi varsa atla
            }
        }
        return null;
    }
}

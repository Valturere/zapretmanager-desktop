using System.Collections.Generic;

namespace ZapretManager.Models;

public sealed class TgWsProxyConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1443;
    public string Secret { get; set; } = string.Empty;
    public List<string> DcIpRules { get; set; } = ["2:149.154.167.220", "4:149.154.167.220"];
    public bool EnableCfProxy { get; set; } = true;
    public bool PreferCfProxy { get; set; } = true;
    public string UserCfProxyDomain { get; set; } = string.Empty;
    public bool VerboseLogging { get; set; }
    public bool CheckUpdatesOnStart { get; set; }
    public bool AutoStart { get; set; } = true;
    public int LogMaxMegabytes { get; set; } = 5;
    public int BufferKilobytes { get; set; } = 256;
    public int PoolSize { get; set; } = 4;
}

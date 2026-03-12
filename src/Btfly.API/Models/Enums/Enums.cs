namespace Btfly.API.Models.Enums;

public enum ServerType
{
    Dark = 0,
    Grey = 1,
    Light = 2
}

public enum BanScope
{
    None = 0,
    NodeLevel = 1,   // Banned by a specific node operator
    Global = 2       // Platform-level ban via Cloudlight identity
}

public enum AccountRole
{
    User = 0,
    NodeAdmin = 1,
    PlatformAdmin = 2
}

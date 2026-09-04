namespace Quota.Models;

/// <summary>Источник bonus quota (отдельный слой сверх base allowance).</summary>
public enum BonusSource
{
    None = 0,
    Models = 1,
    Api = 2,
    Unknown = 3
}

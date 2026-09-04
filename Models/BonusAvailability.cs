namespace Quota.Models;

/// <summary>Состояние доступности bonus (Cursor не всегда отдаёт dollar allowance).</summary>
public enum BonusAvailability
{
    /// <summary>Bonus не применим или данных нет.</summary>
    None = 0,

    /// <summary>remainingBonus = true — Cursor сообщает, что bonus доступен.</summary>
    Available = 1,

    /// <summary>Есть bonus usage, но надёжного статуса allowance нет (в т.ч. remainingBonus=false).</summary>
    Unknown = 2,
}

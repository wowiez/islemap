using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

/// <summary>
/// Combines independent web and packet updates. Percentage-only health uses the
/// requested conversion of one kilogram to one maximum HP. Native HP values,
/// when available, remain authoritative.
/// </summary>
internal sealed class DinosaurVitalReadout
{
    private PlayerTelemetry? _player;
    private NpcapWeightSample? _weight;
    private NpcapVitalSample? _packetVitals;

    public void UpdatePlayer(PlayerTelemetry? player) => _player = player;
    public void UpdateWeight(NpcapWeightSample? weight) => _weight = weight;
    public void UpdateVitals(NpcapVitalSample? sample) => _packetVitals = sample;

    public ExactVitals? Vitals(DateTimeOffset now)
    {
        var web = _player?.ExactVitals;
        if (_packetVitals is not { } packet || now < packet.CapturedAt) return web;
        // Native current values are useful even before a capacity keyframe.
        // Do not manufacture a missing maximum from percentages or body mass.
        var native = packet.Vitals;
        return (web ?? new ExactVitals()) with
        {
            Health = native.Health is not null || native.MaxHealth is not null ? native.Health : web?.Health,
            MaxHealth = native.Health is not null || native.MaxHealth is not null ? native.MaxHealth : web?.MaxHealth,
            Hunger = native.Hunger is not null || native.MaxHunger is not null ? native.Hunger : web?.Hunger,
            MaxHunger = native.Hunger is not null || native.MaxHunger is not null ? native.MaxHunger : web?.MaxHunger,
            Thirst = native.Thirst is not null || native.MaxThirst is not null ? native.Thirst : web?.Thirst,
            MaxThirst = native.Thirst is not null || native.MaxThirst is not null ? native.MaxThirst : web?.MaxThirst,
            Stamina = native.Stamina is not null || native.MaxStamina is not null ? native.Stamina : web?.Stamina,
            MaxStamina = native.Stamina is not null || native.MaxStamina is not null ? native.MaxStamina : web?.MaxStamina
        };
    }

    public DinosaurHealthValues Hunger(DateTimeOffset now) => Values(Vitals(now)?.Hunger, Vitals(now)?.MaxHunger, _player?.HungerPercent);
    public DinosaurHealthValues Thirst(DateTimeOffset now) => Values(Vitals(now)?.Thirst, Vitals(now)?.MaxThirst, _player?.ThirstPercent);
    public DinosaurHealthValues Stamina(DateTimeOffset now) => Values(Vitals(now)?.Stamina, Vitals(now)?.MaxStamina, _player?.StaminaPercent);

    private static DinosaurHealthValues Values(double? current, double? maximum, double? fallback)
    {
        var validCurrent = current is { } c && double.IsFinite(c) && c >= 0 ? current : null;
        var validMaximum = maximum is { } m && double.IsFinite(m) && m > 0 ? maximum : null;
        var percent = validCurrent is { } value && validMaximum is { } max
            ? Math.Clamp(value / max * 100d, 0d, 100d)
            : fallback is { } p && double.IsFinite(p) ? (double?)Math.Clamp(p, 0d, 100d) : null;
        return new(validCurrent, validMaximum, percent, false);
    }

    public double? Kilograms(DateTimeOffset now) =>
        _weight is { } weight && double.IsFinite(weight.Kilograms) && weight.Kilograms > 0d &&
        now >= weight.CapturedAt && (weight.ActorConfirmed ||
            now - weight.CapturedAt <= NpcapDinosaurWeightDecoder.PendingSampleLifetime)
            ? weight.Kilograms
            : null;

    public DinosaurHealthValues Health(DateTimeOffset now)
    {
        if (Vitals(now) is { Health: { } current, MaxHealth: { } maximum } &&
            double.IsFinite(current) && current >= 0d && double.IsFinite(maximum) && maximum > 0d)
        {
            return new(current, maximum, Math.Clamp(current / maximum * 100d, 0d, 100d), false);
        }

        if (_packetVitals is { } native && now >= native.CapturedAt && native.Vitals.Health is { } hp)
            return Values(hp, native.Vitals.MaxHealth, _player?.HealthPercent);
        if (_player is null) return default;

        // HealthPercent is already a percentage from the provider. In particular,
        // 0.5 means 0.5%, not a fraction that should become 50%.
        var percent = _player.HealthPercent is { } reported && double.IsFinite(reported)
            ? (double?)Math.Clamp(reported, 0d, 100d)
            : null;
        if (percent is { } healthPercent && Kilograms(now) is { } kilograms)
        {
            // The HUD uses the whole kilograms requested by the player. Preserve
            // the original packet precision only for decoder validation.
            var wholeKilograms = Math.Truncate(kilograms);
            return new(wholeKilograms * (healthPercent / 100d), wholeKilograms, healthPercent, true);
        }

        return new(null, null, percent, false);
    }
}

internal readonly record struct DinosaurHealthValues(
    double? Current,
    double? Maximum,
    double? Percent,
    bool FromWeight);

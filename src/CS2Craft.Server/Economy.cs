using CS2Craft.Bridge;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using GameServer = CounterStrikeSharp.API.Server;
namespace CS2Craft.Server;
/// <summary>
/// CS2's own economy behind Minecraft's buy menu: the player's CS2 money pays for Minecraft items, under
/// CS2's buy zone and buy time rules, and Minecraft kills on bots earn CS2 kill rewards.
/// </summary>
internal sealed class Economy
{
    CCSGameRulesProxy? proxy;
    float nextLookup;
    public CCSGameRules? Rules
    {
        get
        {
            if (proxy?.IsValid != true && GameServer.CurrentTime >= nextLookup)
            {
                proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
                nextLookup = GameServer.CurrentTime + 1;
            }
            return proxy?.IsValid == true ? proxy.GameRules : null;
        }
    }
    public static int Money(CCSPlayerController player) => player.InGameMoneyServices?.Account ?? 0;
    public static int AddMoney(CCSPlayerController player, int delta)
    {
        if (player.InGameMoneyServices is not { } money) return 0;
        long max = (long)Math.Max(0, Cvar("mp_maxmoney", 16000)), now = (long)money.Account + delta;
        // Rewards stop at mp_maxmoney (never taking away money above it); charges never go negative.
        if (delta > 0) now = Math.Min(now, Math.Max(max, money.Account));
        now = Math.Max(0, now);
        if (now == money.Account) return money.Account;
        money.Account = (int)now;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
        return (int)now;
    }
    /// <summary>Whether the pawn stands in a buy zone, the buy time left and the round time left, in seconds.</summary>
    public (bool Zone, float BuySeconds, float RoundSeconds) Window(CCSPlayerPawn pawn)
    {
        var rules = Rules;
        float now = GameServer.CurrentTime;
        bool anywhere = Cvar("mp_buy_anywhere", 0) is var a && (a == 1 || a == 2 && pawn.TeamNum == 2 || a == 3 && pawn.TeamNum == 3);
        bool zone = pawn.InBuyZone || anywhere;
        if (rules is null) return (zone, 0, 0);
        bool cantBuy = pawn.TeamNum == 2 ? rules.TCantBuy : pawn.TeamNum == 3 ? rules.CTCantBuy : true;
        float buy = rules.WarmupPeriod ? 999 : rules.RoundStartTime + (float)Cvar("mp_buytime", 20) - now;
        float round = rules.FreezePeriod ? rules.RoundTime : rules.RoundStartTime + rules.RoundTime - now;
        return (zone, cantBuy ? 0 : Math.Max(0, buy), Math.Max(0, round));
    }
    /// <summary>Charges <paramref name="price"/> if CS2's rules allow buying now; returns a Protocol.Buy* reason.</summary>
    public int TryBuy(CCSPlayerController player, CCSPlayerPawn pawn, int price)
    {
        var (zone, buySeconds, _) = Window(pawn);
        if (!zone) return Protocol.BuyNotInZone;
        if (buySeconds <= 0) return Protocol.BuyTimeOver;
        if (price < 0 || Money(player) < price) return Protocol.BuyNoMoney;
        AddMoney(player, -price);
        return Protocol.BuyOk;
    }
    /// <summary>A numeric console variable of any numeric type (CS2 declares some as int, some as float).</summary>
    public static double Cvar(string name, double fallback)
    {
        var cvar = ConVar.Find(name);
        if (cvar is null) return fallback;
        return cvar.Type switch
        {
            ConVarType.Bool => cvar.GetPrimitiveValue<bool>() ? 1 : 0,
            ConVarType.Int16 => cvar.GetPrimitiveValue<short>(),
            ConVarType.UInt16 => cvar.GetPrimitiveValue<ushort>(),
            ConVarType.Int32 => cvar.GetPrimitiveValue<int>(),
            ConVarType.UInt32 => cvar.GetPrimitiveValue<uint>(),
            ConVarType.Int64 => cvar.GetPrimitiveValue<long>(),
            ConVarType.UInt64 => cvar.GetPrimitiveValue<ulong>(),
            ConVarType.Float32 => cvar.GetPrimitiveValue<float>(),
            ConVarType.Float64 => cvar.GetPrimitiveValue<double>(),
            _ => double.TryParse(cvar.StringValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback
        };
    }
}

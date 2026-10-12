using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
namespace CS2Craft.Server;
/// <summary>Replicated attack cooldowns prevent Source prediction from firing a hidden gun.</summary>
internal sealed class WeaponGate
{
    readonly Dictionary<uint,(CBasePlayerWeapon Weapon,int Primary,int Secondary)> saved=new();
    public void Update(CCSPlayerPawn pawn)
    {
        if(pawn.WeaponServices is not {} services)return;
        foreach(var handle in services.MyWeapons)
            if(handle.Value is {IsValid:true} weapon)
            {
                if(!saved.ContainsKey(handle.Raw))saved[handle.Raw]=(weapon,weapon.NextPrimaryAttackTick,weapon.NextSecondaryAttackTick);
                if(weapon.NextPrimaryAttackTick!=int.MaxValue){
                    weapon.NextPrimaryAttackTick=int.MaxValue;
                    Utilities.SetStateChanged(weapon,"CBasePlayerWeapon","m_nNextPrimaryAttackTick");
                }
                if(weapon.NextSecondaryAttackTick!=int.MaxValue){
                    weapon.NextSecondaryAttackTick=int.MaxValue;
                    Utilities.SetStateChanged(weapon,"CBasePlayerWeapon","m_nNextSecondaryAttackTick");
                }
            }
    }
    public void Restore()
    {
        foreach(var s in saved.Values)if(s.Weapon.IsValid){
            s.Weapon.NextPrimaryAttackTick=s.Primary;s.Weapon.NextSecondaryAttackTick=s.Secondary;
            Utilities.SetStateChanged(s.Weapon,"CBasePlayerWeapon","m_nNextPrimaryAttackTick");
            Utilities.SetStateChanged(s.Weapon,"CBasePlayerWeapon","m_nNextSecondaryAttackTick");
        }
        saved.Clear();
    }
}

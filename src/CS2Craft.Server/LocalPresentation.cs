using CounterStrikeSharp.API.Modules.Cvars;

namespace CS2Craft.Server;

// Local listen-client cvars are restored on detach, loss of Minecraft, and unload.
internal sealed class LocalPresentation
{
    readonly Dictionary<string,(ConVar Variable,bool Value)> original=new();
    public void Update(bool active,bool hideViewmodel,bool hideHud)
    {
        if(!active){Restore();return;}
        Set("r_drawviewmodel",hideViewmodel);
        Set("cl_drawhud",hideHud);
    }
    void Set(string name,bool hide)
    {
        if(!hide)
        {
            if(original.Remove(name,out var saved))saved.Variable.SetValue(saved.Value);
            return;
        }
        if(original.ContainsKey(name))return;
        var variable=ConVar.Find(name);
        if(variable is null)return;
        original[name]=(variable,variable.GetPrimitiveValue<bool>());
        variable.SetValue(false);
    }
    public void Restore()
    {
        foreach(var saved in original.Values)saved.Variable.SetValue(saved.Value);
        original.Clear();
    }
}

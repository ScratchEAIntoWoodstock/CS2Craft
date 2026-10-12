namespace CS2Craft.Bridge;
/// <summary>Camera orientation independent of the hidden Source pawn/view entity.</summary>
public sealed class MouseLook
{
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    public void Reset(float yaw,float pitch) { Yaw=Coordinates.Wrap(yaw);Pitch=Math.Clamp(pitch,-89.9f,89.9f); }
    public void Move(int dx,int dy,float sensitivity,float yawScale=.022f,float pitchScale=.022f)
    {
        if(!float.IsFinite(sensitivity)||sensitivity<=0) sensitivity=2.5f;
        if(!float.IsFinite(yawScale))yawScale=.022f;
        if(!float.IsFinite(pitchScale))pitchScale=.022f;
        Reset(Yaw+dx*sensitivity*yawScale,Pitch+dy*sensitivity*pitchScale);
    }
}

namespace CS2Craft.Bridge;

/// <summary>
/// Depth-only stand-ins for things CS2 draws that move, so Minecraft blocks behind them are hidden:
/// players and bots (a slightly slim body silhouette, so blocks never vanish beside a person) and doors.
/// Output is Minecraft-space triangles (x, y, z floats) for the renderer's moving occluders.
/// </summary>
public static class MovingShapes
{
    static readonly int[] Faces = { 0,1,3, 0,3,2, 4,6,7, 4,7,5, 0,4,5, 0,5,1, 2,3,7, 2,7,6, 0,2,6, 0,6,4, 1,5,7, 1,7,3 };
    /// <summary>A box in Source units: centre, yaw (degrees about +Z), half extents along its forward, side and up axes.</summary>
    public static void Box(List<float> output, Point center, double yawDegrees, double forward, double side, double up)
    {
        if (!center.IsFinite || !double.IsFinite(yawDegrees) || forward <= 0 || side <= 0 || up <= 0) return;
        double r = yawDegrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        var corners = new Point[8];
        for (int i = 0; i < 8; i++)
        {
            double f = (i & 1) != 0 ? forward : -forward, l = (i & 2) != 0 ? side : -side, u = (i & 4) != 0 ? up : -up;
            corners[i] = Coordinates.ToMinecraft(new(center.X + f * c - l * s, center.Y + f * s + l * c, center.Z + u));
        }
        foreach (int i in Faces) { output.Add((float)corners[i].X); output.Add((float)corners[i].Y); output.Add((float)corners[i].Z); }
    }
    /// <summary>
    /// A CS2 agent standing at <paramref name="feet"/> facing <paramref name="yawDegrees"/>: legs, torso, head and
    /// the arms holding a weapon. <paramref name="eyeHeight"/> (64 standing, 46 crouched) scales it vertically.
    /// </summary>
    public static void Person(List<float> output, Point feet, double yawDegrees, double eyeHeight)
    {
        double k = Math.Clamp(eyeHeight / 64, 0.5, 1.2), r = yawDegrees * Math.PI / 180;
        Point At(double ahead, double z) => new(feet.X + Math.Cos(r) * ahead, feet.Y + Math.Sin(r) * ahead, feet.Z + z * k);
        Box(output, At(0, 17), yawDegrees, 5, 7, 17 * k);      // legs and hips, 0-34
        Box(output, At(0.5, 46), yawDegrees, 5.5, 9, 12 * k);  // torso and shoulders, 34-58
        Box(output, At(1.5, 64), yawDegrees, 4.5, 4, 6 * k);   // head, 58-70
        Box(output, At(12, 48), yawDegrees, 8, 4, 4 * k);      // arms and weapon in front of the chest
    }
}

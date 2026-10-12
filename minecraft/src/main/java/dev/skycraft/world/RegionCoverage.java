package dev.skycraft.world;
/** Region coverage check shared by movement and teleport readiness. */
public final class RegionCoverage {
    private RegionCoverage() {}
    @FunctionalInterface public interface Known { boolean test(int x,int y,int z); }
    public static boolean contains(double minX,double minY,double minZ,double maxX,double maxY,double maxZ,Known known) {
        if(!Double.isFinite(minX+minY+minZ+maxX+maxY+maxZ))return false;
        for(int x=(int)Math.floor(minX/8);x<=(int)Math.floor(maxX/8);x++)
            for(int y=(int)Math.floor(minY/8);y<=(int)Math.floor(maxY/8);y++)
                for(int z=(int)Math.floor(minZ/8);z<=(int)Math.floor(maxZ/8);z++)
                    if(!known.test(x,y,z))return false;
        return true;
    }
}

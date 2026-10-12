package dev.skycraft.world;

/** One exact Skyrim collision triangle in Minecraft space, with precomputed plane and bounds. */
public final class SkyTri {
	/** Surfaces at most ~45 degrees from flat can be walked on. */
	public static final double WALKABLE_NY = 0.7;

	public final double ax, ay, az, bx, by, bz, cx, cy, cz;
	public final double nx, ny, nz; // unit normal (winding is not trusted; use |ny|)
	public final double minX, minY, minZ, maxX, maxY, maxZ;
	public final boolean stairHelper;
	public final boolean collisionOnly;
	public final boolean walkable;

	public SkyTri(float[] v, int o, boolean stairHelper) { this(v,o,stairHelper,false); }
	public SkyTri(float[] v, int o, boolean stairHelper, boolean collisionOnly) {
		this.collisionOnly = collisionOnly;
		this.ax = v[o];
		this.ay = v[o + 1];
		this.az = v[o + 2];
		this.bx = v[o + 3];
		this.by = v[o + 4];
		this.bz = v[o + 5];
		this.cx = v[o + 6];
		this.cy = v[o + 7];
		this.cz = v[o + 8];
		double ux = this.bx - this.ax, uy = this.by - this.ay, uz = this.bz - this.az;
		double wx = this.cx - this.ax, wy = this.cy - this.ay, wz = this.cz - this.az;
		double qx = uy * wz - uz * wy, qy = uz * wx - ux * wz, qz = ux * wy - uy * wx;
		double len = Math.sqrt(qx * qx + qy * qy + qz * qz);
		if (len < 1e-12) {
			this.nx = 0;
			this.ny = 1;
			this.nz = 0;
		} else {
			this.nx = qx / len;
			this.ny = qy / len;
			this.nz = qz / len;
		}
		this.minX = Math.min(this.ax, Math.min(this.bx, this.cx));
		this.minY = Math.min(this.ay, Math.min(this.by, this.cy));
		this.minZ = Math.min(this.az, Math.min(this.bz, this.cz));
		this.maxX = Math.max(this.ax, Math.max(this.bx, this.cx));
		this.maxY = Math.max(this.ay, Math.max(this.by, this.cy));
		this.maxZ = Math.max(this.az, Math.max(this.bz, this.cz));
		this.stairHelper = stairHelper;
		this.walkable = stairHelper || Math.abs(this.ny) >= WALKABLE_NY;
	}

	public boolean degenerate() {
		return this.maxX - this.minX < 1e-9 && this.maxZ - this.minZ < 1e-9;
	}

	/**
	 * Height of the triangle's plane above (x, z) if that point lies inside the triangle's
	 * horizontal footprint, otherwise NaN. Near-vertical triangles have no meaningful height.
	 */
	public double heightAt(double x, double z) {
		if (x < this.minX - 1e-9 || x > this.maxX + 1e-9 || z < this.minZ - 1e-9 || z > this.maxZ + 1e-9 || Math.abs(this.ny) < 0.05) {
			return Double.NaN;
		}
		double d1 = edge(x, z, this.ax, this.az, this.bx, this.bz);
		double d2 = edge(x, z, this.bx, this.bz, this.cx, this.cz);
		double d3 = edge(x, z, this.cx, this.cz, this.ax, this.az);
		boolean hasNeg = d1 < -1e-12 || d2 < -1e-12 || d3 < -1e-12;
		boolean hasPos = d1 > 1e-12 || d2 > 1e-12 || d3 > 1e-12;
		if (hasNeg && hasPos) {
			return Double.NaN;
		}
		// Plane: n . (p - a) = 0  ->  y = ay - (nx (x - ax) + nz (z - az)) / ny
		return this.ay - (this.nx * (x - this.ax) + this.nz * (z - this.az)) / this.ny;
	}

    /** Triangle/AABB separating-axis test for exact crouch clearance. */
    public boolean intersects(double x0,double y0,double z0,double x1,double y1,double z1) {
        double[] a={ax-(x0+x1)/2,ay-(y0+y1)/2,az-(z0+z1)/2};
        double[] b={bx-(x0+x1)/2,by-(y0+y1)/2,bz-(z0+z1)/2};
        double[] c={cx-(x0+x1)/2,cy-(y0+y1)/2,cz-(z0+z1)/2};
        double[] h={(x1-x0)/2,(y1-y0)/2,(z1-z0)/2};
        if(x1<=x0||y1<=y0||z1<=z0)return false;
        for(int i=0;i<3;i++)if(Math.min(a[i],Math.min(b[i],c[i]))>h[i]||Math.max(a[i],Math.max(b[i],c[i]))< -h[i])return false;
        if(separated(nx,ny,nz,a,b,c,h))return false;
        double[][] vertices={a,b,c};
        for(int i=0;i<3;i++) {
            double[] p=vertices[i],q=vertices[(i+1)%3];
            double x=q[0]-p[0],y=q[1]-p[1],z=q[2]-p[2];
            if(separated(0,z,-y,a,b,c,h)||separated(-z,0,x,a,b,c,h)||separated(y,-x,0,a,b,c,h))return false;
        }
        return true;
    }
    private static boolean separated(double x,double y,double z,double[] a,double[] b,double[] c,double[] h) {
        double p=a[0]*x+a[1]*y+a[2]*z,q=b[0]*x+b[1]*y+b[2]*z,r=c[0]*x+c[1]*y+c[2]*z;
        double extent=Math.abs(x)*h[0]+Math.abs(y)*h[1]+Math.abs(z)*h[2];
        return Math.min(p,Math.min(q,r))>extent||Math.max(p,Math.max(q,r))< -extent;
    }

	private static double edge(double px, double pz, double x0, double z0, double x1, double z1) {
		return (x1 - x0) * (pz - z0) - (z1 - z0) * (px - x0);
	}
}

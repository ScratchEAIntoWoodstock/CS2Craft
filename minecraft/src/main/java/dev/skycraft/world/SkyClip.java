package dev.skycraft.world;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;

/**
 * Makes Minecraft ray casts (arrows and other projectiles, the crosshair pick) hit Skyrim's exact
 * collision triangles. Vanilla still clips against real Minecraft blocks; whichever is nearer wins.
 */
public final class SkyClip {
	private SkyClip() {
	}

	public enum Use {
		/** A projectile: the hit cell is the one the surface is in (it sticks there). */
		PROJECTILE,
		/** The player's crosshair: the hit cell is where a block placed against the surface goes. */
		PICK
	}

	private static final ThreadLocal<List<SkyTri>> SCRATCH = ThreadLocal.withInitial(ArrayList::new);

	public static BlockHitResult refine(Vec3 from, Vec3 to, BlockHitResult vanilla, Use use) {
		SkyRay.Hit hit = cast(from, to);
		if (hit == null) {
			return vanilla;
		}
		Vec3 location = new Vec3(hit.x(), hit.y(), hit.z());
		if (vanilla.getType() != HitResult.Type.MISS && from.distanceToSqr(vanilla.getLocation()) <= from.distanceToSqr(location)) {
			return vanilla;
		}
		Direction face = Direction.values()[SkyRay.dominantFace(hit.nx(), hit.ny(), hit.nz())];
		int[] cell = use == Use.PICK ? SkyRay.placementCell(hit) : SkyRay.surfaceCell(hit);
		if(use==Use.PICK) location=new Vec3(
            Math.clamp(location.x,cell[0],cell[0]+1),
            Math.clamp(location.y,cell[1],cell[1]+1),
            Math.clamp(location.z,cell[2],cell[2]+1));
        return new SkyrimHitResult(location, face, new BlockPos(cell[0], cell[1], cell[2]), hit.nx(), hit.ny(), hit.nz());
	}

	/** Nearest Skyrim triangle hit on the segment, or null. */
	public static SkyRay.Hit cast(Vec3 from, Vec3 to) {
		List<SkyTri> tris = SCRATCH.get();
		tris.clear();
		SkyCollision.trianglesNear(new AABB(from, to).inflate(0.01), tris);
		if (tris.isEmpty()) {
			return null;
		}
		SkyRay.Hit hit = SkyRay.cast(tris, from.x, from.y, from.z, to.x, to.y, to.z);
		tris.clear();
		return hit;
	}

	/** A hit on Skyrim geometry (not a Minecraft block). Keeps the exact surface normal. */
	public static final class SkyrimHitResult extends BlockHitResult {
		public final double nx, ny, nz;

		public SkyrimHitResult(Vec3 location, Direction direction, BlockPos pos, double nx, double ny, double nz) {
			super(location, direction, pos, false);
			this.nx = nx;
			this.ny = ny;
			this.nz = nz;
		}
	}
}

package dev.skycraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.skycraft.world.SkyCollision;
import dev.skycraft.world.SkyTri;
import java.util.List;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.level.BlockCollisions;
import net.minecraft.world.level.CollisionGetter;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.shapes.CollisionContext;
import net.minecraft.world.phys.shapes.EntityCollisionContext;
import net.minecraft.world.phys.shapes.Shapes;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;

/**
 * Adds Skyrim's geometry to every block-collision query. Vanilla movement, step-up, onGround
 * and fall-damage logic then run unchanged against it.
 */
@Mixin(BlockCollisions.class)
public abstract class BlockCollisionsMixin {
	@Shadow @Final private AABB box;
	@Unique private List<SkyTri> cs2craft$bodyBlockers;

	@WrapOperation(
		method = "computeNext",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/world/phys/shapes/CollisionContext;getCollisionShape(Lnet/minecraft/world/level/block/state/BlockState;Lnet/minecraft/world/level/CollisionGetter;Lnet/minecraft/core/BlockPos;)Lnet/minecraft/world/phys/shapes/VoxelShape;"
		)
	)
	private VoxelShape skycraft$addSkyrimShape(
		CollisionContext context, BlockState state, CollisionGetter level, BlockPos pos, Operation<VoxelShape> original
	) {
		VoxelShape blockShape = original.call(context, state, level, pos);
		if (context instanceof EntityCollisionContext entityContext && SkyCollision.usesSmoothCollider(entityContext.getEntity())) {
			return blockShape; // this entity collides with Skyrim's exact triangles instead (SkyCollider)
		}
		VoxelShape sky = SkyCollision.shapeAt(pos);
		if (sky == null) {
			return blockShape;
		}
		if (context instanceof EntityCollisionContext ec && SkyCollision.exactEntity(ec.getEntity()) && !cs2craft$exactOverlap(ec.getEntity(), pos)) {
			return blockShape;
		}
		return blockShape.isEmpty() ? sky : Shapes.or(blockShape, sky);
	}

	/**
	 * Whether the exact-collision entity's query actually touches map triangles in this cell.
	 * Body-sized queries (pose fit, the server's movement validation) use the movement collider's
	 * cylinder/step rules; a plain box test would count the slope under the feet and force a
	 * crouch/crawl pose or a server rubber-band on every ramp. Probes at or below the feet
	 * (support, edge and fall checks) keep exact triangle overlap.
	 */
	@Unique
	private boolean cs2craft$exactOverlap(Entity entity, BlockPos pos) {
		AABB cell = new AABB(pos);
		if (box.getYsize() >= 0.5 && box.maxY > entity.getY() + 0.3) {
			if (cs2craft$bodyBlockers == null) {
				double step = entity.maxUpStep();
				cs2craft$bodyBlockers = SkyCollision.bodyBlockers(box, step, entity.onGround() ? step : 0.02);
			}
			if (cs2craft$bodyBlockers.isEmpty() || !box.intersects(cell)) {
				return false;
			}
			AABB query = box.intersect(cell);
			for (SkyTri t : cs2craft$bodyBlockers) {
				if (t.intersects(query.minX, query.minY, query.minZ, query.maxX, query.maxY, query.maxZ)) {
					return true;
				}
			}
			return false;
		}
		AABB bounds = box.deflate(1e-4);
		return bounds.intersects(cell) && SkyCollision.intersects(bounds.intersect(cell));
	}
}

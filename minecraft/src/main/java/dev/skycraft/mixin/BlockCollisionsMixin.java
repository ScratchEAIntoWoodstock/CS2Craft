package dev.skycraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.skycraft.world.SkyCollision;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.BlockCollisions;
import net.minecraft.world.level.CollisionGetter;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.shapes.CollisionContext;
import net.minecraft.world.phys.shapes.EntityCollisionContext;
import net.minecraft.world.phys.shapes.Shapes;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/**
 * Adds Skyrim's geometry to every block-collision query. Vanilla movement, step-up, onGround
 * and fall-damage logic then run unchanged against it.
 */
@Mixin(BlockCollisions.class)
public abstract class BlockCollisionsMixin {
    @org.spongepowered.asm.mixin.Shadow @org.spongepowered.asm.mixin.Final
    private net.minecraft.world.phys.AABB box;
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
        if (context instanceof EntityCollisionContext ec && SkyCollision.exactEntity(ec.getEntity())) {
            var bounds=box.deflate(1e-4);
            // Footprint corners may touch a slope slightly above the capsule's support point.
            // Ignore that contact only for body-fit queries; edge probes below the feet still see it.
            var entity=ec.getEntity();
            if(entity!=null && bounds.minY>=entity.getY()-.01)
                bounds=bounds.setMinY(Math.max(bounds.minY,entity.getY()+.05));
            var query=bounds.intersect(new net.minecraft.world.phys.AABB(pos));
            if(!SkyCollision.intersects(query))return blockShape;
        }
        return blockShape.isEmpty() ? sky : Shapes.or(blockShape, sky);
	}
}

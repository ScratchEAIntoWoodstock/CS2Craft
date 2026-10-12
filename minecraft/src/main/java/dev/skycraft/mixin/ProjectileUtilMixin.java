package dev.skycraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.llamalad7.mixinextras.sugar.Local;
import dev.skycraft.combat.SkyrimActorEntity;
import dev.skycraft.world.SkyClip;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.projectile.ProjectileUtil;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** Thrown projectiles (snowballs, eggs, pearls, potions) and spear reach checks see Skyrim surfaces. */
@Mixin(ProjectileUtil.class)
public abstract class ProjectileUtilMixin {
	@WrapOperation(
		method = { "getHitResult", "getHitEntitiesAlong" },
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/Level;clipIncludingBorder(Lnet/minecraft/world/level/ClipContext;)Lnet/minecraft/world/phys/BlockHitResult;")
	)
	private static BlockHitResult skycraft$hitSkyrim(Level level, ClipContext context, Operation<BlockHitResult> original) {
		return SkyClip.refine(context.getFrom(), context.getTo(), original.call(level, context), SkyClip.Use.PROJECTILE);
	}

	/**
	 * Projectiles hit anything within a margin (up to 0.3 blocks on every side) to make Minecraft mobs
	 * easy to hit. A CS2 player's stand-in already matches the CS2 body, so arrows get its exact box:
	 * the box is shrunk by the margin here, and the inflate that follows restores it.
	 */
	@WrapOperation(
		method = {
			"getEntityHitResult(Lnet/minecraft/world/level/Level;Lnet/minecraft/world/entity/Entity;Lnet/minecraft/world/phys/Vec3;Lnet/minecraft/world/phys/Vec3;Lnet/minecraft/world/phys/AABB;Ljava/util/function/Predicate;F)Lnet/minecraft/world/phys/EntityHitResult;",
			"getManyEntityHitResult(Lnet/minecraft/world/level/Level;Lnet/minecraft/world/entity/Entity;Lnet/minecraft/world/phys/Vec3;Lnet/minecraft/world/phys/Vec3;Lnet/minecraft/world/phys/AABB;Ljava/util/function/Predicate;FLnet/minecraft/world/level/ClipContext$Block;ZZ)Ljava/util/Collection;"
		},
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/entity/Entity;getBoundingBox()Lnet/minecraft/world/phys/AABB;")
	)
	private static AABB skycraft$exactCs2Hitbox(Entity target, Operation<AABB> original, @Local(argsOnly = true) float margin) {
		AABB box = original.call(target);
		return target instanceof SkyrimActorEntity && margin > 0.0F ? box.inflate(-margin) : box;
	}
}

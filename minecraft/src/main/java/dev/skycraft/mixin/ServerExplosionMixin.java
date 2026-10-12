package dev.skycraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.skycraft.link.Proto;
import dev.skycraft.link.SkyLink;
import dev.skycraft.world.SkyClip;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.ServerExplosion;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Tells Skyrim about every Minecraft explosion (TNT, creepers, beds, ...) once it has gone off, so
 * Skyrim's own physics feel it: loose objects are thrown and people are knocked away. In CS2Craft the
 * host plays a CS2 explosion effect there instead.
 */
@Mixin(ServerExplosion.class)
public abstract class ServerExplosionMixin {
	@Inject(method = "explode", at = @At("RETURN"))
	private void skycraft$tellSkyrim(CallbackInfoReturnable<Integer> cir) {
		if (!SkyLink.active()) {
			return;
		}
		ServerExplosion self = (ServerExplosion) (Object) this;
		var center = self.center();
		SkyLink.pushEvent(Proto.EV_EXPLOSION, 0, (float) center.x, (float) center.y, (float) center.z, self.radius(), 0);
	}

	/** Map walls shield from blasts: exposure rays stop at map geometry as well as Minecraft blocks. */
	@WrapOperation(
		method = "getSeenPercent",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/Level;clip(Lnet/minecraft/world/level/ClipContext;)Lnet/minecraft/world/phys/BlockHitResult;")
	)
	private static BlockHitResult cs2craft$wallsShield(Level level, ClipContext context, Operation<BlockHitResult> original) {
		return SkyClip.refine(context.getFrom(), context.getTo(), original.call(level, context), SkyClip.Use.PROJECTILE);
	}
}

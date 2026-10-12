package dev.skycraft.mixin;

import dev.skycraft.link.Proto;
import dev.skycraft.link.SkyLink;
import net.minecraft.world.level.ServerExplosion;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Tells Skyrim about every Minecraft explosion (TNT, creepers, beds, ...) once it has gone off, so
 * Skyrim's own physics feel it: loose objects are thrown and people are knocked away.
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
}

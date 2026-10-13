package dev.skycraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.skycraft.link.SkyLink;
import net.minecraft.server.level.ServerPlayer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;

/**
 * The hunger bar is CS2Craft's stamina. Vanilla movement costs (0.1 per block sprinted, 0.2 per sprint
 * jump) barely move it during a CS2 round, so while linked the cost of sprinting, swimming and jumping is
 * multiplied by the host's StaminaDrain setting. Attacking and taking damage keep their vanilla cost.
 * Movement hunger is charged on the server's player only (the client's food is synced from it).
 */
@Mixin(ServerPlayer.class)
public abstract class PlayerStaminaMixin {
	@Unique
	private static final SkyLink.SkyState skycraft$state = new SkyLink.SkyState();
	@Unique
	private static boolean skycraft$logged;

	@WrapOperation(
		method = { "checkMovementStatistics", "jumpFromGround" },
		at = @At(value = "INVOKE", target = "Lnet/minecraft/server/level/ServerPlayer;causeFoodExhaustion(F)V")
	)
	private void skycraft$staminaDrain(ServerPlayer player, float exhaustion, Operation<Void> original) {
		float scale = skycraft$scale();
		if (scale != 1.0F && !skycraft$logged) {
			skycraft$logged = true;
			dev.skycraft.SkyCraft.LOG.info("CS2Craft: stamina drain x{} (movement hunger cost {} -> {})", scale, exhaustion, exhaustion * scale);
		}
		original.call(player, exhaustion * scale);
	}

	/** The host's setting while linked, else vanilla. Server thread only, so the shared snapshot is safe. */
	@Unique
	private static float skycraft$scale() {
		if (!SkyLink.active() || !SkyLink.readSkyState(skycraft$state)) {
			return 1.0F;
		}
		float scale = skycraft$state.staminaDrain;
		return Float.isFinite(scale) && scale >= 0.0F && scale <= 10.0F ? scale : 1.0F;
	}
}

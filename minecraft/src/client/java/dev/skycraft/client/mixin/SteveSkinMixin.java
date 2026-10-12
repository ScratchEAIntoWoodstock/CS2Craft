package dev.skycraft.client.mixin;
import dev.skycraft.client.SkyClient;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.AbstractClientPlayer;
import net.minecraft.client.resources.DefaultPlayerSkin;
import net.minecraft.world.entity.player.PlayerSkin;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;
@Mixin(AbstractClientPlayer.class)
public abstract class SteveSkinMixin {
    // Default skin index 15 in the pinned 26.3 client is the classic wide Steve.
    private static final PlayerSkin STEVE = DefaultPlayerSkin.get(new java.util.UUID(0,15));
    @Inject(method="getSkin",at=@At("HEAD"),cancellable=true)
    private void cs2craft$steve(CallbackInfoReturnable<PlayerSkin> result) {
        if(SkyClient.linked() && (Object)this == Minecraft.getInstance().player) result.setReturnValue(STEVE);
    }
}

package dev.skycraft.client.mixin;

import dev.skycraft.client.SkyClient;
import net.minecraft.client.gui.components.toasts.AdvancementToast;
import net.minecraft.client.gui.components.toasts.RecipeToast;
import net.minecraft.client.gui.components.toasts.Toast;
import net.minecraft.client.gui.components.toasts.ToastManager;
import net.minecraft.client.gui.components.toasts.TutorialToast;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Advancement, recipe and tutorial pop-ups would cover CS2's screen corner mid-fight; drop them while linked. */
@Mixin(ToastManager.class)
public abstract class ToastManagerMixin {
	@Inject(method = "addToast", at = @At("HEAD"), cancellable = true)
	private void skycraft$noPopupsInCs2(Toast toast, CallbackInfo ci) {
		if (SkyClient.linked() && (toast instanceof AdvancementToast || toast instanceof RecipeToast || toast instanceof TutorialToast)) {
			ci.cancel();
		}
	}
}

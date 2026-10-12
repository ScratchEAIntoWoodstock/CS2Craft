package dev.skycraft.client;

import dev.skycraft.SkyCraft;
import net.fabricmc.fabric.api.client.rendering.v1.hud.HudElementRegistry;
import net.minecraft.client.Minecraft;
import net.minecraft.resources.Identifier;

/**
 * The parts of CS2's HUD that CS2Craft hides while Minecraft plays: money (bottom left), the round
 * clock (top centre) and, during buy time in a buy zone, a reminder that B opens the buy menu.
 */
public final class CraftHud {
	private CraftHud() {
	}

	public static void register() {
		HudElementRegistry.addLast(Identifier.fromNamespaceAndPath(SkyCraft.MOD_ID, "cs2_status"), (g, delta) -> {
			Minecraft minecraft = Minecraft.getInstance();
			var sky = SkyClient.sky();
			if (!SkyClient.linked() || !sky.inGame() || sky.loading() || minecraft.player == null) {
				return;
			}
			var font = minecraft.font;
			int w = g.guiWidth(), h = g.guiHeight();
			if (sky.roundSeconds > 0) {
				String clock = clock(sky.roundSeconds);
				g.text(font, clock, (w - font.width(clock)) / 2, 4, sky.roundSeconds < 10 ? 0xFFFF5555 : 0xFFFFFFFF);
			}
			g.text(font, "$" + sky.money, 6, h - 14, 0xFF7CFC7C);
			if (sky.canBuy() && minecraft.gui.screen() == null) {
				String hint = "[" + BuyScreen.KEY.getTranslatedKeyMessage().getString() + "] Buy menu" + (sky.buySeconds < 900 ? "  " + clock(sky.buySeconds) : "");
				g.text(font, hint, 6, h - 26, 0xFFFFD866);
			}
		});
	}

	/** m:ss, rounding up like CS2's clock. */
	public static String clock(float seconds) {
		int s = (int) Math.ceil(Math.min(seconds, 5999));
		return (s / 60) + ":" + (s % 60 < 10 ? "0" : "") + (s % 60);
	}
}

package dev.skycraft.client;

import com.mojang.blaze3d.platform.InputConstants;
import dev.skycraft.link.Proto;
import dev.skycraft.link.SkyLink;
import dev.skycraft.shop.Shop;
import java.util.ArrayList;
import java.util.List;
import net.fabricmc.fabric.api.client.keymapping.v1.KeyMappingHelper;
import net.minecraft.client.KeyMapping;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.resources.sounds.SimpleSoundInstance;
import net.minecraft.network.chat.Component;
import net.minecraft.sounds.SoundEvents;

/**
 * Minecraft's buy menu (B, like CS2's; the CS2 plugin keeps CS2's own menu closed). Offers are
 * paid with CS2 money: a click asks CS2 (EV_BUY), CS2 checks its buy zone, buy time and the money and
 * answers with IN_BUY_RESULT, and only then does the integrated server hand the items over.
 */
public final class BuyScreen extends Screen {
	public static final KeyMapping KEY = new KeyMapping("key.skycraft.buy_menu", InputConstants.KEY_B, KeyMapping.Category.GAMEPLAY);
	private static final int PANEL_W = 380, PANEL_H = 236, ROW_H = 22, COLUMN_W = 178;
	private static Shop.Category category = Shop.Category.MELEE;
	private static String status = "";
	private static int statusColor = 0xFFFFFFFF;
	private static long statusUntil;
	private static int requests;
	private final List<OfferButton> offers = new ArrayList<>();
	private int left, top;

	private record OfferButton(Shop.Offer offer, Button button) {
	}

	public BuyScreen() {
		super(Component.literal("Buy Menu"));
	}

	public static void register() {
		KeyMappingHelper.registerKeyMapping(KEY);
		// "/cs2buy <offer name>" buys without the menu (handy for binds; the live test uses it too).
		net.fabricmc.fabric.api.client.command.v2.ClientCommandRegistrationCallback.EVENT.register((dispatcher, context) -> dispatcher.register(
			net.fabricmc.fabric.api.client.command.v2.ClientCommands.literal("cs2buy").then(
				net.fabricmc.fabric.api.client.command.v2.ClientCommands.argument("offer", com.mojang.brigadier.arguments.StringArgumentType.greedyString())
					.executes(command -> {
						String name = com.mojang.brigadier.arguments.StringArgumentType.getString(command, "offer").trim();
						Shop.Offer offer = Shop.offers().stream().filter(o -> o.name().equalsIgnoreCase(name)).findFirst().orElse(null);
						var sky = SkyClient.sky();
						String refusal = offer == null ? "No offer called " + name
							: Shop.refusal(command.getSource().getPlayer(), offer, sky.money, sky.canBuy());
						if (refusal != null) {
							command.getSource().sendError(Component.literal(refusal));
							return 0;
						}
						buy(offer);
						return 1;
					}))));
	}

	/** Client tick: B opens the menu while Minecraft plays inside CS2. */
	public static void tick(Minecraft minecraft) {
		while (KEY.consumeClick()) {
			if (minecraft.gui.screen() == null && minecraft.player != null && SkyClient.linked() && SkyClient.sky().inGame()) {
				InputBridge.releaseAll();
				minecraft.gui.setScreen(new BuyScreen());
			}
		}
	}

	@Override
	protected void init() {
		this.left = (this.width - PANEL_W) / 2;
		this.top = Math.max(4, (this.height - PANEL_H) / 2);
		int tabW = (PANEL_W - 16) / Shop.Category.values().length;
		int x = this.left + 8;
		for (Shop.Category c : Shop.Category.values()) {
			Button tab = Button.builder(Component.literal(c.label), b -> {
				category = c;
				this.rebuildWidgets();
			}).bounds(x, this.top + 34, tabW - 2, 20).build();
			tab.active = c != category;
			this.addRenderableWidget(tab);
			x += tabW;
		}
		this.offers.clear();
		int row = 0, column = 0;
		for (Shop.Offer offer : Shop.offers()) {
			if (offer.category() != category) {
				continue;
			}
			int bx = this.left + 8 + column * (COLUMN_W + 8) + 20, by = this.top + 60 + row * ROW_H;
			Button button = Button.builder(Component.literal(offer.name() + "  $" + offer.price()), b -> buy(offer)).bounds(bx, by, COLUMN_W - 20, 20).build();
			this.offers.add(new OfferButton(offer, this.addRenderableWidget(button)));
			if (++column == 2) {
				column = 0;
				row++;
			}
		}
		this.addRenderableWidget(Button.builder(Component.literal("Close (B)"), b -> this.onClose()).bounds(this.left + PANEL_W - 88, this.top + PANEL_H - 26, 80, 20).build());
		this.refresh();
	}

	@Override
	public void tick() {
		this.refresh();
	}

	/** Greys out what cannot be bought right now and says why in the tooltip. */
	private void refresh() {
		var sky = SkyClient.sky();
		var player = this.minecraft.player;
		for (OfferButton o : this.offers) {
			String refusal = player == null ? "" : Shop.refusal(player, o.offer(), sky.money, sky.canBuy());
			o.button().active = refusal == null;
			Component tip = Component.literal(o.offer().name() + " - $" + o.offer().price() + "\n" + o.offer().description()
				+ (refusal == null ? "" : "\n§c" + refusal));
			o.button().setTooltip(Tooltip.create(tip));
		}
	}

	private static void buy(Shop.Offer offer) {
		var sky = SkyClient.sky();
		var player = Minecraft.getInstance().player;
		if (player == null || Shop.refusal(player, offer, sky.money, sky.canBuy()) != null) {
			return;
		}
		SkyLink.pushEvent(Proto.EV_BUY, ++requests, offer.price(), 0.0F, 0.0F, 0.0F, 0, offer.id());
		show("Buying " + offer.name() + "...", 0xFFAAAAAA);
	}

	/** CS2 answered a purchase (InputBridge, client thread). */
	public static void result(Minecraft minecraft, boolean bought, int offerId, int moneyLeft, int reason) {
		Shop.Offer offer = Shop.offer(offerId);
		String name = offer == null ? "item" : offer.name();
		if (bought) {
			show("Bought " + name + "  ($" + moneyLeft + " left)", 0xFF55FF55);
		} else {
			show(switch (reason) {
				case Proto.BUY_NO_MONEY -> "Not enough money ($" + moneyLeft + ")";
				case Proto.BUY_NOT_IN_ZONE -> "You are not in a buy zone";
				case Proto.BUY_TIME_OVER -> "Buy time is over";
				default -> "You can't buy right now";
			}, 0xFFFF5555);
		}
		minecraft.getSoundManager().play(SimpleSoundInstance.forUI(bought ? SoundEvents.ITEM_PICKUP : SoundEvents.VILLAGER_NO, bought ? 1.2F : 1.0F, 0.6F));
		if (!bought || minecraft.player == null || minecraft.gui.screen() instanceof BuyScreen) {
			return;
		}
		minecraft.player.sendOverlayMessage(Component.literal("Bought " + name).withStyle(net.minecraft.ChatFormatting.GREEN));
	}

	private static void show(String text, int color) {
		status = text;
		statusColor = color;
		statusUntil = System.currentTimeMillis() + 4000;
	}

	@Override
	public boolean keyPressed(KeyEvent event) {
		if (KEY.matches(event)) {
			this.onClose();
			return true;
		}
		return super.keyPressed(event);
	}

	@Override
	public boolean isPauseScreen() {
		return false;
	}

	@Override
	public void extractBackground(GuiGraphicsExtractor g, int mouseX, int mouseY, float partial) {
		// No blur: CS2's world shows around the panel.
		g.fill(this.left, this.top, this.left + PANEL_W, this.top + PANEL_H, 0xD0101418);
		g.fill(this.left, this.top, this.left + PANEL_W, this.top + 1, 0xFF6A8CAF);
	}

	@Override
	public void extractRenderState(GuiGraphicsExtractor g, int mouseX, int mouseY, float partial) {
		super.extractRenderState(g, mouseX, mouseY, partial);
		var sky = SkyClient.sky();
		g.text(this.font, "BUY MENU", this.left + 8, this.top + 8, 0xFFFFFFFF);
		String money = "$" + sky.money;
		g.text(this.font, money, this.left + PANEL_W - 8 - this.font.width(money), this.top + 8, 0xFF7CFC7C);
		String window = sky.canBuy() ? (sky.buySeconds < 900 ? "Buy time " + CraftHud.clock(sky.buySeconds) : "Warmup: buy any time")
			: (sky.flags & Proto.SKY_BUY_ZONE) == 0 ? "Not in a buy zone" : "Buy time is over";
		g.text(this.font, window, this.left + 8, this.top + 20, sky.canBuy() ? 0xFFC8C8C8 : 0xFFFF7070);
		for (OfferButton o : this.offers) {
			g.item(o.offer().icon(), o.button().getX() - 19, o.button().getY() + 2);
		}
		if (System.currentTimeMillis() < statusUntil) {
			g.text(this.font, status, this.left + 8, this.top + PANEL_H - 20, statusColor);
		}
	}
}

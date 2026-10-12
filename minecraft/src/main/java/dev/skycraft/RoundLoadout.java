package dev.skycraft;

import dev.skycraft.shop.Shop;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import net.fabricmc.fabric.api.command.v2.CommandRegistrationCallback;
import net.fabricmc.fabric.api.entity.event.v1.ServerPlayerEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.networking.v1.ServerPlayConnectionEvents;
import net.minecraft.commands.Commands;
import net.minecraft.network.chat.Component;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.item.ItemStack;

/**
 * CS2's loadout rules in Minecraft. Every life starts with the default kit ({@link Shop#defaultKit}),
 * the way CS2 hands out a knife and pistol; what the player buys is kept while they survive, into
 * the next round, and lost when they die. Each round also starts like a fresh life: full health and
 * hunger, no effects, and everything carried repaired.
 *
 * <p>{@code /cs2craft loadout} makes the current inventory this session's default kit instead
 * (for sandbox building); {@code /cs2craft loadout reset} goes back to the built-in one.
 */
public final class RoundLoadout {
	private static final Map<UUID, List<ItemStack>> CUSTOM = new ConcurrentHashMap<>();

	private RoundLoadout() {
	}

	public static void init() {
		// Joining starts a match: whatever the world last saved is replaced by the default kit.
		ServerPlayConnectionEvents.JOIN.register((handler, sender, server) -> server.execute(() -> {
			giveDefaultKit(handler.getPlayer());
			freshLife(handler.getPlayer());
		}));
		// Keep-inventory copied the old life's items over; a new life starts from the default kit.
		ServerPlayerEvents.AFTER_RESPAWN.register((oldPlayer, newPlayer, alive) -> {
			if (!alive) {
				giveDefaultKit(newPlayer);
			}
		});
		ServerLifecycleEvents.SERVER_STOPPING.register(server -> server.getPlayerList().getPlayers().forEach(RoundLoadout::giveDefaultKit));
		CommandRegistrationCallback.EVENT.register((dispatcher, registries, selection) -> dispatcher.register(
			Commands.literal("cs2craft").then(Commands.literal("loadout").executes(context -> {
				ServerPlayer player = context.getSource().getPlayerOrException();
				CUSTOM.put(player.getUUID(), snapshot(player));
				context.getSource().sendSuccess(() -> Component.literal("CS2Craft: this inventory is now your default kit for this session."), false);
				return 1;
			}).then(Commands.literal("reset").executes(context -> {
				CUSTOM.remove(context.getSource().getPlayerOrException().getUUID());
				context.getSource().sendSuccess(() -> Component.literal("CS2Craft: back to the built-in default kit."), false);
				return 1;
			})))
		));
	}

	/**
	 * A CS2 round started (server thread). {@code freshGear}: a new match or half, or carry-over is off;
	 * otherwise a survivor keeps what they carry, repaired.
	 */
	public static void roundStart(ServerPlayer player, boolean freshGear) {
		if (!player.isAlive()) {
			return; // the immediate respawn gives the default kit
		}
		if (freshGear) {
			giveDefaultKit(player);
		} else {
			repairAll(player);
		}
		freshLife(player);
		SkyCraft.LOG.info("CS2Craft: round start for {} ({})", player.getName().getString(), freshGear ? "default kit" : "kept gear, repaired");
	}

	/** The inventory becomes the default kit, as new. */
	public static void giveDefaultKit(ServerPlayer player) {
		player.stopUsingItem();
		Inventory inventory = player.getInventory();
		inventory.clearContent();
		List<ItemStack> custom = CUSTOM.get(player.getUUID());
		if (custom != null) {
			for (int i = 0; i < Math.min(custom.size(), inventory.getContainerSize()); i++) {
				inventory.setItem(i, fresh(custom.get(i)));
			}
		} else {
			for (ItemStack stack : Shop.defaultKit()) {
				inventory.add(stack);
			}
		}
		player.containerMenu.broadcastChanges();
	}

	/** Full health and hunger, no effects or fire: how every CS2 round starts. */
	private static void freshLife(ServerPlayer player) {
		player.removeAllEffects();
		player.setAbsorptionAmount(0.0F);
		player.setHealth(player.getMaxHealth());
		player.getFoodData().setFoodLevel(20);
		player.getFoodData().setSaturation(5.0F);
		player.clearFire();
		player.setAirSupply(player.getMaxAirSupply());
		player.resetFallDistance();
	}

	private static void repairAll(ServerPlayer player) {
		Inventory inventory = player.getInventory();
		for (int i = 0; i < inventory.getContainerSize(); i++) {
			ItemStack stack = inventory.getItem(i);
			if (stack.isDamageableItem() && stack.isDamaged()) {
				stack.setDamageValue(0);
			}
		}
		player.containerMenu.broadcastChanges();
	}

	private static List<ItemStack> snapshot(ServerPlayer player) {
		Inventory inventory = player.getInventory();
		List<ItemStack> items = new ArrayList<>(inventory.getContainerSize());
		for (int i = 0; i < inventory.getContainerSize(); i++) {
			items.add(fresh(inventory.getItem(i)));
		}
		return items;
	}

	/** A copy as good as new. */
	private static ItemStack fresh(ItemStack stack) {
		ItemStack copy = stack.copy();
		if (copy.isDamageableItem() && copy.isDamaged()) {
			copy.setDamageValue(0);
		}
		return copy;
	}
}

package dev.skycraft;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import net.fabricmc.fabric.api.command.v2.CommandRegistrationCallback;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.networking.v1.ServerPlayConnectionEvents;
import net.minecraft.commands.Commands;
import net.minecraft.network.chat.Component;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.item.ItemStack;

/**
 * CS2 rounds reset the player like a fresh life: full health and hunger, no effects, and the round
 * loadout back in the inventory (armour and off hand included), fully repaired.
 *
 * <p>The loadout is the inventory the player joined with (the starter kits on a new save), or
 * whatever they last saved with {@code /cs2craft loadout}. It is restored before the world saves on
 * quit, so a half-spent inventory is never what the next session starts from.
 */
public final class RoundLoadout {
	private static final Map<UUID, List<ItemStack>> LOADOUTS = new ConcurrentHashMap<>();

	private RoundLoadout() {
	}

	/** Register after the starter-kit JOIN handler so the kits are part of the first loadout. */
	public static void init() {
		ServerPlayConnectionEvents.JOIN.register((handler, sender, server) -> LOADOUTS.computeIfAbsent(handler.getPlayer().getUUID(), id -> snapshot(handler.getPlayer())));
		ServerLifecycleEvents.SERVER_STOPPING.register(server -> server.getPlayerList().getPlayers().forEach(RoundLoadout::reset));
		CommandRegistrationCallback.EVENT.register((dispatcher, registries, selection) -> dispatcher.register(
			Commands.literal("cs2craft").then(Commands.literal("loadout").executes(context -> {
				ServerPlayer player = context.getSource().getPlayerOrException();
				LOADOUTS.put(player.getUUID(), snapshot(player));
				context.getSource().sendSuccess(() -> Component.literal("CS2Craft: this inventory is now restored at every round start."), false);
				return 1;
			}))
		));
	}

	private static List<ItemStack> snapshot(ServerPlayer player) {
		Inventory inventory = player.getInventory();
		List<ItemStack> items = new ArrayList<>(inventory.getContainerSize());
		for (int i = 0; i < inventory.getContainerSize(); i++) {
			items.add(fresh(inventory.getItem(i)));
		}
		return items;
	}

	/** A copy as good as new: every round starts with undamaged weapons, tools and armour. */
	private static ItemStack fresh(ItemStack stack) {
		ItemStack copy = stack.copy();
		if (copy.isDamageableItem() && copy.isDamaged()) {
			copy.setDamageValue(0);
		}
		return copy;
	}

	/** Runs on the server thread. */
	public static void reset(ServerPlayer player) {
		if (!player.isAlive()) {
			return; // immediate respawn brings them back; the next round resets them
		}
		player.stopUsingItem();
		List<ItemStack> loadout = LOADOUTS.get(player.getUUID());
		if (loadout != null) {
			Inventory inventory = player.getInventory();
			inventory.clearContent();
			for (int i = 0; i < Math.min(loadout.size(), inventory.getContainerSize()); i++) {
				inventory.setItem(i, fresh(loadout.get(i)));
			}
		}
		player.removeAllEffects();
		player.setAbsorptionAmount(0.0F);
		player.setHealth(player.getMaxHealth());
		player.getFoodData().setFoodLevel(20);
		player.getFoodData().setSaturation(5.0F);
		player.clearFire();
		player.setAirSupply(player.getMaxAirSupply());
		player.resetFallDistance();
		player.containerMenu.broadcastChanges();
		SkyCraft.LOG.info("CS2Craft: round reset {} (health, hunger, effects{})", player.getName().getString(), loadout != null ? ", loadout" : "");
	}
}

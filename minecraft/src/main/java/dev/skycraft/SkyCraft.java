package dev.skycraft;

import dev.skycraft.combat.SkyCombat;
import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.minecraft.server.MinecraftServer;
import net.minecraft.world.level.gamerules.GameRules;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public final class SkyCraft implements ModInitializer {
	public static final String MOD_ID = "skycraft";
	public static final String WORLD_NAME = System.getProperty("cs2craft.world", "CS2Craft");
	public static final Logger LOG = LoggerFactory.getLogger(MOD_ID);

	@Override
	public void onInitialize() {
		SkyCombat.init();
		dev.skycraft.net.SkyNet.init();
		ServerLifecycleEvents.SERVER_STARTED.register(SkyCraft::configureServer);
		// Every life starts with CS2Craft's default kit; the rest is bought (dev.skycraft.shop.Shop).
		RoundLoadout.init();
	}

	/** The mirror world is a void that only exists to host the player; Skyrim drives time and spawning. */
	private static void configureServer(MinecraftServer server) {
		GameRules rules = server.getGameRules();
		rules.set(GameRules.ADVANCE_TIME, false, server);
		rules.set(GameRules.ADVANCE_WEATHER, false, server);
		rules.set(GameRules.SPAWN_MOBS, false, server);
		rules.set(GameRules.SPAWN_MONSTERS, false, server);
		rules.set(GameRules.SPAWN_PHANTOMS, false, server);
		rules.set(GameRules.SPAWN_PATROLS, false, server);
		rules.set(GameRules.SPAWN_WANDERING_TRADERS, false, server);
		rules.set(GameRules.PLAYER_MOVEMENT_CHECK, false, server);
		rules.set(GameRules.KEEP_INVENTORY, true, server);
		rules.set(GameRules.IMMEDIATE_RESPAWN, true, server);
		rules.set(GameRules.SHOW_ADVANCEMENT_MESSAGES, false, server);
		// CS2 has no health regeneration: healing is bought (potions, golden apples) or comes with a new round.
		rules.set(GameRules.NATURAL_HEALTH_REGENERATION, false, server);
		server.getCommands().performPrefixedCommand(server.createCommandSourceStack().withSuppressedOutput(), "time set noon");
		LOG.info("SkyCraft: mirror world configured");
	}
}

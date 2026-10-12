package dev.skycraft.shop;

import dev.skycraft.SkyCraft;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.function.Supplier;
import net.minecraft.core.Holder;
import net.minecraft.core.component.DataComponents;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.alchemy.Potion;
import net.minecraft.world.item.alchemy.PotionContents;
import net.minecraft.world.item.alchemy.Potions;
import org.jspecify.annotations.Nullable;

/**
 * Minecraft's buy menu, paid with the player's CS2 money under CS2's buy zone and buy time rules
 * (the CS2 plugin charges the price and answers; see Economy.cs). Prices are CS2 dollars, balanced
 * against CS2's economy ($800 pistol round, $300-$600 per kill, $1400-$3400 round bonuses) and against
 * how this bridge scales damage: bots have 100 HP, and every bullet a bot fires is Minecraft damage
 * (CS2 damage / 5) that Minecraft armour reduces.
 *
 * <ul>
 * <li>Melee: hits to kill a bot at full charge. Iron sword 4, diamond 4 (3 crits), netherite 3;
 * axes are slower but kill in 3 (2 critical hits from diamond up). The mace adds fall damage.
 * <li>Ranged: a full-draw arrow does 36-66, so two kill.
 * <li>Armour against an AK-47 body shot (36): iron takes 45% off (CS2 kevlar takes 22%), diamond 73%,
 * netherite 74% plus knockback resistance. Natural regeneration is off, so healing is bought.
 * <li>A raised shield blocks bullets from the front (and slows you), a totem cheats death once.
 * </ul>
 */
public final class Shop {
	public enum Category {
		MELEE("Melee"), RANGED("Ranged"), ARMOR("Armor"), UTILITY("Utility"), BLOCKS("Blocks");

		public final String label;

		Category(String label) {
			this.label = label;
		}
	}

	public enum Kind {
		/** Into the inventory (dropped at the player's feet if it is full). */
		ITEMS,
		/** A full armour set, worn at once and replacing what was worn (as CS2 replaces armour). */
		ARMOR,
		/** Into the off hand when it is free (shield, totem). */
		OFFHAND
	}

	/**
	 * @param limit most of the first item the player may carry after buying (0 = no limit); armour sets
	 *              cannot be bought while already worn.
	 */
	public record Offer(int id, Category category, String name, int price, String description, int limit, Kind kind, Supplier<List<ItemStack>> items) {
		public ItemStack icon() {
			return this.items.get().getFirst();
		}
	}

	private static final List<Offer> OFFERS = new ArrayList<>();
	private static final EquipmentSlot[] ARMOR_SLOTS = { EquipmentSlot.HEAD, EquipmentSlot.CHEST, EquipmentSlot.LEGS, EquipmentSlot.FEET };

	static {
		melee("Stone Sword", 100, "20 per hit, 5 hits", Items.STONE_SWORD);
		melee("Iron Sword", 400, "25 per hit, 4 hits (3 critical)", Items.IRON_SWORD);
		melee("Diamond Sword", 1000, "30 per hit, 4 hits (3 critical)", Items.DIAMOND_SWORD);
		melee("Netherite Sword", 2200, "35 per hit, 3 hits", Items.NETHERITE_SWORD);
		melee("Iron Axe", 650, "35 per hit, 3 hits; slow swing", Items.IRON_AXE);
		melee("Diamond Axe", 1400, "40 per hit, 3 hits (2 critical); slow swing", Items.DIAMOND_AXE);
		melee("Netherite Axe", 2600, "45 per hit, 3 hits (2 critical); slow swing", Items.NETHERITE_AXE);
		melee("Mace", 3000, "30 per hit plus fall damage: jump-hits deal 55+", Items.MACE);

		add(Category.RANGED, "Bow", 300, "Full draw 36-66: two arrows kill", 1, Kind.ITEMS, () -> List.of(new ItemStack(Items.BOW)));
		add(Category.RANGED, "Arrows x16", 100, "Ammunition for bows and crossbows (carry 64)", 64, Kind.ITEMS, () -> List.of(new ItemStack(Items.ARROW, 16)));
		add(Category.RANGED, "Crossbow", 900, "Loads slowly, stays loaded, hits as hard as a bow", 1, Kind.ITEMS, () -> List.of(new ItemStack(Items.CROSSBOW)));
		add(Category.RANGED, "Trident", 1800, "35 per stab, 40 thrown (pick it back up)", 1, Kind.ITEMS, () -> List.of(new ItemStack(Items.TRIDENT)));

		armor("Leather Armor", 300, "7 armour: 14% less bullet damage", Items.LEATHER_HELMET, Items.LEATHER_CHESTPLATE, Items.LEATHER_LEGGINGS, Items.LEATHER_BOOTS);
		armor("Chainmail Armor", 650, "12 armour: 34% less bullet damage", Items.CHAINMAIL_HELMET, Items.CHAINMAIL_CHESTPLATE, Items.CHAINMAIL_LEGGINGS, Items.CHAINMAIL_BOOTS);
		armor("Iron Armor", 1000, "15 armour: 45% less bullet damage", Items.IRON_HELMET, Items.IRON_CHESTPLATE, Items.IRON_LEGGINGS, Items.IRON_BOOTS);
		armor("Diamond Armor", 2500, "20 armour + toughness: 73% less bullet damage", Items.DIAMOND_HELMET, Items.DIAMOND_CHESTPLATE, Items.DIAMOND_LEGGINGS, Items.DIAMOND_BOOTS);
		armor("Netherite Armor", 3500, "74% less bullet damage, no knockback", Items.NETHERITE_HELMET, Items.NETHERITE_CHESTPLATE, Items.NETHERITE_LEGGINGS, Items.NETHERITE_BOOTS);
		add(Category.ARMOR, "Shield", 1000, "Raise it to block bullets from the front (slows you)", 1, Kind.OFFHAND, () -> List.of(new ItemStack(Items.SHIELD)));
		add(Category.ARMOR, "Totem of Undying", 4000, "Hold it to survive one killing blow", 1, Kind.OFFHAND, () -> List.of(new ItemStack(Items.TOTEM_OF_UNDYING)));

		add(Category.UTILITY, "Ender Pearl", 400, "Throw to teleport (costs 25 HP). Carry 2", 2, Kind.ITEMS, () -> List.of(new ItemStack(Items.ENDER_PEARL)));
		add(Category.UTILITY, "TNT x2", 500, "Up to ~115 damage; walls shield it. Light with flint and steel. Carry 6", 6, Kind.ITEMS,
			() -> List.of(new ItemStack(Items.TNT, 2), new ItemStack(Items.FLINT_AND_STEEL)));
		potion(Items.SPLASH_POTION, "Splash Potion of Healing", 400, "Instantly heals 40 HP. Carry 2", 2, Potions.STRONG_HEALING);
		add(Category.UTILITY, "Golden Apple", 500, "Heals ~20 HP over 5 s and adds 20 HP of absorption. Carry 2", 2, Kind.ITEMS,
			() -> List.of(new ItemStack(Items.GOLDEN_APPLE)));
		potion(Items.POTION, "Potion of Regeneration", 700, "Heals 90 HP over 45 s", 1, Potions.REGENERATION);
		potion(Items.POTION, "Potion of Strength", 800, "+3 melee damage for 3 min (a diamond sword hits for 43)", 1, Potions.STRENGTH);
		potion(Items.POTION, "Potion of Swiftness", 300, "20% faster for 3 min", 1, Potions.SWIFTNESS);
		potion(Items.POTION, "Potion of Leaping", 200, "Jump half a block higher for 3 min", 1, Potions.LEAPING);
		potion(Items.POTION, "Potion of Slow Falling", 150, "No fall damage for 90 s", 1, Potions.SLOW_FALLING);
		add(Category.UTILITY, "Water Bucket", 200, "Break a fall, put out fire", 1, Kind.ITEMS, () -> List.of(new ItemStack(Items.WATER_BUCKET)));

		blocks("Oak Planks x32", 100, "Stops bullets until CS2 wood penetration gets through", Items.OAK_PLANKS, 32);
		blocks("Cobblestone x32", 150, "Resists TNT better than wood", Items.COBBLESTONE, 32);
		blocks("Glass x16", 100, "See-through cover", Items.GLASS, 16);
		blocks("Obsidian x8", 600, "TNT cannot break it", Items.OBSIDIAN, 8);
		blocks("Ladder x16", 100, "Climb anywhere", Items.LADDER, 16);
		blocks("Cooked Beef x8", 50, "Food keeps you sprinting (it does not heal)", Items.COOKED_BEEF, 8);
	}

	private Shop() {
	}

	public static List<Offer> offers() {
		return OFFERS;
	}

	public static @Nullable Offer offer(int id) {
		return id >= 0 && id < OFFERS.size() ? OFFERS.get(id) : null;
	}

	/** CS2's default loadout, the Minecraft way: what every life starts with (the knife and pistol). */
	public static List<ItemStack> defaultKit() {
		return List.of(new ItemStack(Items.STONE_SWORD), new ItemStack(Items.BOW), new ItemStack(Items.ARROW, 4), new ItemStack(Items.OAK_PLANKS, 16),
			new ItemStack(Items.COOKED_BEEF, 4));
	}

	/** Why this player cannot buy the offer right now, or null. Money and buy rules are CS2's and checked again there. */
	public static @Nullable String refusal(Player player, Offer offer, int money, boolean canBuy) {
		if (!canBuy) {
			return "You can only buy in a buy zone during buy time";
		}
		if (money < offer.price()) {
			return "Not enough money";
		}
		if (offer.kind() == Kind.ARMOR && wearing(player, offer)) {
			return "Already wearing it";
		}
		if (offer.limit() > 0) {
			ItemStack first = offer.icon();
			if (carried(player, first) + first.getCount() > offer.limit()) {
				return "You can't carry more";
			}
		}
		return null;
	}

	/** Runs on the server thread once CS2 has charged for the offer. */
	public static void give(ServerPlayer player, Offer offer) {
		List<ItemStack> stacks = offer.items().get();
		switch (offer.kind()) {
			case ARMOR -> {
				for (int i = 0; i < stacks.size() && i < ARMOR_SLOTS.length; i++) {
					player.setItemSlot(ARMOR_SLOTS[i], stacks.get(i));
				}
			}
			case OFFHAND -> {
				if (player.getItemBySlot(EquipmentSlot.OFFHAND).isEmpty()) {
					player.setItemSlot(EquipmentSlot.OFFHAND, stacks.getFirst());
				} else {
					store(player, stacks.getFirst());
				}
			}
			case ITEMS -> {
				for (int i = 0; i < stacks.size(); i++) {
					ItemStack stack = stacks.get(i);
					// Bundled extras (the flint and steel with TNT) only when the player has none.
					if (i > 0 && carried(player, stack) > 0) {
						continue;
					}
					store(player, stack);
				}
			}
		}
		player.containerMenu.broadcastChanges();
		SkyCraft.LOG.info("CS2Craft: {} bought {} for ${}", player.getName().getString(), offer.name(), offer.price());
	}

	private static void store(ServerPlayer player, ItemStack stack) {
		if (!player.getInventory().add(stack) && !stack.isEmpty()) {
			player.drop(stack, false, net.minecraft.util.Prediction.SERVER_ONLY);
		}
	}

	private static boolean wearing(Player player, Offer offer) {
		List<ItemStack> stacks = offer.items().get();
		for (int i = 0; i < stacks.size() && i < ARMOR_SLOTS.length; i++) {
			if (!player.getItemBySlot(ARMOR_SLOTS[i]).is(stacks.get(i).getItem())) {
				return false;
			}
		}
		return true;
	}

	/** How many of this item (same potion, for potions) the player carries, hands and armour included. */
	public static int carried(Player player, ItemStack like) {
		Inventory inventory = player.getInventory();
		int count = 0;
		for (int i = 0; i < inventory.getContainerSize(); i++) {
			ItemStack stack = inventory.getItem(i);
			if (stack.is(like.getItem()) && Objects.equals(stack.get(DataComponents.POTION_CONTENTS), like.get(DataComponents.POTION_CONTENTS))) {
				count += stack.getCount();
			}
		}
		return count;
	}

	private static void add(Category category, String name, int price, String description, int limit, Kind kind, Supplier<List<ItemStack>> items) {
		OFFERS.add(new Offer(OFFERS.size(), category, name, price, description, limit, kind, items));
	}

	private static void melee(String name, int price, String description, Item item) {
		add(Category.MELEE, name, price, description, 1, Kind.ITEMS, () -> List.of(new ItemStack(item)));
	}

	private static void armor(String name, int price, String description, Item head, Item chest, Item legs, Item feet) {
		add(Category.ARMOR, name, price, description, 0, Kind.ARMOR,
			() -> List.of(new ItemStack(head), new ItemStack(chest), new ItemStack(legs), new ItemStack(feet)));
	}

	private static void potion(Item item, String name, int price, String description, int limit, Holder<Potion> potion) {
		add(Category.UTILITY, name, price, description, limit, Kind.ITEMS, () -> List.of(PotionContents.createItemStack(item, potion)));
	}

	private static void blocks(String name, int price, String description, Item item, int count) {
		add(Category.BLOCKS, name, price, description, 0, Kind.ITEMS, () -> List.of(new ItemStack(item, count)));
	}
}

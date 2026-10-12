package dev.skycraft.combat;

import java.util.Map;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.projectile.Projectile;
import net.minecraft.world.entity.projectile.arrow.AbstractArrow;
import net.minecraft.world.entity.projectile.arrow.ThrownTrident;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;

/**
 * Converts the damage Minecraft computed for a hit on a CS2 bot's stand-in into CS2 health points
 * (bots have 100). Minecraft has already applied attack charge, crits, Sharpness, Strength and
 * immunity frames; only the scale changes.
 *
 * <p>Melee weapons map their full-strength, non-critical hit to a fixed CS2 value, calibrated
 * against CS2's knife (40 per slash). Better materials hit harder, a diamond sword needs four clean
 * hits (three crits), and axes trade swing speed for damage. Spam clicking still deals Minecraft's
 * reduced partial-charge damage.
 */
public final class Cs2Damage {
	/** item -> {Minecraft full-strength damage (incl. the player's base 1), CS2 damage for that hit}. */
	private static final Map<Item, float[]> MELEE = Map.ofEntries(
		Map.entry(Items.WOODEN_SWORD, new float[] { 4, 15 }),
		Map.entry(Items.GOLDEN_SWORD, new float[] { 4, 15 }),
		Map.entry(Items.STONE_SWORD, new float[] { 5, 20 }),
		Map.entry(Items.COPPER_SWORD, new float[] { 5, 22 }),
		Map.entry(Items.IRON_SWORD, new float[] { 6, 25 }),
		Map.entry(Items.DIAMOND_SWORD, new float[] { 7, 30 }),
		Map.entry(Items.NETHERITE_SWORD, new float[] { 8, 35 }),
		Map.entry(Items.WOODEN_AXE, new float[] { 7, 25 }),
		Map.entry(Items.GOLDEN_AXE, new float[] { 7, 25 }),
		Map.entry(Items.STONE_AXE, new float[] { 9, 30 }),
		Map.entry(Items.COPPER_AXE, new float[] { 9, 32 }),
		Map.entry(Items.IRON_AXE, new float[] { 9, 35 }),
		Map.entry(Items.DIAMOND_AXE, new float[] { 9, 40 }),
		Map.entry(Items.NETHERITE_AXE, new float[] { 10, 45 }),
		Map.entry(Items.TRIDENT, new float[] { 9, 35 }),
		Map.entry(Items.MACE, new float[] { 6, 30 })
	);
	/** Fists, tools and anything else held: a punch (1) is 4 HP. */
	private static final float DEFAULT_FACTOR = 4.0F;
	/** Arrows: a full-draw bow shot (6) is 42 HP; a critical full draw (~9-10) is about 65. */
	private static final float ARROW_FACTOR = 7.0F;
	/** Thrown trident (8): 40 HP. */
	private static final float THROWN_TRIDENT_FACTOR = 5.0F;

	private Cs2Damage() {
	}

	public static float toCs2(DamageSource source, float minecraftDamage) {
		if (source.getDirectEntity() instanceof ThrownTrident) {
			return minecraftDamage * THROWN_TRIDENT_FACTOR;
		}
		if (source.getDirectEntity() instanceof AbstractArrow) {
			return minecraftDamage * ARROW_FACTOR;
		}
		if (source.getDirectEntity() instanceof Projectile) {
			return minecraftDamage * DEFAULT_FACTOR; // snowballs, eggs, wind charges: mostly 0
		}
		ItemStack weapon = source.getWeaponItem();
		float[] entry = weapon == null || weapon.isEmpty() ? null : MELEE.get(weapon.getItem());
		return entry == null ? minecraftDamage * DEFAULT_FACTOR : minecraftDamage * entry[1] / entry[0];
	}
}

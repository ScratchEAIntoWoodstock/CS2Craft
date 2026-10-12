package dev.skycraft.world;

import dev.skycraft.SkyCraft;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import net.minecraft.core.BlockPos;
import net.minecraft.core.SectionPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.item.FallingBlockEntity;
import net.minecraft.world.entity.item.PrimedTnt;
import net.minecraft.world.level.ChunkPos;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.chunk.LevelChunk;
import net.minecraft.world.level.chunk.LevelChunkSection;

/**
 * CS2 round reset: the mirror world is an empty void, so every block in it was placed by a player.
 * Removes them (and falling blocks / lit TNT) from every loaded chunk near the players.
 */
public final class RoundReset {
	// Loaded chunks around a player never exceed render/simulation distance (8), plus margin.
	private static final int CHUNK_RADIUS = 16;

	private RoundReset() {
	}

	/** Runs on the server thread. Returns the number of blocks removed. */
	public static int clear(ServerLevel level) {
		// No drops, no neighbour cascades: everything around is being removed too.
		int flags = Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS;
		BlockState air = Blocks.AIR.defaultBlockState();
		BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
		Set<Long> visited = new HashSet<>();
		int removed = 0;
		for (ServerPlayer player : level.players()) {
			ChunkPos center = player.chunkPosition();
			for (int cx = center.x() - CHUNK_RADIUS; cx <= center.x() + CHUNK_RADIUS; cx++) {
				for (int cz = center.z() - CHUNK_RADIUS; cz <= center.z() + CHUNK_RADIUS; cz++) {
					if (!visited.add(ChunkPos.pack(cx, cz))) {
						continue;
					}
					LevelChunk chunk = level.getChunkSource().getChunkNow(cx, cz);
					if (chunk == null) {
						continue;
					}
					LevelChunkSection[] sections = chunk.getSections();
					for (int i = 0; i < sections.length; i++) {
						LevelChunkSection section = sections[i];
						if (section.hasOnlyAir()) {
							continue;
						}
						int baseY = SectionPos.sectionToBlockCoord(chunk.getSectionYFromSectionIndex(i));
						for (int y = 0; y < 16; y++) {
							for (int z = 0; z < 16; z++) {
								for (int x = 0; x < 16; x++) {
									if (!section.getBlockState(x, y, z).isAir()) {
										pos.set(cx * 16 + x, baseY + y, cz * 16 + z);
										if (level.setBlock(pos, air, flags)) {
											removed++;
										}
									}
								}
							}
						}
					}
				}
			}
		}
		List<Entity> loose = new ArrayList<>();
		for (Entity entity : level.getAllEntities()) {
			if (entity instanceof FallingBlockEntity || entity instanceof PrimedTnt) {
				loose.add(entity);
			}
		}
		loose.forEach(Entity::discard);
		SkyCraft.LOG.info("CS2Craft: round reset removed {} blocks and {} falling/TNT entities", removed, loose.size());
		return removed;
	}
}

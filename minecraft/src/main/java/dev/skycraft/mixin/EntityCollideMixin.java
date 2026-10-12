package dev.skycraft.mixin;
import com.llamalad7.mixinextras.injector.wrapmethod.WrapMethod;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import dev.skycraft.world.SkyCollider;
import dev.skycraft.world.SkyCollision;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.phys.Vec3;
import org.spongepowered.asm.mixin.Mixin;
@Mixin(Entity.class)
public abstract class EntityCollideMixin {
    @WrapMethod(method = "collide")
    private Vec3 cs2craft$move(Vec3 movement, Operation<Vec3> original) {
        if (!((Object)this instanceof Player player) || !SkyCollision.exactEntity(player) || player.noPhysics)
            return original.call(movement);
        SkyCollision.movementQuery(true);
        try { return SkyCollider.collide(player, original.call(movement)); }
        finally { SkyCollision.movementQuery(false); }
    }
}

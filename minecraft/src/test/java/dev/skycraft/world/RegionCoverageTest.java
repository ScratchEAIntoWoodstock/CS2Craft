package dev.skycraft.world;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
class RegionCoverageTest {
    @Test void fallingAcrossUnloadedFloorBoundaryIsHeld() {
        assertFalse(RegionCoverage.contains(1,-.1,1,1.6,1.8,1.6,(x,y,z)->y==0));
        assertTrue(RegionCoverage.contains(1,-.1,1,1.6,1.8,1.6,(x,y,z)->y>=-1&&y<=0));
    }
    @Test void bodyMustFitInsideLoadedHorizontalRegions() {
        assertFalse(RegionCoverage.contains(7.6,1,1,8.2,2.8,1.6,(x,y,z)->x==0));
        assertTrue(RegionCoverage.contains(7.6,1,1,8.2,2.8,1.6,(x,y,z)->x<=1));
    }
    @Test void negativeCoordinatesUseTheCorrectRegion() {
        assertTrue(RegionCoverage.contains(-8,1,-8,-7.4,2.8,-7.4,(x,y,z)->x==-1&&y==0&&z==-1));
        assertFalse(RegionCoverage.contains(-8.01,1,-8,-7.4,2.8,-7.4,(x,y,z)->x==-1&&y==0&&z==-1));
    }
}

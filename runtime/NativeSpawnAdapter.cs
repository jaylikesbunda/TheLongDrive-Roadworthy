using System.Collections.Generic;

namespace TldMp
{
    internal static class NativeSpawnAdapter
    {
        internal const int SourcePoi = 1;
        internal const int SourceSave = 2;
        internal const int SourceFarPlace = 3;
        internal const int SourceDeveloper = 4;

        internal static long PoiKey(double posX, double posY, double posZ, int poiId)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                hash = (hash ^ posX.GetHashCode()) * 1099511628211L;
                hash = (hash ^ posY.GetHashCode()) * 1099511628211L;
                hash = (hash ^ posZ.GetHashCode()) * 1099511628211L;
                hash = (hash ^ poiId) * 1099511628211L;
                return hash;
            }
        }

        internal static SpawnJobKey PoiIdentity(long generation, double posX, double posY, double posZ, int poiId, object owner)
        {
            return new SpawnJobKey(generation, SourcePoi, PoiKey(posX, posY, posZ, poiId), owner);
        }

        internal static SpawnJobKey FarPlaceIdentity(long generation, uint savedItemId)
        {
            return new SpawnJobKey(generation, SourceFarPlace, savedItemId, null);
        }

        internal static SpawnJob PoiJob(SpawnJobKey identity, SpawnUnit unit)
        {
            return new SpawnJob(identity, new List<SpawnUnit> { unit }, null, 1, 0);
        }

        internal static SpawnJob FarPlaceJob(SpawnJobKey identity, SpawnUnit unit)
        {
            return new SpawnJob(identity, new List<SpawnUnit> { unit }, null, 1, 0);
        }
    }
}

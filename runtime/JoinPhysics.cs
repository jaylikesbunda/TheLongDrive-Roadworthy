using UnityEngine;

namespace TldMp
{
    // Client join physics gate. While the local client is waiting for the world
    // baseline, host-owned cars and cargo have no loaded support underneath them
    // and a single frame of free-fall can end hundreds of metres underground.
    // Stop automatic simulation for the whole client scene instead of touching
    // individual bodies. The mode the game was already using is restored exactly;
    // host simulation, ownership, messages and saves are untouched.
    public static partial class StableGameBridge
    {
        static bool joinPhysicsFrozen;
        static SimulationMode joinPhysicsSaved;

        internal static void TickJoinPhysics()
        {
            // Hold through the whole client join, including the seated/death paths
            // that release the player spawn hold early. Only a client that is
            // connected, error-free and not yet Live is frozen.
            if(!ShouldFreezeJoinPhysics())
            {
                ResumeJoinPhysics();
                return;
            }
            if(joinPhysicsFrozen)return;
            joinPhysicsSaved=Physics.simulationMode;
            joinPhysicsFrozen=true;
            Physics.simulationMode=SimulationMode.Script;
            JoinDiagnostic("join physics frozen was="+joinPhysicsSaved);
        }

        static void ResumeJoinPhysics()
        {
            if(!joinPhysicsFrozen)return;
            joinPhysicsFrozen=false;
            Physics.simulationMode=joinPhysicsSaved;
            JoinDiagnostic("join physics resumed mode="+joinPhysicsSaved);
        }

        static bool ShouldFreezeJoinPhysics()
        {
            return Connected && node!=null && !node.IsHost && node.Error==null && !node.Ready;
        }
    }
}
